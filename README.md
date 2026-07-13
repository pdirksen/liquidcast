<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="branding/logos/liquidcast-logo-horizontal-dark.svg">
    <source media="(prefers-color-scheme: light)" srcset="branding/logos/liquidcast-logo-horizontal-light.svg">
    <img src="branding/logos/liquidcast-logo-horizontal-light.svg" alt="Liquidcast" width="380">
  </picture>
</p>

A self-hostable webradio platform. Arrange MP3 tracks on time-based playlists,
schedule them days ahead, and stream the result to Icecast with crossfades and a
guaranteed fallback — **the listener connection never drops**, even between
tracks, between scheduled slots, or when a playlist runs dry.

- **Backend:** .NET 10 / ASP.NET Core, EF Core + SQLite, SignalR
- **Frontend:** Vue 3 + PrimeVue + Pinia (drag-and-drop timeline editor), i18n (en/de/es)
- **Audio engine:** a single, persistent Liquidsoap 2.4.5 process, fed just-in-time
- **Runs:** Docker (Linux amd64) or standalone on Linux / Windows 11

## How stream continuity works

Liquidcast starts **one** Liquidsoap process and keeps it alive for its whole
lifetime. The Icecast output is `fallible=false` and sits over a fallback chain:

```
output.icecast  ←  fallback(track_sensitive=false, [ crossfade(main_queue), fallback_playlist, silence ])
```

The backend never restarts Liquidsoap to change content. Instead a 1-second
scheduler loop decides what should be on air and pushes track URIs into the
`main` request queue over Liquidsoap's control socket (unix socket on Linux, TCP
telnet on Windows). If the queue empties, the stream structurally falls through
to the fallback playlist or silence — no reconnect, no gap on the listener side.

## Quick start (Docker)

```bash
docker compose up --build
```

- Web GUI: <http://localhost:5000> (default login `admin` / `admin`)
- Stream: <http://localhost:8000/stream>

Override secrets via env or a `.env` file (see `docker-compose.yaml`):
`ADMIN_PASSWORD`, `JWT_SECRET`, `ICECAST_SOURCE_PASSWORD`, `ICECAST_ADMIN_PASSWORD`.

> In Docker, Icecast connection settings come from the environment (so the app
> can reach the `icecast` service by name) and are re-applied on every start.
> For standalone use, configure everything from the **Settings** page instead.

## Standalone

Requirements: **.NET 10 runtime** (or use a self-contained publish) and
**Liquidsoap 2.4.5** on `PATH` (or set its path in Settings / `LIQUIDSOAP_PATH`).
Point the **Settings** page at any reachable Icecast server.

```bash
# dev: backend + Vite dev server (proxies /api and /hubs to :5000)
cd src/Liquidcast.Api && dotnet run          # http://localhost:5000
cd src/web && npm install && npm run dev      # http://localhost:5173

# production single binary (serves the SPA from wwwroot)
cd src/web && npm run build                    # emits into ../Liquidcast.Api/wwwroot
cd ../Liquidcast.Api && dotnet publish -c Release -r linux-x64 --self-contained
```

The data directory (default `data/`, or `DataPath`) holds: `tracks/` (uploads,
organized into subfolders), `fallback/` (loose MP3s for the fallback playlist),
`backups/` (scheduled/manual DB backup zips), `liquidcast.db`, the generated
`liquidcast.liq`, the control socket and the Liquidsoap log.

## Using it

1. **Tracks** — upload MP3s into folders (duration/artist/title are extracted
   automatically); drag tracks between folders to reorganize, or **Rescan** to
   pick up files dropped directly into `tracks/` on disk.
2. **Playlists** — create one, then drag tracks from the library onto the
   timeline; reorder by dragging; set per-track crossfade overrides; **Save**.
3. **Schedule** — assign playlists to wall-clock slots (one-off, daily, weekly),
   with hard-cut or crossfade boundaries.
4. **Monitor** — live now-playing, queue, listener count, connection state, and
   Skip / Restart / scheduler toggle controls (live over SignalR).
5. **Statistics** — listener trends (peak/avg by hour-of-day and weekday) and
   play history (top tracks/artists, plays per day, total airtime), computed
   in the browser's timezone.
6. **Settings** — Icecast target, crossfade defaults, fallback (silence or
   playlist), bitrate, Liquidsoap process options, and scheduled DB backups
   (target folder, daily time, retention). Saving connection/process settings
   regenerates the script and restarts Liquidsoap (a brief blip); backups can
   also be triggered or restored manually.

## Project layout

```
src/Liquidcast.Api/   .NET host — API, EF/SQLite, SignalR, Liquidsoap supervisor + scheduler
src/web/              Vue 3 + PrimeVue SPA (built into Api/wwwroot)
Dockerfile            multi-stage: Vue build → .NET self-contained publish → savonet/liquidsoap runtime
docker-compose.yaml   liquidcast + icecast
icecast.xml           reference Icecast config (optional)
```
