# Jellyfin.Plugin.Spwnjp

A Jellyfin metadata provider for [spwn.jp](https://spwn.jp) events — primarily concert movies and similar live-event releases sold through spwn.jp. The plugin looks up the corresponding event page and fills in titles, performers, descriptions, dates, and backdrop artwork on matching library items.

## How it works

spwn.jp pages are rendered client-side (Firebase-backed), so a plain HTTP fetch returns markup with no event data in it. The plugin solves this by routing every page request through a user-supplied [headless-shell](https://github.com/chromedp/docker-headless-shell) instance, then parsing the rendered HTML it gets back. You run headless-shell yourself (typically as a Docker container on the same host as Jellyfin) and point the plugin at it.

## Requirements

- Jellyfin server 10.9.x.
- A reachable headless-shell instance you control. The plugin validates the URL by issuing `GET /json/version`; if that responds with the standard Chrome DevTools version payload, the URL is considered valid.

## Configuration

In **Dashboard → Plugins → Spwnjp**, set:

- **Headless shell URL** — base URL of your headless-shell instance (for example `http://localhost:9222`). Saved settings are validated against `/json/version` before the rest of the plugin will use them.

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
