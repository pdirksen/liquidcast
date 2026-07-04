# Kills a leftover Vite dev server from a dev run that didn't shut down cleanly.
# Matches by listening port.
param(
  [int]$Port = 5173
)
$ErrorActionPreference = "Stop"

$killed = 0

$conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
foreach ($c in $conns) {
  $p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
  if ($p) {
    Write-Host "==> Killing frontend on :$Port -> $($p.ProcessName) (pid $($p.Id))" -ForegroundColor Yellow
    Stop-Process -Id $p.Id -Force
    $killed++
  }
}

if ($killed -eq 0) { Write-Host "Nothing running." -ForegroundColor Green }
else { Write-Host "Killed $killed process(es)." -ForegroundColor Green }
