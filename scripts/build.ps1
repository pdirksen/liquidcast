# Production build: bundles the Vue SPA into wwwroot and publishes a self-contained
# .NET binary to .\publish. Requires node + .NET 10 SDK. Liquidsoap + Icecast are
# runtime dependencies (not built here).
param([string]$Rid = "win-x64")   # e.g. win-x64, linux-x64, osx-arm64

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "==> Building Vue SPA" -ForegroundColor Cyan
npm --prefix src/web ci
npm --prefix src/web run build   # emits into src/Liquidcast.Api/wwwroot

Write-Host "==> Publishing .NET API ($Rid, self-contained)" -ForegroundColor Cyan
dotnet publish src/Liquidcast.Api/Liquidcast.Api.csproj `
  -c Release -r $Rid --self-contained true `
  -o "$root\publish"

Write-Host "==> Done. Run: .\publish\Liquidcast.Api.exe  (serves GUI on http://localhost:5000)" -ForegroundColor Green
