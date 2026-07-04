#!/usr/bin/env bash
# Kills a leftover Vite dev server from a dev run that didn't shut down cleanly.
# Matches by listening port.
set -uo pipefail

PORT="${1:-5173}"
killed=0

pid=$(lsof -ti tcp:"$PORT" -sTCP:LISTEN 2>/dev/null || true)
if [ -n "$pid" ]; then
  echo "==> Killing frontend on :$PORT -> pid $pid"
  kill -9 $pid
  killed=1
fi

if [ "$killed" -eq 0 ]; then
  echo "Nothing running."
else
  echo "Done."
fi
