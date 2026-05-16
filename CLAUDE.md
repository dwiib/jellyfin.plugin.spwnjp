# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A Jellyfin server plugin that acts as a **metadata provider for [spwn.jp](https://spwn.jp) events** — concert movies and similar event-based media. Jellyfin invokes the plugin to look up event metadata when scanning matching items. There is no "application" to run on its own — the output DLL is loaded by a running Jellyfin server.

The project was forked from `jellyfin/jellyfin-plugin-template` (see the initial commit). Most of the scaffolding — `Plugin.cs`, `PluginConfiguration.cs`, `configPage.html` — is still the unmodified template plumbing; the actual spwn.jp integration has not been implemented yet.

## Build / deploy

```bash
# Build (and publish) the plugin
dotnet publish --configuration=Debug Jellyfin.Plugin.Spwnjp.sln \
    /property:GenerateFullPaths=true /consoleloggerparameters:NoSummary
```

Built artifacts land in `Jellyfin.Plugin.Spwnjp/bin/Debug/net9.0/publish/`. To run, copy the contents into a Jellyfin plugins subfolder (Linux: `$HOME/.local/share/jellyfin/plugins/Jellyfin.Plugin.Spwnjp/`, Windows: `%LOCALAPPDATA%/jellyfin/plugins/Jellyfin.Plugin.Spwnjp/`), then restart Jellyfin. The VS Code `build-and-copy` task in `.vscode/tasks.json` automates this — paths are configured by `pluginName`, `jellyfinLinuxDataDir`, and `jellyfinWindowsDataDir` in `.vscode/settings.json`.

There are no unit tests in this repo. CI (`.github/workflows/`) delegates build/test to the shared `jellyfin/jellyfin-meta-plugins` workflows. Note: `scan-codeql.yaml` and `changelog.yaml` still hardcode `repository-name: jellyfin/jellyfin-plugin-template` — update these when a new origin is set.

## Version coupling (easy to get wrong)

Three files reference framework / ABI versions that must stay consistent, but they currently disagree — when bumping, update **all of them together**:

- `Jellyfin.Plugin.Spwnjp/Jellyfin.Plugin.Spwnjp.csproj` — `<TargetFramework>` (currently `net9.0`) and the `Jellyfin.Controller` / `Jellyfin.Model` PackageReference versions (currently `10.9.11`). The Controller version must match the **installed Jellyfin server version**, or the plugin loads as `NotSupported`.
- `build.yaml` — `framework` (currently `net8.0` — mismatches the csproj) and `targetAbi` (`10.9.0.0`).
- `.vscode/tasks.json` — `copy-dll` reads from `bin/Debug/net9.0/publish/`, so the framework moniker is hardcoded here too.

`PackageReference` entries for `Jellyfin.Controller` / `Jellyfin.Model` must include `<ExcludeAssets>runtime</ExcludeAssets>` — otherwise Jellyfin's own assemblies get copied into the plugin folder and the plugin fails to register.

## Plugin anatomy

Three pieces wire a Jellyfin plugin together:

1. **`Plugin.cs`** — inherits `BasePlugin<PluginConfiguration>`. Exposes `Name`, a stable `Id` GUID, and a static `Instance` singleton set in the constructor (the standard Jellyfin pattern — other classes read config via `Plugin.Instance!.Configuration`). The `Id` here (`1665ca06-677c-4f4e-9292-72552207d00e`) must match `guid:` in `build.yaml` and `pluginUniqueId` in `configPage.html`.
2. **`Configuration/PluginConfiguration.cs`** — inherits `BasePluginConfiguration`. Public properties become serialized settings. Defaults set in the constructor apply when no saved config exists.
3. **`Configuration/configPage.html`** — the settings page rendered in the Jellyfin dashboard. Implementing `IHasWebPages` on `Plugin` and returning a `PluginPageInfo` with `EmbeddedResourcePath = "{Namespace}.Configuration.configPage.html"` registers it. The `.csproj` marks the file as `<EmbeddedResource>`.

If the settings button doesn't appear in Jellyfin, the most common causes are: missing `IHasWebPages` implementation, the HTML not embedded, the resource path string not matching the namespace, or the DLL not actually deployed.

## What still needs to be built (metadata provider work)

To make this an actual spwn.jp metadata provider, the plugin needs to implement Jellyfin's metadata provider interfaces. The relevant ones live in `MediaBrowser.Controller.Providers`:

- `IRemoteMetadataProvider<TItem, TLookupInfo>` — implement for each Jellyfin item type you want to enrich (likely `Movie` with `MovieInfo`, possibly `MusicVideo`). Provides `GetMetadata(TLookupInfo, CancellationToken)` and `GetSearchResults(TLookupInfo, CancellationToken)`.
- `IRemoteImageProvider` — for poster/backdrop artwork from spwn.jp.

Providers are auto-discovered by Jellyfin via reflection on plugin load — no explicit registration needed beyond implementing the interface. HTTP calls should go through an `IHttpClientFactory` injected via constructor (Jellyfin's DI handles this).

## Code style enforcement

The project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and `<AnalysisMode>AllEnabledByDefault</AnalysisMode>`, with the shared `jellyfin.ruleset` and three analyzer packages (StyleCop, SerilogAnalyzer, SmartAnalyzers.MultithreadingAnalyzer). Practical consequence: **every public member needs an XML doc comment**, nullable annotations must be correct, and most StyleCop rules are on. Builds fail on analyzer warnings, not just compile errors.
