#!/usr/bin/env bash
# Kills leftover Liquidcast backend + Liquidsoap processes from dev runs that
# didn't shut down cleanly (crashed dotnet host, orphaned Liquidsoap child, etc).
# Matches the backend by listening port, Liquidsoap by process name.
set -uo pipefail

PORT="${1:-5000}"
killed=0

pid=$(lsof -ti tcp:"$PORT" -sTCP:LISTEN 2>/dev/null || true)
if [ -n "$pid" ]; then
  echo "==> Killing backend on :$PORT -> pid $pid"
  kill -9 $pid
  killed=1
fi

pids=$(pgrep -x liquidsoap 2>/dev/null || true)
if [ -n "$pids" ]; then
  echo "==> Killing orphaned Liquidsoap -> pid(s) $pids"
  kill -9 $pids
  killed=1
fi

if [ "$killed" -eq 0 ]; then
  echo "Nothing running."
else
  echo "Done."
fi
