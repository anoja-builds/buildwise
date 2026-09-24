<#
.SYNOPSIS
    Starts the BuildWise Component 2 development stack (API, agent service, React web) in the background.

.DESCRIPTION
    Starts the three runtimes the setup guide describes, then verifies them:

      1. BuildWise API   http://localhost:5078   (dotnet, Development environment, seeded database)
      2. Agent service   http://127.0.0.1:8001   (uvicorn quotation_agent:app, from backend\agent_service\.venv)
      3. React web       http://127.0.0.1:5173   (Vite dev server)

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
    $env:ASPNETCORE_URLS        = 'http://localhost:5078'

    # Note: -ArgumentList does not add quotes, and this repo path contains spaces,
    # so the assembly path must be quoted explicitly or dotnet sees only "C:\Users\L".
    $api = Start-Process -FilePath 'dotnet' -ArgumentList "`"$apiDll`"" -WorkingDirectory $apiDir `
        -RedirectStandardOutput (Join-Path $logDir 'api.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'api.err.log') `
        -WindowStyle Hidden -PassThru
    $pids['api'] = $api.Id
    Write-Host ("BuildWise API started (pid {0}) -> {1}" -f $api.Id, (Join-Path $logDir 'api.out.log')) -ForegroundColor Green
}

# ------------------------------------------------------------------ 2. Agent service
if (Test-PortBusy -Port 8001) {
    Write-Host 'Port 8001 already in use - leaving the running agent service alone.' -ForegroundColor Yellow
} else {
    if (-not (Test-Path $agentPy)) { throw "Python virtual environment not found at $agentPy - run: python -m venv .venv ; pip install -r requirements.txt" }

    $agent = Start-Process -FilePath $agentPy `
        -ArgumentList @('-m', 'uvicorn', 'quotation_agent:app', '--host', '127.0.0.1', '--port', '8001') `
        -WorkingDirectory $agentDir `
        -RedirectStandardOutput (Join-Path $logDir 'agent.out.log') `
        -RedirectStandardError  (Join-Path $logDir 'agent.err.log') `
        -WindowStyle Hidden -PassThru
    $pids['agent'] = $agent.Id
    Write-Host ("Agent service started (pid {0}) -> {1}" -f $agent.Id, (Join-Path $logDir 'agent.out.log')) -ForegroundColor Green
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
