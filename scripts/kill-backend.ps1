# Kills leftover Liquidcast backend + Liquidsoap processes from dev runs that
# didn't shut down cleanly (crashed dotnet host, orphaned Liquidsoap child, etc).
# Matches the backend by listening port, Liquidsoap by process name.
param(
  [int]$Port = 5000
)
$ErrorActionPreference = "Stop"

$killed = 0

$conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
foreach ($c in $conns) {
  $p = Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue
  if ($p) {
    Write-Host "==> Killing backend on :$Port -> $($p.ProcessName) (pid $($p.Id))" -ForegroundColor Yellow
    Stop-Process -Id $p.Id -Force
    $killed++
  }
}

Get-Process -Name liquidsoap -ErrorAction SilentlyContinue | ForEach-Object {
  Write-Host "==> Killing orphaned Liquidsoap (pid $($_.Id))" -ForegroundColor Yellow
  Stop-Process -Id $_.Id -Force
  $killed++
}

if ($killed -eq 0) { Write-Host "Nothing running." -ForegroundColor Green }
else { Write-Host "Killed $killed process(es)." -ForegroundColor Green }
