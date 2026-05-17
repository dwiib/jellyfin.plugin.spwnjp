# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Jellyfin server plugin that acts as a **metadata provider for [spwn.jp](https://spwn.jp) events** — concert movies and other live-event releases sold through spwn.jp. Jellyfin calls into the plugin during library scans (and on demand from the Identify dialog) to fetch event titles, performer lists, descriptions, dates, and backdrop artwork.

The project was forked from `jellyfin/jellyfin-plugin-template` (see the initial commit) and the workflow is fully wired end-to-end. Three projects:
- **`Jellyfin.Plugin.Spwnjp.Core/`** — Jellyfin-independent library (HtmlAgilityPack only). Owns page fetching, parsing, models, and the `SpwnClient` facade.
- **`Jellyfin.Plugin.Spwnjp/`** — the actual Jellyfin plugin. Configuration + three provider adapters in `Providers/`. References Core.
- **`Jellyfin.Plugin.Spwnjp.Cli/`** — `dotnet run` harness for testing the same code paths without Jellyfin. References Core only.

## Architecture

The plugin fetches spwn.jp pages and parses the resulting HTML for event metadata. Page fetching has two interchangeable backends, selected at runtime by config:

```
Jellyfin ──> SpwnjpMetadataProvider ──> IPageFetcher ──> spwn.jp
                                            │
                                            ├── direct HttpClient   (default; HeadlessShellUrl unset)
                                            └── headless-shell CDP  (when HeadlessShellUrl is configured)
```

Headless-shell is **optional**. spwn.jp is a Firebase-backed SPA, so some pages won't have their event data in the raw HTTP response — the headless path exists to recover those pages by returning the post-render DOM. If `Configuration.HeadlessShellUrl` is empty or whitespace, the plugin uses a plain `HttpClient` and lives with whatever the server returns. If the URL is set, the plugin must use it (and surface an error if it's unreachable) — falling back silently would hide misconfiguration.

Concrete pieces (implemented):

- **Abstraction.** `Core/IPageFetcher.cs` — single method `FetchHtmlAsync(Uri, CT) → PageFetchResult`. Two implementations: `DirectPageFetcher` and `CdpPageFetcher`. Selected at fetcher construction by `Providers/PageFetcherFactory.cs` reading `Plugin.Instance.Configuration.HeadlessShellUrl`.
- **Headless-shell client.** `Core/CdpPageFetcher.cs` drives CDP over WebSocket: `PUT /json/new?<url>`, connect to the returned `webSocketDebuggerUrl`, poll `Runtime.evaluate` for a caller-supplied readiness expression (best-effort, 10s timeout, falls through and captures whatever's in the DOM if the marker never appears), evaluate `document.documentElement.outerHTML`, `GET /json/close/<id>`. The host portion of the WS URL is rewritten to the configured base URL's host so the fetcher works when headless-shell isn't on localhost. Reachability checked via `GET /json/version`. `SpwnClient` picks the readiness expression per page type: `#act_info article h3` for event detail (waits for the cast section to render), and `ul[aria-label="イベント検索結果一覧"] || #container has 検索結果がありません` for search (covers both hits-present and no-results cases).
- **Search.** `Core/Urls.SearchUrl(keyword)` builds `https://spwn.jp/search?keyword=<kw>`. Parsed by `Core/Parsing/SearchPageParser.cs` — anchors on `aria-label="イベント検索結果一覧"` and `<a href="/events/evt_…">`. Returns `SpwnSearchResult[]`.
- **Event detail.** `Core/Urls.EventUrl(eventId)`. Parsed by `Core/Parsing/EventPageParser.cs` — anchors on the public-web.spwn.jp image src pattern (for title via alt + backdrop URL), `id="act_info"` (for the performer list), `translate-this-block` class (for description), and a `\d{4}/\d{2}/\d{2}` regex (for date).
- **Image URL normalisation.** `Core/Parsing/ImageUrl.StripSizeSuffix` strips the `_WIDTHxHEIGHT` suffix to fetch original-resolution CDN assets.
- **High-level facade.** `Core/SpwnClient.cs` — `GetEventAsync(id)` and `SearchAsync(keyword)`. Consumed identically by the CLI harness and the Jellyfin providers.

## Jellyfin provider wiring

The plugin registers three things with Jellyfin's metadata pipeline, all in `Jellyfin.Plugin.Spwnjp/Providers/` and auto-discovered by reflection on plugin load:

1. **`SpwnjpMetadataProvider : IRemoteMetadataProvider<Movie, MovieInfo>`** — `GetSearchResults` returns the pinned event directly if `info.ProviderIds["Spwnjp"]` is set, otherwise runs a keyword search using `searchInfo.Name`. `GetMetadata` resolves the event-detail page and populates `MetadataResult<Movie>` with `Name`, `Tagline`, `Overview`, `PremiereDate`, `ProductionYear`, plus one `PersonInfo { Type = PersonKind.Actor }` per performer.
2. **`SpwnjpImageProvider : IRemoteImageProvider`** — returns one `RemoteImageInfo { Type = Backdrop }` with the original-resolution URL. `GetImageResponse` uses a plain `HttpClient` from `IHttpClientFactory` (CDN GET; never CDP).
3. **`SpwnjpExternalId : IExternalId`** — registers `Spwnjp` as a movie external id (`ExternalIdMediaType.Movie`), surfacing the **Spwn event ID** field on the metadata edit page.

All three pull the provider key (`"Spwnjp"`) from `SpwnjpConstants.ProviderKey` — never hardcode it inline.

## Metadata field mapping

| Jellyfin target on `MetadataResult<Movie>` | Source on the event detail page |
| --- | --- |
| `Item.Name` | Event title |
| `Item.Tagline` | Performer list line, verbatim (separators are preserved as displayed) |
| `Item.Overview` | Event description block |
| `Item.PremiereDate` / `Item.ProductionYear` | Event date — page format is `YYYY/MM/DD` |
| `People` (via `MetadataResult.AddPerson`) | One `PersonInfo { Type = PersonKind.Actor, Name = … }` per performer |
| Backdrop image (separate `IRemoteImageProvider`) | `https://public-web.spwn.jp/events/<uuid>` with the `_1280x720` size suffix stripped |

## Build / deploy

```bash
# Build (and publish) the plugin
dotnet publish --configuration=Debug Jellyfin.Plugin.Spwnjp.sln \
    /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
```

Built artifacts land in `Jellyfin.Plugin.Spwnjp/bin/Debug/net9.0/publish/`. To run, copy the contents into a Jellyfin plugins subfolder (Linux: `$HOME/.local/share/jellyfin/plugins/Jellyfin.Plugin.Spwnjp/`, Windows: `%LOCALAPPDATA%/jellyfin/plugins/Jellyfin.Plugin.Spwnjp/`), then restart Jellyfin. The VS Code `build-and-copy` task in `.vscode/tasks.json` automates this — paths are configured by `pluginName`, `jellyfinLinuxDataDir`, and `jellyfinWindowsDataDir` in `.vscode/settings.json`.

The plugin csproj runs **ILRepack** at the `Publish` target to merge `Jellyfin.Plugin.Spwnjp.Core.dll` and `HtmlAgilityPack.dll` into the main plugin assembly, with `Internalize=true` so the merged types don't collide with another plugin that pulls the same packages. The publish folder ends up with a single `Jellyfin.Plugin.Spwnjp.dll` (plus the stock `.pdb`, `.xml`, `.deps.json`). This is needed because Jellyfin's per-plugin `AssemblyLoadContext` doesn't reliably resolve transitive NuGet dependencies dropped alongside the plugin DLL. Day-to-day `dotnet build` keeps DLLs separate (faster incremental, simpler CLI debug).

There are no unit tests in this repo yet. CI (`.github/workflows/`) delegates build/test to the shared `jellyfin/jellyfin-meta-plugins` workflows.

## CLI dev harness

`Jellyfin.Plugin.Spwnjp.Cli/` is the iterative-dev driver — exercises the same `IPageFetcher`/`SpwnClient` code paths the Jellyfin providers wrap, without needing a running Jellyfin. Three subcommands:

```bash
# fetch and parse an event detail page (default: direct HTTP)
dotnet run --project Jellyfin.Plugin.Spwnjp.Cli -- event evt_qB2HIXL5QZpwoeqcAKRD

# run a search
dotnet run --project Jellyfin.Plugin.Spwnjp.Cli -- search <keyword>

# fetch raw HTML (no parsing) — for debugging the page shape
dotnet run --project Jellyfin.Plugin.Spwnjp.Cli -- fetch <url>

# route through headless-shell for the SPA-only pages
SPWNJP_HEADLESS_URL=http://localhost:9222 dotnet run --project Jellyfin.Plugin.Spwnjp.Cli -- event <id>
```

Direct HTTP returns a bootstrap shell for event/search pages (data hydrated client-side). CDP path returns ~50× more HTML on event pages and is where the parser is meant to operate. Direct-on-search will reasonably return 0 results — that's not a bug, it's the case headless-shell exists to fix.

## Version coupling (easy to get wrong)

Three files reference framework / ABI versions that must stay consistent, but they currently disagree — when bumping, update **all of them together**:

- `Jellyfin.Plugin.Spwnjp/Jellyfin.Plugin.Spwnjp.csproj` — `<TargetFramework>` (currently `net9.0`) and the `Jellyfin.Controller` / `Jellyfin.Model` PackageReference versions (currently `10.10.7`). The Controller version must match the **installed Jellyfin server version**, or the plugin loads as `NotSupported`.
- `build.yaml` — `framework` (currently `net9.0`) and `targetAbi` (currently `10.10.0.0`).
- `.vscode/tasks.json` — `copy-dll` reads from `bin/Debug/net9.0/publish/`, so the framework moniker is hardcoded here too.

`PackageReference` entries for `Jellyfin.Controller` / `Jellyfin.Model` must include `<ExcludeAssets>runtime</ExcludeAssets>` — otherwise Jellyfin's own assemblies get copied into the plugin folder and the plugin fails to register.

## Plugin anatomy

Three pieces wire a Jellyfin plugin together:

1. **`Plugin.cs`** — inherits `BasePlugin<PluginConfiguration>`. Exposes `Name`, a stable `Id` GUID, and a static `Instance` singleton set in the constructor (the standard Jellyfin pattern — other classes read config via `Plugin.Instance!.Configuration`). The `Id` here (`1665ca06-677c-4f4e-9292-72552207d00e`) must match `guid:` in `build.yaml` and `pluginUniqueId` in `configPage.html`.
2. **`Configuration/PluginConfiguration.cs`** — inherits `BasePluginConfiguration`. Public properties become serialized settings. Defaults set in the constructor apply when no saved config exists. Currently has a single `HeadlessShellUrl` string.
3. **`Configuration/configPage.html`** — the settings page rendered in the Jellyfin dashboard. Implementing `IHasWebPages` on `Plugin` and returning a `PluginPageInfo` with `EmbeddedResourcePath = "{Namespace}.Configuration.configPage.html"` registers it. The `.csproj` marks the file as `<EmbeddedResource>`.

If the settings button doesn't appear in Jellyfin, the most common causes are: missing `IHasWebPages` implementation, the HTML not embedded, the resource path string not matching the namespace, or the DLL not actually deployed.

## Code style enforcement

The project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`, with the shared `jellyfin.ruleset` and three analyzer packages (StyleCop, SerilogAnalyzer, SmartAnalyzers.MultithreadingAnalyzer). Practical consequence: **every public member needs an XML doc comment**, nullable annotations must be correct, and most StyleCop rules are on. Builds fail on analyzer warnings, not just compile errors.
