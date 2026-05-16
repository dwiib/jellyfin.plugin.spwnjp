# Jellyfin.Plugin.Spwnjp

A Jellyfin metadata provider for [spwn.jp](https://spwn.jp) events — primarily concert movies and similar live-event releases sold through spwn.jp. The plugin looks up the corresponding event page and fills in titles, performers, descriptions, dates, and backdrop artwork on matching library items.

## How it works

The plugin fetches spwn.jp pages and parses them for event metadata. By default it uses a regular HTTP client. Because spwn.jp is a Firebase-backed single-page app, some pages don't expose their event data in the raw HTTP response — to handle that, the plugin can **optionally** route page fetches through a user-supplied [headless-shell](https://github.com/chromedp/docker-headless-shell) instance and parse the post-render DOM. If you don't configure one, the plugin just uses the direct HTTP fetch.

## Requirements

- Jellyfin server 10.9.x.

## Configuration

In **Dashboard → Plugins → Spwnjp**:

- **Headless shell URL** *(optional)* — base URL of a headless-shell instance you control, for example `http://localhost:9222`. Leave it empty to use direct HTTP fetches. When set, the plugin validates the URL by issuing `GET /json/version` (the standard Chrome DevTools version endpoint) and routes all page fetches through it; if the URL is set but unreachable, the plugin treats that as a configuration error rather than silently falling back.

## Usage

Two ways a library item gets matched to a spwn.jp event:

1. **Automatic, by filename.** During a library scan, the plugin derives a search keyword from the item's filename and queries `https://spwn.jp/search?keyword=<keyword>`. Matching events come back as search results; Jellyfin selects a best match or surfaces them in the **Identify** dialog.
2. **Manual, by event ID.** Every spwn.jp event has an ID of the form `evt_XXXXXXXXXXXXXXXXXXXX` (visible in the page URL). From the item's **Identify** dialog or its metadata edit page, paste the event ID into the **Spwn event ID** field. The plugin fetches `https://spwn.jp/events/<event-id>` directly and skips the search step.

## Metadata populated

| Jellyfin field | Source on the event page |
| --- | --- |
| Title | Event title |
| Tagline | Performer list as displayed on the page (verbatim) |
| Overview | Event description text |
| Premiere date | Event date (parsed from `YYYY/MM/DD`) |
| People (actors) | Each performer as an individual `Actor` entry |
| Backdrop | `https://public-web.spwn.jp/events/<uuid>` — the `_1280x720` suffix that appears in page markup is stripped to fetch the original-resolution image |

## Building from source

See `CLAUDE.md` for the build and copy-to-Jellyfin workflow.

## License

GPLv3 — see `LICENSE`.
