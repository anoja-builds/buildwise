<#
.SYNOPSIS
    Checks whether each BuildWise Component 2 service is running, and says why when it is not.

.DESCRIPTION
    Covers the four moving parts of the local development stack:
      * PostgreSQL      127.0.0.1:5432  - port check, plus a real query against the buildwise database when psql is found
      * Agent service   127.0.0.1:8001  - GET /health  (expects status=healthy, service=quotation_agent)
      * BuildWise API   127.0.0.1:5078  - GET /swagger/v1/swagger.json, then POST /api/auth/login + GET /api/auth/me
                                          (proves the API, the database connection, the seed data and JWT validation all work)
      * React web       127.0.0.1:5173  - GET /  (the Vite dev server)

    Exit code is 0 when every service is up and 1 otherwise, so this can gate a script or a CI step.
    Credentials for the deep API check mirror the seeded demo accounts (DbSeeder.DemoPassword).

.PARAMETER WaitSeconds
    Poll every 2 seconds for up to this many seconds instead of reporting once. Example: -WaitSeconds 60.

.PARAMETER Json
    Emit machine-readable JSON instead of the formatted table.

.EXAMPLE
    .\check-services.ps1
.EXAMPLE
    .\check-services.ps1 -WaitSeconds 90
.EXAMPLE
    .\check-services.ps1 -Json | ConvertFrom-Json
#>
[CmdletBinding()]
param(
    [int]$WaitSeconds = 0,
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

# Invoke-WebRequest reports byte-level progress; it is noise when output is redirected or logged.
$ProgressPreference = 'SilentlyContinue'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $repoRoot '.env'
if (Test-Path $envFile) {
    foreach ($line in Get-Content $envFile) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#') -or $trimmed -notmatch '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { continue }
        $name = $matches[1]; $value = $matches[2].Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) { $value = $value.Substring(1, $value.Length - 2) }
        if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) { [Environment]::SetEnvironmentVariable($name, $value, 'Process') }
    }
}
$apiBase   = 'http://127.0.0.1:5078'
$agentBases = @{
    8001 = 'http://127.0.0.1:8001'
    8002 = 'http://127.0.0.1:8002'
    8003 = 'http://127.0.0.1:8003'
    8004 = 'http://127.0.0.1:8004'
}
$webBase   = 'http://127.0.0.1:5173'

# Mirrors BuildWise.Api/Data/DbSeeder.cs (DemoPassword) - seeded demo accounts only.
$demoManagerEmail = 'procurement.manager@buildwise.demo'
$demoPassword     = 'Passw0rd!'

function Test-PortOpen {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try   { $client.Connect('127.0.0.1', $Port); return $true }
    catch { return $false }
    finally { $client.Dispose() }
}

function Get-PortOwner {
    param([int]$Port)
    try {
        $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop | Select-Object -First 1
        if (-not $conn) { return $null }
        $proc = Get-Process -Id $conn.OwningProcess -ErrorAction SilentlyContinue
        if ($proc) { return ('{0} (pid {1})' -f $proc.ProcessName, $proc.Id) }
        return ('pid {0}' -f $conn.OwningProcess)
    } catch { return $null }
}

function Get-PsqlPath {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $file = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($file) { return $file.FullName }
    return $null
}

function Get-ConnectionStringParts {
    $connectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Process')
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        $postgresPassword = [Environment]::GetEnvironmentVariable('POSTGRES_PASSWORD', 'Process')
        if ($postgresPassword) {
            $hostName = if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { '127.0.0.1' }
            $port = if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { '5432' }
            $database = if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { 'buildwise' }
            $userName = if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { 'postgres' }
            $connectionString = "Host=$hostName;Port=$port;Database=$database;Username=$userName;Password=$postgresPassword"
        }
    }
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        try {
            $settings = Get-Content (Join-Path $repoRoot 'backend\BuildWise.Api\appsettings.json') -Raw | ConvertFrom-Json
            $connectionString = $settings.ConnectionStrings.DefaultConnection
        } catch { }
    }

    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        try {
            $secretLines = & dotnet user-secrets list --project (Join-Path $repoRoot 'backend\BuildWise.Api\BuildWise.Api.csproj') 2>$null
            $secretLine = $secretLines | Where-Object { $_ -match '^ConnectionStrings:DefaultConnection\s*=' } | Select-Object -First 1
            if ($secretLine) { $connectionString = ($secretLine -split '=', 2)[1].Trim() }
        } catch { }
    }

    if ([string]::IsNullOrWhiteSpace($connectionString)) { return $null }
    $parts = @{}
    foreach ($pair in $connectionString -split ';') {
        if ($pair -match '^\s*([^=]+)=(.*)$') { $parts[$matches[1].Trim()] = $matches[2].Trim() }
    }
    return $parts
}

function New-Result {
    param([string]$Name, [int]$Port, [bool]$Up, [string]$Detail)
    return [pscustomobject]@{ Service = $Name; Port = $Port; Status = $(if ($Up) { 'UP' } else { 'DOWN' }); Up = $Up; Detail = $Detail }
}

function Test-Postgres {
    if (-not (Test-PortOpen -Port 5432)) {
        return New-Result -Name 'PostgreSQL' -Port 5432 -Up $false `
            -Detail 'nothing listening on 127.0.0.1:5432 - start the PostgreSQL service'
    }

    $detail = 'tcp 5432 open'
    $owner = Get-PortOwner -Port 5432
    if ($owner) { $detail = "$detail ($owner)" }

    $psql = Get-PsqlPath
    if ($psql) {
        $parts = Get-ConnectionStringParts
        if ($parts -and $parts['Database']) {
            try {
                if ($parts['Password']) { $env:PGPASSWORD = $parts['Password'] }
                $hostName = if ($parts['Host']) { $parts['Host'] } else { 'localhost' }
                $userName = if ($parts['Username']) { $parts['Username'] } else { 'postgres' }
                $count = & $psql -h $hostName -U $userName -d $parts['Database'] -tAc 'select count(*) from users' 2>&1
                if ($LASTEXITCODE -eq 0) {
                    $detail = "$detail; $($parts['Database']) reachable (users=$(("$count").Trim()))"
                } else {
                    $detail = "$detail; query failed: $(("$count").Trim())"
                }
            } catch {
                $detail = "$detail; query failed: $($_.Exception.Message)"
            }
        } else {
            $detail = "$detail; connection string not readable, skipped the database query"
        }
    } else {
        $detail = "$detail; psql not found, skipped the database query"
    }

    return New-Result -Name 'PostgreSQL' -Port 5432 -Up $true -Detail $detail
}

function Test-AgentServices {
    $definitions = @(
        @{ Port = 8001; Name = 'Quotation agent'; Service = 'quotation_agent' },
        @{ Port = 8002; Name = 'Request agent'; Service = 'request_agent' },
        @{ Port = 8003; Name = 'Delivery agent'; Service = 'delivery_agent' },
        @{ Port = 8004; Name = 'Quality agent'; Service = 'quality_risk_agent' }
    )
    return @($definitions | ForEach-Object {
        $definition = $_
        if (-not (Test-PortOpen -Port $definition.Port)) {
            return New-Result -Name $definition.Name -Port $definition.Port -Up $false `
                -Detail "nothing listening on 127.0.0.1:$($definition.Port)"
        }
        try {
            $response = Invoke-WebRequest -Uri "$($agentBases[$definition.Port])/health" -UseBasicParsing -TimeoutSec 5
            $health = $response.Content | ConvertFrom-Json
            $up = ($health.status -eq 'healthy' -and $health.service -eq $definition.Service)
            return New-Result -Name $definition.Name -Port $definition.Port -Up $up `
                -Detail "HTTP $($response.StatusCode); status=$($health.status); service=$($health.service)"
        } catch {
            return New-Result -Name $definition.Name -Port $definition.Port -Up $false `
                -Detail "port is open but /health failed: $($_.Exception.Message)"
        }
    })
}

function Test-Api {
    if (-not (Test-PortOpen -Port 5078)) {
        return New-Result -Name 'BuildWise API' -Port 5078 -Up $false `
            -Detail 'nothing listening on 127.0.0.1:5078 - run the API (scripts\start-dev.ps1, or dotnet run in backend\BuildWise.Api)'
    }

    $swaggerOk = $false
    try {
        $swagger = Invoke-WebRequest -Uri "$apiBase/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 5
        $swaggerOk = ($swagger.StatusCode -eq 200)
    } catch {
        return New-Result -Name 'BuildWise API' -Port 5078 -Up $false -Detail "port is open but swagger failed: $($_.Exception.Message)"
    }

    $authDetail = 'login not attempted'
    $authOk = $false
    try {
        $loginBody = @{ email = $demoManagerEmail; password = $demoPassword } | ConvertTo-Json -Compress
        $login = Invoke-WebRequest -Uri "$apiBase/api/auth/login" -Method Post -ContentType 'application/json' `
            -Body $loginBody -UseBasicParsing -TimeoutSec 10
        $token = ($login.Content | ConvertFrom-Json).token
        if ($token) {
            $me = Invoke-WebRequest -Uri "$apiBase/api/auth/me" -UseBasicParsing -TimeoutSec 5 `
                -Headers @{ Authorization = "Bearer $token" }
            $identity = $me.Content | ConvertFrom-Json
            $authOk = $true
            $authDetail = "login + /api/auth/me OK as $($identity.email) [$($identity.roles -join ', ')]"
        } else {
            $authDetail = 'login returned 200 but no token'
        }
    } catch {
        $authDetail = "login/me failed: $($_.Exception.Message)"
    }

    $detail = "swagger $($swagger.StatusCode); $authDetail"
    if (-not $authOk) { $detail = "$detail (is PostgreSQL up and seeded?)" }
    return New-Result -Name 'BuildWise API' -Port 5078 -Up $swaggerOk -Detail $detail
}

function Test-Web {
    if (-not (Test-PortOpen -Port 5173)) {
        return New-Result -Name 'React web' -Port 5173 -Up $false `
            -Detail 'nothing listening on 127.0.0.1:5173 - run npm run dev in web\buildwise-web'
    }

    try {
        $response = Invoke-WebRequest -Uri "$webBase/" -UseBasicParsing -TimeoutSec 5
        $isVite = ($response.Content -match '/@vite/client' -or $response.Content -match '<div id="root">')
        $up = ($response.StatusCode -eq 200)
        $detail = "HTTP $($response.StatusCode)" + $(if ($isVite) { '; Vite dev server response' } else { '; unexpected content (not the Vite dev server?)' })
        return New-Result -Name 'React web' -Port 5173 -Up $up -Detail $detail
    } catch {
        return New-Result -Name 'React web' -Port 5173 -Up $false -Detail "port is open but GET / failed: $($_.Exception.Message)"
    }
}

function Invoke-AllChecks {
    return @(
        (Test-Postgres)
    ) + @(Test-AgentServices) + @(
        (Test-Api),
        (Test-Web)
    )
}

# ---------------------------------------------------------------- run the checks
$results = Invoke-AllChecks

if ($WaitSeconds -gt 0) {
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((($results | Where-Object { -not $_.Up }).Count -gt 0) -and ((Get-Date) -lt $deadline)) {
        if (-not $Json) { Write-Host '.' -NoNewline }
        Start-Sleep -Seconds 2
        $results = Invoke-AllChecks
    }
    if (-not $Json) { Write-Host '' }
}

$down = @($results | Where-Object { -not $_.Up })

if ($Json) {
    $results | ConvertTo-Json -Depth 4
} else {
    Write-Host ''
    Write-Host 'BuildWise complete stack - service status' -ForegroundColor Cyan
    Write-Host ('=' * 74)
    $results | Format-Table -AutoSize @{ Label = 'Service'; Expression = { $_.Service } },
                                      @{ Label = 'Port'; Expression = { $_.Port } },
                                      @{ Label = 'Status'; Expression = { $_.Status } },
                                      @{ Label = 'Detail'; Expression = { $_.Detail } } | Out-String -Width 400 | Write-Host
    if ($down.Count -eq 0) {
        Write-Host "All $($results.Count) services are running." -ForegroundColor Green
        Write-Host ''
    } else {
        Write-Host "$($down.Count) of $($results.Count) services are not running:" -ForegroundColor Yellow
        foreach ($d in $down) { Write-Host ("  - {0}: {1}" -f $d.Service, $d.Detail) -ForegroundColor Yellow }
        Write-Host ''
        Write-Host 'Start everything with:  scripts\start-dev.ps1' -ForegroundColor Gray
        Write-Host ''
    }
}

if ($down.Count -eq 0) { exit 0 } else { exit 1 }
