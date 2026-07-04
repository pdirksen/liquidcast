#!/usr/bin/env bash
# Production build: bundles the Vue SPA into wwwroot and publishes a self-contained
# .NET binary to ./publish. Requires node + .NET 10 SDK. Liquidsoap + Icecast are
# runtime dependencies (not built here).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${1:-linux-x64}"   # e.g. linux-x64, win-x64, osx-arm64

echo "==> Building Vue SPA"
npm --prefix src/web ci
npm --prefix src/web run build   # emits into src/Liquidcast.Api/wwwroot

echo "==> Publishing .NET API ($RID, self-contained)"
dotnet publish src/Liquidcast.Api/Liquidcast.Api.csproj \
  -c Release -r "$RID" --self-contained true \
  -o "$ROOT/publish"

echo "==> Done. Run: ./publish/Liquidcast.Api  (serves GUI on http://localhost:5000)"
