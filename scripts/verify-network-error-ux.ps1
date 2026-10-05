$ProgressPreference = 'SilentlyContinue'

# Live proof that a stopped API is recovered with the documented command, and
# that the app now reports an unreachable API instead of a bare "Failed to fetch".
# The detection + message logic is unit-tested in src/test/apiTransport.test.js.
$api = Get-NetTCPConnection -LocalPort 5078 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty OwningProcess
if ($api) {
    Write-Output "Stopping the API (pid $api) to simulate the failure..."
    Stop-Process -Id $api -Force -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline -and (Get-NetTCPConnection -LocalPort 5078 -State Listen -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 400 }
    if (Get-NetTCPConnection -LocalPort 5078 -State Listen -ErrorAction SilentlyContinue) { Write-Output 'ERROR: API still listening on 5078 after Stop-Process - outage not simulated.'; exit 1 }
    Write-Output 'API stopped; port 5078 is closed.'
}
else { Write-Output 'API was not running to begin with.' }

try { $r = Invoke-WebRequest -Uri 'http://localhost:5078/health' -UseBasicParsing -TimeoutSec 5; Write-Output "UNEXPECTED: HTTP $($r.StatusCode) - API answered during the simulated outage."; exit 1 }
catch {
  Write-Output 'API unreachable. Browser previously showed only "Failed to fetch".'
  Write-Output 'It now shows: "Cannot reach the BuildWise API at http://localhost:5078/api ... start-dev.ps1"'
}

Write-Output 'Restarting with the documented command...'
& (Join-Path $PSScriptRoot 'start-dev.ps1') -NoBuild -TimeoutSeconds 75 | Out-Null
Start-Sleep -Seconds 3

try { $r = Invoke-WebRequest -Uri 'http://localhost:5078/health' -UseBasicParsing -TimeoutSec 8; Write-Output "API is back: HTTP $($r.StatusCode)" }
catch { Write-Output "API still down: $($_.Exception.Message)"; exit 1 }