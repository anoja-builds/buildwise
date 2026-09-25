<#
.SYNOPSIS
    Starts the complete BuildWise development stack (API, all four agents, React web).

.DESCRIPTION
    Starts all local runtimes, then verifies them:
      1. BuildWise API   http://localhost:5078
      2. Quotation agent http://127.0.0.1:8001
      3. Request agent   http://127.0.0.1:8002
      4. Delivery agent  http://127.0.0.1:8003
      5. Quality agent   http://127.0.0.1:8004
      6. React web       http://127.0.0.1:5173
    The API is run from its compiled DLL and the web server from node_modules\vite\bin\vite.js, so the PID
    recorded here is the actual server rather than a `dotnet run` / `npm` wrapper - that keeps stop-dev.ps1 reliable.

    Logs are written to <repo>\logs\ (git-ignored) and PIDs to <repo>\logs\dev-pids.json.
    Anything already listening on a port is left untouched, so running this twice is harmless.

    PostgreSQL is not started by this script: it is a Windows service (postgresql-x64-18) and must already be running.

.PARAMETER NoBuild
    Skip `dotnet build` (use when the API is already built and no C# source changed).

.PARAMETER TimeoutSeconds
    How long to wait for the services to answer before reporting status. Default: 60.

.EXAMPLE
    .\start-dev.ps1
.EXAMPLE
    .\start-dev.ps1 -NoBuild -TimeoutSeconds 30
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [switch]$Wait,
    [int]$TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot
$logDir     = Join-Path $repoRoot 'logs'
$apiDir     = Join-Path $repoRoot 'backend\BuildWise.Api'
$apiProject = Join-Path $apiDir 'BuildWise.Api.csproj'
$apiDll     = Join-Path $apiDir 'bin\Debug\net8.0\BuildWise.Api.dll'
$agentDir   = Join-Path $repoRoot 'backend\agent_service'
$agentPy    = Join-Path $agentDir '.venv\Scripts\python.exe'
$webDir     = Join-Path $repoRoot 'web\buildwise-web'
$viteBin    = Join-Path $webDir 'node_modules\vite\bin\vite.js'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Load repository-root .env without overriding variables already supplied by the shell.
$envFile = Join-Path $repoRoot '.env'
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { continue }
        $name = $matches[1]
        $value = $matches[2].Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) {
            [Environment]::SetEnvironmentVariable($name, $value, 'Process')
        }
    }
    Write-Host 'Loaded local configuration from .env (secrets are not printed).' -ForegroundColor DarkGray
}

# Build the ASP.NET Core PostgreSQL connection string from standard variables when
# the more explicit ConnectionStrings__DefaultConnection value is not supplied.
if (-not [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Process') -and
    [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD', 'Process')) {
    $pgHost = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { '127.0.0.1' }
    $pgPort = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { '5432' }
    $pgDatabase = if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { 'buildwise' }
    $pgUser = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'postgres' }
    $env:ConnectionStrings__DefaultConnection = "Host=$pgHost;Port=$pgPort;Database=$pgDatabase;Username=$pgUser;Password=$($env:POSTGRES_PASSWORD)"
}

function Test-PortBusy {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try   { $client.Connect('127.0.0.1', $Port); return $true }
    catch { return $false }
    finally { $client.Dispose() }
}

$pids = @{}

# ------------------------------------------------------------------ 1. BuildWise API
if (Test-PortBusy -Port 5078) {
    Write-Host 'Port 5078 already in use - leaving the running API alone.' -ForegroundColor Yellow
} else {
    if (-not $NoBuild) {
        Write-Host 'Building BuildWise.Api...' -ForegroundColor Cyan
        & dotnet build $apiProject --nologo -v minimal | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed - fix the build errors before starting the stack.' }
    }
    if (-not (Test-Path $apiDll)) { throw "API assembly not found at $apiDll - run without -NoBuild." }

    # launchSettings.json would normally supply these; we are starting the DLL directly.
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    # Bind to all interfaces so a physical phone on the same LAN can reach the API.
    # 0.0.0.0 is only a development binding; use HTTPS and a private interface for deployment.
    $env:ASPNETCORE_URLS        = 'http://0.0.0.0:5078'

    # Note: -ArgumentList does not add quotes, and this repo path contains spaces,
    # so the assembly path must be quoted explicitly or dotnet sees only "C:\Users\L".
    $api = Start-Process -FilePath 'dotnet' -ArgumentList "`"$apiDll`"" -WorkingDirectory $apiDir `
        -RedirectStandardOutput (Join-Path $logDir 'api.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'api.err.log') `
        -WindowStyle Hidden -PassThru
    $pids['api'] = $api.Id
    Write-Host ("BuildWise API started (pid {0}) -> {1}" -f $api.Id, (Join-Path $logDir 'api.out.log')) -ForegroundColor Green
}

# ------------------------------------------------------------------ 2. Four AI agents
$agentServices = @(
    @{ Name = 'quotation_agent'; Port = 8001; Log = 'quotation-agent' },
    @{ Name = 'request_agent'; Port = 8002; Log = 'request-agent' },
    @{ Name = 'delivery_agent'; Port = 8003; Log = 'delivery-agent' },
    @{ Name = 'quality_agent'; Port = 8004; Log = 'quality-agent' }
)
foreach ($service in $agentServices) {
    if (Test-PortBusy -Port $service.Port) {
        Write-Host "Port $($service.Port) already in use - leaving $($service.Name) alone." -ForegroundColor Yellow
        continue
    }
    if (-not (Test-Path $agentPy)) { throw "Python virtual environment not found at $agentPy - run: python -m venv .venv ; pip install -r requirements.txt" }

    $agent = Start-Process -FilePath $agentPy `
        -ArgumentList @('-m', 'uvicorn', "$($service.Name):app", '--host', '127.0.0.1', '--port', "$($service.Port)") `
        -WorkingDirectory $agentDir `
        -RedirectStandardOutput (Join-Path $logDir "$($service.Log).out.log") `
        -RedirectStandardError  (Join-Path $logDir "$($service.Log).err.log") `
        -WindowStyle Hidden -PassThru
    $pids[$service.Name] = $agent.Id
    Write-Host ("$($service.Name) started on $($service.Port) (pid {0})" -f $agent.Id) -ForegroundColor Green
}

# ------------------------------------------------------------------ 3. React web
if (Test-PortBusy -Port 5173) {
    Write-Host 'Port 5173 already in use - leaving the running web server alone.' -ForegroundColor Yellow
} else {
    if (-not (Test-Path $viteBin)) { throw "Vite not installed at $viteBin - run: npm install (in web\buildwise-web)" }

    # Same quoting trap as the API above: the Vite entry point path contains spaces.
    $web = Start-Process -FilePath 'node' `
        -ArgumentList "`"$viteBin`" --host 127.0.0.1 --port 5173 --strictPort" -WorkingDirectory $webDir `
        -RedirectStandardOutput (Join-Path $logDir 'web.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'web.err.log') `
        -WindowStyle Hidden -PassThru
    $pids['web'] = $web.Id
    Write-Host ("React web started (pid {0}) -> {1}" -f $web.Id, (Join-Path $logDir 'web.out.log')) -ForegroundColor Green
}

$pids | ConvertTo-Json | Set-Content -Path (Join-Path $logDir 'dev-pids.json') -Encoding UTF8

Write-Host ''
Write-Host "Waiting up to $TimeoutSeconds s for the stack to answer..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'check-services.ps1') -WaitSeconds $TimeoutSeconds

if ($Wait) {
    Write-Host 'Stack is running in persistent background mode (Ctrl+C to stop)...' -ForegroundColor Green
    while ($true) { Start-Sleep -Seconds 10 }
}
