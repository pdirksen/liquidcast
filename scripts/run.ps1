# Local dev run: starts the .NET backend (:5000) and the Vite dev server (:5173)
# together. The Vite server proxies /api and /hubs to the backend, so open
# http://localhost:5173. Ctrl-C stops both.
#
# Requires: .NET 10 SDK, node. For a working stream you also need Liquidsoap 2.4.5
# on PATH (or set its path on the Settings page) and a reachable Icecast. Without
# them the GUI still runs; the stream just stays in fallback/down.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path src/web/node_modules)) {
  Write-Host "==> Installing frontend deps" -ForegroundColor Cyan
  npm --prefix src/web install
}

$procs = @()
try {
  Write-Host "==> Backend  -> http://localhost:5000" -ForegroundColor Cyan
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  # Not -NoNewWindow: that Start-Process mode leaves child console handles in a
  # state that can hang Liquidsoap (a grandchild, spawned with redirected
  # stdio) during its own runtime startup, before it even reaches user script.
  $procs += Start-Process dotnet -ArgumentList 'run','--no-launch-profile' `
    -WorkingDirectory "$root\src\Liquidcast.Api" -PassThru

  Write-Host "==> Frontend -> http://localhost:5173  (open this one)" -ForegroundColor Green
  # npm is npm.cmd on Windows; CreateProcess can't launch it directly, so go via cmd.
  $procs += Start-Process cmd.exe -ArgumentList '/c','npm','run','dev' `
    -WorkingDirectory "$root\src\web" -PassThru -NoNewWindow

  Wait-Process -Id ($procs.Id)
}
finally {
  foreach ($p in $procs) { if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
}
