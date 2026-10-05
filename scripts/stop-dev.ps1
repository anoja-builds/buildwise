<#
.SYNOPSIS
    Stops the BuildWise Component 2 development stack started by start-dev.ps1.

.DESCRIPTION
    Stops, in order:
      1. the PIDs recorded in <repo>\logs\dev-pids.json (written by start-dev.ps1), then
      2. whatever is still listening on the six development ports (API, four agents, web).

    Only processes on those ports or in that PID file are touched, so unrelated dotnet/node/python
    processes on your machine are not affected. PostgreSQL is left running (it is a Windows service).

.PARAMETER Quiet
    Suppress the per-service output; the exit code still reports whether every port ended up free.

.EXAMPLE
    .\stop-dev.ps1
#>
[CmdletBinding()]
param(
    [switch]$Quiet
)

$ErrorActionPreference = 'Continue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pidFile  = Join-Path $repoRoot 'logs\dev-pids.json'

# Keys are strings on purpose: an [ordered] dictionary indexes by position when given an
# integer, so integer keys would resolve to $null instead of the service name.
$ports = [ordered]@{
    '5078' = 'BuildWise API'
    '8001' = 'Quotation agent'
    '8002' = 'Request agent'
    '8003' = 'Delivery agent'
    '8004' = 'Quality agent'
    '5173' = 'React web'
}

function Write-Status {
    param([string]$Message, [string]$Colour = 'Gray')
    if (-not $Quiet) { Write-Host $Message -ForegroundColor $Colour }
}

# 1. Stop the PIDs recorded at start-up (also catches processes that never bound their port).
if (Test-Path $pidFile) {
    try {
        $recorded = Get-Content $pidFile -Raw | ConvertFrom-Json
        foreach ($name in $recorded.PSObject.Properties.Name) {
            $processId = $recorded.$name
            $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
            if ($proc) {
                Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
                Write-Status ("Stopped $name (pid $processId, $($proc.ProcessName))") 'Yellow'
            }
        }
    } catch {
        Write-Status "Could not read $pidFile : $($_.Exception.Message)" 'DarkYellow'
    }
}

# 2. Stop anything still holding one of the development ports.
foreach ($port in $ports.Keys) {
    $label = $ports[$port]
    $connections = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    if (-not $connections) {
        Write-Status ("{0} (port {1}) was not running." -f $label, $port)
        continue
    }
    foreach ($connection in ($connections | Select-Object -ExpandProperty OwningProcess -Unique)) {
        $proc = Get-Process -Id $connection -ErrorAction SilentlyContinue
        $procName = if ($proc) { $proc.ProcessName } else { 'unknown' }
        Stop-Process -Id $connection -Force -ErrorAction SilentlyContinue
        Write-Status ("Stopped {0} (port {1}, pid {2}, {3})" -f $label, $port, $connection, $procName) 'Yellow'
    }
}

Start-Sleep -Seconds 2

# 3. Report what is left.
$stillUp = @()
foreach ($port in $ports.Keys) {
    if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
        $stillUp += "{0} (port {1})" -f $ports[$port], $port
    }
}

if ($stillUp.Count -eq 0) {
    Write-Status 'All six development ports (5078, 8001-8004, 5173) are free. PostgreSQL is still running.' 'Green'
    exit 0
}

Write-Status ("Still listening: {0}" -f ($stillUp -join ', ')) 'Red'
exit 1
