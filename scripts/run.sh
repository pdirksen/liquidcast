#!/usr/bin/env bash
# Local dev run: starts the .NET backend (:5000) and the Vite dev server (:5173)
# together. The Vite server proxies /api and /hubs to the backend, so open
# http://localhost:5173. Ctrl-C stops both.
#
# Requires: .NET 10 SDK, node. For a working stream you also need Liquidsoap 2.4.5
# on PATH and a reachable Icecast (configure it on the Settings page). Without
# them the GUI still runs; the stream just stays in fallback/down.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [ ! -d src/web/node_modules ]; then
  echo "==> Installing frontend deps"
  npm --prefix src/web install
fi

pids=()
cleanup() { for p in "${pids[@]:-}"; do kill "$p" 2>/dev/null || true; done; }
trap cleanup EXIT INT TERM

echo "==> Backend  -> http://localhost:5000"
( cd src/Liquidcast.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run --no-launch-profile ) &
pids+=($!)

echo "==> Frontend -> http://localhost:5173  (open this one)"
( npm --prefix src/web run dev ) &
pids+=($!)

wait
