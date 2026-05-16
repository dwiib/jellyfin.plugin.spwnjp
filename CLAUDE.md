# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Jellyfin server plugin that acts as a **metadata provider for [spwn.jp](https://spwn.jp) events** — concert movies and other live-event releases sold through spwn.jp. Jellyfin calls into the plugin during library scans (and on demand from the Identify dialog) to fetch event titles, performer lists, descriptions, dates, and backdrop artwork.

The project was forked from `jellyfin/jellyfin-plugin-template` (see the initial commit). Most of the scaffolding — `Plugin.cs`, `PluginConfiguration.cs`, `configPage.html` — is still the unmodified template plumbing; the spwn.jp integration itself has not been implemented yet.

## Architecture (planned)

The plugin needs to fetch spwn.jp pages and parse the resulting HTML for event metadata. Page fetching has two interchangeable backends, selected at runtime by config:

```
Jellyfin ──> SpwnjpMetadataProvider ──> IPageFetcher ──> spwn.jp
                                            │
                                            ├── direct HttpClient   (default; HeadlessShellUrl unset)
                                            └── headless-shell CDP  (when HeadlessShellUrl is configured)
```

Headless-shell is **optional**. spwn.jp is a Firebase-backed SPA, so some pages won't have their event data in the raw HTTP response — the headless path exists to recover those pages by returning the post-render DOM. If `Configuration.HeadlessShellUrl` is empty or whitespace, the plugin uses a plain `HttpClient` and lives with whatever the server returns. If the URL is set, the plugin must use it (and surface an error if it's unreachable) — falling back silently would hide misconfiguration.

Concrete pieces this implies:

- **Abstraction.** A single `IPageFetcher` (or equivalent) with one method along the lines of `Task<string> FetchHtmlAsync(Uri url, CancellationToken ct)`. Two implementations behind that interface; choose by reading `Plugin.Instance!.Configuration.HeadlessShellUrl` at construction. Constructor-inject `IHttpClientFactory` from Jellyfin's DI for the direct path.
- **Headless-shell client.** A thin Chrome DevTools Protocol client over WebSocket: open a target on the requested URL, wait for the page to settle, evaluate `document.documentElement.outerHTML`, close the target. The base URL is whatever the user configured. Reachability/validity is checked with `GET /json/version` — that endpoint returns the standard Chrome DevTools version JSON; treat a 2xx with a parseable body as "valid."
- **Search.** `https://spwn.jp/search?keyword=<keyword>` returns the event search results page. Fed by a keyword derived from the item's filename when Jellyfin asks for search results.
- **Event detail.** `https://spwn.jp/events/<event-id>` (e.g. `evt_qB2HIXL5QZpwoeqcAKRD`) is the canonical event page. Fed directly when the user has pinned an event ID, or via the URL from a chosen search result.
- **Images.** Image URLs on the event page have the form `https://public-web.spwn.jp/events/<uuid>_1280x720`. These are plain CDN assets — a direct `GET` returns the bytes, no JS rendering needed, so the headless path doesn't apply. (It's not really an option either: headless-shell is a browser controlled over CDP, not a generic byte proxy. Pulling image bytes through it would mean wiring up `Network.getResponseBody` after a navigation just to retrieve what a one-line `HttpClient.GetAsync` already gives you.) Jellyfin's `IRemoteImageProvider.GetImageResponse` expects an `HttpClient`-style fetch anyway. Drop the `_1280x720` suffix to fetch original resolution.

## Jellyfin provider wiring

The plugin needs to register with Jellyfin's metadata pipeline as three things, all auto-discovered by reflection on plugin load:

1. **`IRemoteMetadataProvider<Movie, MovieInfo>`** — `GetSearchResults(MovieInfo, …)` runs the filename search; `GetMetadata(MovieInfo, …)` resolves the event detail page and populates a `MetadataResult<Movie>`. If `info.ProviderIds["Spwnjp"]` is set, skip the search and go straight to the detail page.
2. **`IRemoteImageProvider`** — supplies the backdrop image URL. `GetImages` returns one `RemoteImageInfo` with `Type = ImageType.Backdrop` and the original-resolution URL.
3. **`IExternalId`** — registers `Spwnjp` (or whatever string is chosen as the provider key) as a known external ID. This surfaces the **Spwn event ID** field on the metadata edit page in the web UI. The string typed there flows into `MovieInfo.ProviderIds[<key>]` on subsequent metadata calls.

All three must use the **same key string** (case sensitive) for the `ProviderIds` dictionary lookup to work end-to-end. Constant it once in the plugin.

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

There are no unit tests in this repo yet. CI (`.github/workflows/`) delegates build/test to the shared `jellyfin/jellyfin-meta-plugins` workflows.

## Version coupling (easy to get wrong)

Three files reference framework / ABI versions that must stay consistent, but they currently disagree — when bumping, update **all of them together**:

- `Jellyfin.Plugin.Spwnjp/Jellyfin.Plugin.Spwnjp.csproj` — `<TargetFramework>` (currently `net9.0`) and the `Jellyfin.Controller` / `Jellyfin.Model` PackageReference versions (currently `10.9.11`). The Controller version must match the **installed Jellyfin server version**, or the plugin loads as `NotSupported`.
- `build.yaml` — `framework` (currently `net8.0` — mismatches the csproj) and `targetAbi` (`10.9.0.0`).
- `.vscode/tasks.json` — `copy-dll` reads from `bin/Debug/net9.0/publish/`, so the framework moniker is hardcoded here too.

`PackageReference` entries for `Jellyfin.Controller` / `Jellyfin.Model` must include `<ExcludeAssets>runtime</ExcludeAssets>` — otherwise Jellyfin's own assemblies get copied into the plugin folder and the plugin fails to register.

## Plugin anatomy

Three pieces wire a Jellyfin plugin together:

1. **`Plugin.cs`** — inherits `BasePlugin<PluginConfiguration>`. Exposes `Name`, a stable `Id` GUID, and a static `Instance` singleton set in the constructor (the standard Jellyfin pattern — other classes read config via `Plugin.Instance!.Configuration`). The `Id` here (`1665ca06-677c-4f4e-9292-72552207d00e`) must match `guid:` in `build.yaml` and `pluginUniqueId` in `configPage.html`.
2. **`Configuration/PluginConfiguration.cs`** — inherits `BasePluginConfiguration`. Public properties become serialized settings. Defaults set in the constructor apply when no saved config exists. The headless-shell URL field will live here.
3. **`Configuration/configPage.html`** — the settings page rendered in the Jellyfin dashboard. Implementing `IHasWebPages` on `Plugin` and returning a `PluginPageInfo` with `EmbeddedResourcePath = "{Namespace}.Configuration.configPage.html"` registers it. The `.csproj` marks the file as `<EmbeddedResource>`.

If the settings button doesn't appear in Jellyfin, the most common causes are: missing `IHasWebPages` implementation, the HTML not embedded, the resource path string not matching the namespace, or the DLL not actually deployed.

## Code style enforcement

The project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`, with the shared `jellyfin.ruleset` and three analyzer packages (StyleCop, SerilogAnalyzer, SmartAnalyzers.MultithreadingAnalyzer). Practical consequence: **every public member needs an XML doc comment**, nullable annotations must be correct, and most StyleCop rules are on. Builds fail on analyzer warnings, not just compile errors.
