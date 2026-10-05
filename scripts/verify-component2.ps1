<#
.SYNOPSIS
    One-command verification that BuildWise Component 2 (Supplier, Quotation & Procurement
    Management) and its Quotation & Supplier Analysis Agent satisfy the component spec.

.DESCRIPTION
    check-services.ps1 answers "is it up?" and smoke-test.ps1 answers "is it working?".
    This script answers "does the whole component work correctly?" by running every layer, in
    order, and printing one PASS/FAIL/SKIP table at the end:

      1 Build              dotnet build (API + tests project) and the Vite production build
      2 Agent unit tests   pytest  backend\agent_service\test_quotation_agent.py     (9 tests)
      3 API/business tests dotnet test backend\BuildWise.Api.Tests                  (20 tests)
      4 React tests        vitest run web\buildwise-web                             (15 tests)
      5 Service health     scripts\check-services.ps1                               (4 services)
            6 Stack smoke test   scripts\smoke-test.ps1 (auth, suppliers, purchase orders, agent decision) (11 assertions)
      7 Agent output path  starts a workflow over the real API as the Procurement Officer and
                           reads the recommendation, validation result and audit trail back out;
                           the alternative "reason" text also proves the external Python agent
                           answered rather than the in-process fallback
      8 Approval gate      the Officer is refused the Manager-only decision endpoint (403), and
                           purchase-order creation before approval is refused (400) with no PO
                           written and the workflow left AwaitingApproval
      9 Approval -> PO     (-IncludeApprovalPath) Manager approves -> PO created, winner quotation
                           Selected, losers Rejected, workflow Completed. This WRITES demo data,
                           so it is opt-in and skips itself when that request already has a PO
                           (spec §5.10 blocks a duplicate).

    Sections 1-4 need no running services. Sections 5-9 need the stack: scripts\start-dev.ps1.
    Skips are always printed with their reason. Exit code 0 when nothing failed, 1 otherwise.

.PARAMETER SkipBuild
    Skip section 1 (.NET + Vite build).

.PARAMETER SkipTests
    Skip sections 2-4 (pytest, dotnet test, vitest).

.PARAMETER IncludeApprovalPath
    Also run section 9, which approves the workflow as the Procurement Manager and therefore
    creates a real purchase order in the local demo database.

.PARAMETER WaitSeconds
    Passed through to check-services.ps1: poll for up to this many seconds until every service
    answers instead of reporting once.

.PARAMETER ApiBase
    Base URL of the BuildWise API. Default: http://127.0.0.1:5078

.PARAMETER AgentBase
    Base URL of the Python agent service. Default: http://127.0.0.1:8001

.EXAMPLE
    .\verify-component2.ps1
.EXAMPLE
    .\verify-component2.ps1 -SkipBuild -WaitSeconds 60
.EXAMPLE
    .\verify-component2.ps1 -SkipBuild -SkipTests -IncludeApprovalPath
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$SkipTests,
    [switch]$IncludeApprovalPath,
    [int]$WaitSeconds = 0,
    [string]$ApiBase = 'http://127.0.0.1:5078',
    [string]$AgentBase = 'http://127.0.0.1:8001',
    [string]$WebBase = 'http://127.0.0.1:5173'
)

$ErrorActionPreference = 'Stop'

# Invoke-WebRequest reports byte-level progress; it is noise when output is redirected or logged.
$ProgressPreference = 'SilentlyContinue'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$logDir    = Join-Path $repoRoot 'logs'
$testsProj = Join-Path $repoRoot 'backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj'
$testsDll  = Join-Path $repoRoot 'backend\BuildWise.Api.Tests\bin\Debug\net8.0\BuildWise.Api.Tests.dll'
$agentDir  = Join-Path $repoRoot 'backend\agent_service'
$agentPy   = Join-Path $agentDir '.venv\Scripts\python.exe'
$webDir    = Join-Path $repoRoot 'web\buildwise-web'
$vitestBin = Join-Path $webDir 'node_modules\vitest\vitest.mjs'

# Mirrors BuildWise.Api/Data/DbSeeder.cs (DemoPassword) - seeded demo accounts only.
$demoPassword = 'Passw0rd!'
$officerEmail = 'procurement.officer@buildwise.demo'
$managerEmail = 'procurement.manager@buildwise.demo'

New-Item -ItemType Directory -Force -Path $logDir | Out-Null

$script:checks = @()
$script:servicesUp = $false
$script:workflowId = 0
$script:requestId = 0

# --------------------------------------------------------------------------- output helpers
function Write-Section {
    param([string]$Title)
    Write-Host ''
    Write-Host $Title -ForegroundColor Cyan
    Write-Host ('-' * 74) -ForegroundColor DarkGray
}

function Add-Check {
    param(
        [Parameter(Mandatory)][string]$Group,
        [Parameter(Mandatory)][string]$Label,
        [bool]$Pass = $false,
        [string]$Detail,
        [switch]$Skip,
        [string]$SkipReason
    )

    if ($Skip) {
        Write-Host ("  SKIP  {0}" -f $Label) -ForegroundColor Yellow
        if ($SkipReason) { Write-Host ("        {0}" -f $SkipReason) -ForegroundColor Gray }
        $script:checks += [pscustomobject]@{ Group = $Group; Label = $Label; State = 'SKIP'; Detail = $SkipReason }
        return
    }

    if ($Pass) {
        Write-Host ("  PASS  {0}" -f $Label) -ForegroundColor Green
        $script:checks += [pscustomobject]@{ Group = $Group; Label = $Label; State = 'PASS'; Detail = $Detail }
    } else {
        Write-Host ("  FAIL  {0}" -f $Label) -ForegroundColor Red
        $script:checks += [pscustomobject]@{ Group = $Group; Label = $Label; State = 'FAIL'; Detail = $Detail }
    }
    if ($Detail) { Write-Host ("        {0}" -f $Detail) -ForegroundColor Gray }
}

function Show-Log {
    param([string]$Path, [string]$Pattern, [int]$Max = 40)
    if (-not (Test-Path $Path)) { return }
    Get-Content $Path | Where-Object { $_ -match $Pattern } | Select-Object -First $Max |
        ForEach-Object { Write-Host ("    {0}" -f $_.TrimEnd()) -ForegroundColor DarkGray }
}

# --------------------------------------------------------------------------- process/HTTP helpers
function Test-PortOpen {
    param([int]$Port)
    $client = New-Object System.Net.Sockets.TcpClient
    try   { $client.Connect('127.0.0.1', $Port); return $true }
    catch { return $false }
    finally { $client.Dispose() }
}

function Invoke-NativeStep {
    param([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [string]$LogName)
    $logPath = Join-Path $logDir $LogName
    $code = -1
    Push-Location $WorkingDirectory
    try {
        $prev = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $FilePath @Arguments *> $logPath
        $code = $LASTEXITCODE
    } catch {
        # native command wrote to the error stream; capture what we can
        $code = $code  # keep whatever LASTEXITCODE was
    } finally {
        $ErrorActionPreference = $prev
        Pop-Location
    }
    return [pscustomobject]@{ ExitCode = $code; LogPath = $logPath }
}

function Select-LogLine {
    param([string]$Path, [string]$Pattern)
        if (-not (Test-Path $Path)) { return $null }
    $clean = Get-Content $Path -Raw -ErrorAction SilentlyContinue
    $clean = $clean -replace '\x1b\[\d+(?:;\d+)*[a-zA-Z]', ''  # strip ANSI escape codes
    $match = $clean -split "`n" | Select-String -Pattern $Pattern -CaseSensitive -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($match) { return $match.Line.Trim() }
    return $null
}

function Invoke-ApiRequest {
    param(
        [string]$Uri,
        [string]$Method = 'GET',
        [hashtable]$Headers,
        [string]$Body
    )

    $params = @{ Uri = $Uri; Method = $Method; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($Headers) { $params.Headers = $Headers }
    if ($Body) { $params.ContentType = 'application/json'; $params.Body = $Body }

    try {
        $response = Invoke-WebRequest @params
        $data = $null
        if ($response.Content) { $data = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue }
        return [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data }
    } catch {
        $status = -1
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        return [pscustomobject]@{ Status = $status; Data = $null }
    }
}

function Get-Token {
    param([string]$Email)
    $login = Invoke-ApiRequest -Uri "$ApiBase/api/auth/login" -Method Post `
        -Body (@{ email = $Email; password = $demoPassword } | ConvertTo-Json -Compress)
    if ($login.Status -eq 200 -and $login.Data.token) { return $login.Data.token }
    return $null
}

function Get-AuthHeaders {
    param([string]$Email)
    $token = Get-Token -Email $Email
    if (-not $token) { return $null }
    return @{ Authorization = "Bearer $token" }
}

# --------------------------------------------------------------------------- 1. build
function Invoke-BuildSection {
    Write-Section '1. Build'

    if ($SkipBuild) {
        Add-Check -Group 'Build' -Label 'Builds' -Skip -SkipReason 'skipped with -SkipBuild'
        return
    }

    # The running API holds bin\Debug\net8.0\BuildWise.Api.dll, so a build cannot overwrite it.
    if (Test-PortOpen -Port 5078) {
        Add-Check -Group 'Build' -Label '.NET build (API + tests)' -Skip `
            -SkipReason 'the API is running and holds bin\Debug\net8.0\BuildWise.Api.dll - run scripts\stop-dev.ps1 first to build'
    } else {
        $step = Invoke-NativeStep -FilePath 'dotnet' -Arguments @('build', $testsProj, '--nologo', '-v', 'minimal') `
            -WorkingDirectory $repoRoot -LogName 'verify-dotnet-build.log'
        $detail = if ($step.ExitCode -eq 0) { 'BuildWise.Api + BuildWise.Api.Tests compiled' }
                  else { "dotnet build exited $($step.ExitCode) - see logs\verify-dotnet-build.log" }
        Add-Check -Group 'Build' -Label '.NET build (API + tests)' -Pass ($step.ExitCode -eq 0) -Detail $detail
    }

    $step = Invoke-NativeStep -FilePath 'npm' -Arguments @('run', 'build') -WorkingDirectory $webDir -LogName 'verify-web-build.log'
    $detail = if ($step.ExitCode -eq 0) { Select-LogLine -Path $step.LogPath -Pattern 'built in' }
              else { "npm run build exited $($step.ExitCode) - see logs\verify-web-build.log" }
    Add-Check -Group 'Build' -Label 'React production build (vite build)' -Pass ($step.ExitCode -eq 0) -Detail $detail
}

# --------------------------------------------------------------------------- 2. agent unit tests
function Invoke-AgentTestSection {
    Write-Section '2. Agent service unit tests (pytest)'

    if ($SkipTests) {
        Add-Check -Group 'Tests' -Label 'Agent service tests' -Skip -SkipReason 'skipped with -SkipTests'
        return
    }

    $python = if (Test-Path $agentPy) { $agentPy } else { 'python' }
    $step = Invoke-NativeStep -FilePath $python -Arguments @('-m', 'pytest', 'test_quotation_agent.py', '-q') `
        -WorkingDirectory $agentDir -LogName 'verify-agent-tests.log'
    $detail = Select-LogLine -Path $step.LogPath -Pattern 'passed|failed|error'
    if (-not $detail) { $detail = "pytest exited $($step.ExitCode) - see logs\verify-agent-tests.log" }
    Add-Check -Group 'Tests' -Label 'pytest backend\agent_service\test_quotation_agent.py' -Pass ($step.ExitCode -eq 0) -Detail $detail
}

# --------------------------------------------------------------------------- 3. API tests
function Invoke-ApiTestSection {
    Write-Section '3. API + business-rule tests (dotnet test)'

    if ($SkipTests) {
        Add-Check -Group 'Tests' -Label 'BuildWise.Api.Tests' -Skip -SkipReason 'skipped with -SkipTests'
        return
    }

    $apiRunning = Test-PortOpen -Port 5078
    if ($apiRunning -and -not (Test-Path $testsDll)) {
        Add-Check -Group 'Tests' -Label 'dotnet test BuildWise.Api.Tests' -Skip `
            -SkipReason 'the API is running (its DLL is locked) and no test assembly exists yet - run scripts\stop-dev.ps1 first'
        return
    }

    $args = @('test', $testsProj, '--nologo', '-v', 'minimal')
    $note = 'built, then tested'
    if ($apiRunning) {
        $args += '--no-build'
        $note = 'the API is running, so the last built test assembly was used (--no-build)'
    }

    $step = Invoke-NativeStep -FilePath 'dotnet' -Arguments $args -WorkingDirectory $repoRoot -LogName 'verify-dotnet-tests.log'
    $detail = Select-LogLine -Path $step.LogPath -Pattern 'Passed!|Failed!'
    if (-not $detail) { $detail = "dotnet test exited $($step.ExitCode) - see logs\verify-dotnet-tests.log" }
    Add-Check -Group 'Tests' -Label 'dotnet test BuildWise.Api.Tests' -Pass ($step.ExitCode -eq 0) -Detail "$detail ($note)"
}

# --------------------------------------------------------------------------- 4. React tests
function Invoke-WebTestSection {
    Write-Section '4. React component tests (vitest)'

    if ($SkipTests) {
        Add-Check -Group 'Tests' -Label 'vitest run' -Skip -SkipReason 'skipped with -SkipTests'
        return
    }

    if (-not (Test-Path $vitestBin)) {
        Add-Check -Group 'Tests' -Label 'vitest run' -Skip `
            -SkipReason 'node_modules\vitest is missing - run npm install in web\buildwise-web'
        return
    }

    $step = Invoke-NativeStep -FilePath 'node' -Arguments @($vitestBin, 'run') -WorkingDirectory $webDir -LogName 'verify-web-tests.log'
            $detail = Select-LogLine -Path $step.LogPath -Pattern 'Tests\s+\d+\s+(failed|passed|skipped|todo)'
    if (-not $detail) { $detail = "vitest exited $($step.ExitCode) - see logs\verify-web-tests.log" }
    Add-Check -Group 'Tests' -Label 'vitest run web\buildwise-web' -Pass ($step.ExitCode -eq 0) -Detail $detail
}

# --------------------------------------------------------------------------- 5. service health
function Invoke-ServiceSection {
    Write-Section '5. Service health (PostgreSQL, agent service, API, React web)'

    $checker = Join-Path $PSScriptRoot 'check-services.ps1'
    if (-not (Test-Path $checker)) {
        Add-Check -Group 'Services' -Label 'scripts\check-services.ps1 is present' -Pass $false -Detail 'not found in scripts\'
        return
    }

    # check-services.ps1 ends with `exit 0/1`; the call operator returns here and sets $LASTEXITCODE.
    $logPath = Join-Path $logDir 'verify-services.log'
    & $checker -WaitSeconds $WaitSeconds *> $logPath
    $exitCode = $LASTEXITCODE

    Show-Log -Path $logPath -Pattern 'PostgreSQL|Agent service|BuildWise API|React web' -Max 6

    $script:servicesUp = ($exitCode -eq 0)
    $detail = if ($script:servicesUp) { 'all four services answered their health checks' }
              else { 'see logs\verify-services.log for the reason - start them with scripts\start-dev.ps1' }
    Add-Check -Group 'Services' -Label 'All four services are up' -Pass $script:servicesUp -Detail $detail
}

# --------------------------------------------------------------------------- 6. stack smoke test
function Invoke-SmokeTestSection {
    Write-Section '6. Stack smoke test (auth, suppliers, purchase orders, agent decision)'

    if (-not $script:servicesUp) {
        Add-Check -Group 'Smoke' -Label 'scripts\smoke-test.ps1' -Skip `
            -SkipReason 'the stack is not fully up - start it with scripts\start-dev.ps1'
        return
    }

    $scriptPath = Join-Path $PSScriptRoot 'smoke-test.ps1'
    if (-not (Test-Path $scriptPath)) {
        Add-Check -Group 'Smoke' -Label 'scripts\smoke-test.ps1 is present' -Pass $false -Detail 'not found in scripts\'
        return
    }

    $logPath = Join-Path $logDir 'verify-smoke.log'
    & $scriptPath *> $logPath
    $exitCode = $LASTEXITCODE

    Show-Log -Path $logPath -Pattern 'PASS|FAIL|SKIP' -Max 20
    $detail = Select-LogLine -Path $logPath -Pattern 'Smoke test (passed|failed)'
    if (-not $detail) { $detail = 'see logs\verify-smoke.log' }
    Add-Check -Group 'Smoke' -Label 'End-to-end smoke test assertions' -Pass ($exitCode -eq 0) -Detail $detail
}

# --------------------------------------------------------------------------- 7. agent output path
function Invoke-AgentPathSection {
    Write-Section '7. Agent output path (agent service -> API -> workflow response)'

    if (-not $script:servicesUp) {
        Add-Check -Group 'Agent path' -Label 'Agent recommendation is produced and routed' -Skip `
            -SkipReason 'the stack is not fully up - start it with scripts\start-dev.ps1'
        return
    }

    # ---- 7a. the microservice itself, called directly with the spec §11 cement scenario -----
    $payload = @'
{"material_request_id":1,"requested_quantities":{"1":250.0},"quotations":[
 {"quotation_id":3,"supplier_id":1,"supplier_name":"Supplier A Building Materials","supplier_status":"Active","quantity_offered":{"1":250.0},"unit_prices":{"1":2100.0},"total_amount":525000.0,"valid":true},
 {"quotation_id":6,"supplier_id":2,"supplier_name":"Supplier B Traders","supplier_status":"Suspended","quantity_offered":{"1":250.0},"unit_prices":{"1":2040.0},"total_amount":510000.0,"valid":true},
 {"quotation_id":7,"supplier_id":3,"supplier_name":"Supplier C Wholesale","supplier_status":"Active","quantity_offered":{"1":200.0},"unit_prices":{"1":2050.0},"total_amount":410000.0,"valid":true},
 {"quotation_id":8,"supplier_id":4,"supplier_name":"Supplier D Expired","supplier_status":"Active","quantity_offered":{"1":250.0},"unit_prices":{"1":1990.0},"total_amount":497500.0,"valid":false}]}
'@

    $agent = Invoke-ApiRequest -Uri "$AgentBase/analyze" -Method Post -Body $payload
    $agentOk = ($agent.Status -eq 200) -and ($agent.Data.recommended_quotation_id -eq 3)
    Add-Check -Group 'Agent path' -Label 'Agent /analyze recommends the Active, full-coverage, cheapest quotation' `
        -Pass $agentOk -Detail "HTTP $($agent.Status); recommended=#$($agent.Data.recommended_quotation_id) ($($agent.Data.recommended_supplier_name))"
    if (-not $agentOk) { return }

    $rankedIds = @($agent.Data.ranked_alternatives | ForEach-Object { $_.quotation_id })
    $warnings = ($agent.Data.warnings -join ' | ')
    $quality = ($rankedIds -notcontains 6) -and ($rankedIds -notcontains 8) -and
               ($agent.Data.recommended_quotation_id -ne 7) -and
               ($warnings -match 'Suspended') -and ($warnings -match '200/250')
    Add-Check -Group 'Agent path' -Label 'Suspended and expired quotations are excluded, partial coverage is warned' `
        -Pass $quality -Detail "ranked=[$($rankedIds -join ', ')]; $warnings"

    # ---- 7b. through the API, started by the Procurement Officer (§4.2 / §7) ----------------
    $officerHeaders = Get-AuthHeaders -Email $officerEmail
    if (-not $officerHeaders) {
        Add-Check -Group 'Agent path' -Label 'Procurement Officer can sign in' -Pass $false `
            -Detail 'login failed - is PostgreSQL up and the database seeded?'
        return
    }

        $requests = Invoke-ApiRequest -Uri "$ApiBase/api/material-requests?status=Approved" -Headers $officerHeaders
    # The API may return a flat array/object (one approved request) or a paginated {items, total}.
    $approved = if ($requests.Data -is [System.Collections.IEnumerable] -and $requests.Data -isnot [string]) {
        @($requests.Data)
    } elseif ($requests.Data.items) {
        @($requests.Data.items)
    } elseif ($requests.Data.id -and $requests.Data.status -eq 'Approved') {
        @($requests.Data)
    } else {
        @()
    }
    if ($approved.Count -eq 0) {
        Add-Check -Group 'Agent path' -Label 'An Approved material request exists to analyse' -Pass $false `
            -Detail 'no Approved material requests - Component 1 has to approve one first'
        return
    }

    $target = $approved[0]
    $script:requestId = [int]$target.id
    Write-Host ("    analysing request #{0} - {1}" -f $target.id, $target.projectName) -ForegroundColor DarkGray

    $start = Invoke-ApiRequest -Uri "$ApiBase/api/material-requests/$($target.id)/procurement-workflow" -Method Post `
        -Headers $officerHeaders -Body '{}'
    $script:workflowId = [int]$start.Data.workflowId
    Add-Check -Group 'Agent path' -Label 'Officer can start the agent workflow over the API' -Pass ($start.Status -eq 200) `
        -Detail "HTTP $($start.Status); workflow=$($start.Data.workflowId); status=$($start.Data.status)"
    if ($script:workflowId -le 0) { return }

        $wf = Invoke-ApiRequest -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)" -Headers $officerHeaders
    $rec = $wf.Data.recommendation
    $alts = @($rec.rankedAlternatives)
    $reasons = ($alts | ForEach-Object { $_.reason }) -join ' | '

    Add-Check -Group 'Agent path' -Label 'Workflow pauses at AwaitingApproval instead of auto-approving' `
        -Pass (($wf.Data.status -eq 'AwaitingApproval') -and ($wf.Data.approvalStatus -eq 'Pending')) `
        -Detail "status=$($wf.Data.status); approvalStatus=$($wf.Data.approvalStatus); $($wf.Data.finalOutcome)"

    $recOk = ($null -ne $rec.recommendedQuotationId) -and ($null -ne $rec.recommendedSupplierId) -and
             (-not [string]::IsNullOrWhiteSpace($rec.rationale))
    Add-Check -Group 'Agent path' -Label 'Recommendation reaches the client (quotation, supplier, rationale)' `
        -Pass $recOk -Detail "#$($rec.recommendedQuotationId) $($rec.recommendedSupplierName): $($rec.rationale)"

    $altDetail = if ($alts.Count -ge 1) { $reasons } else { 'no alternatives were returned' }
    Add-Check -Group 'Agent path' -Label 'Ranked alternatives come back with their reasons' -Pass ($alts.Count -ge 1) -Detail $altDetail

    # The in-process fallback words a reason as "Full coverage, lowest price", the Python agent as
    # "Full coverage across all items, total ...". Absence of the fallback wording proves which answered.
    Add-Check -Group 'Agent path' -Label 'The external agent produced it (not the in-process fallback)' `
        -Pass ($reasons -notmatch 'Full coverage, lowest price') `
        -Detail "fallback wording absent; reasons: $reasons"

    Add-Check -Group 'Agent path' -Label 'Deterministic validation re-checked the agent recommendation (§5)' `
        -Pass ($wf.Data.validation.isValid -eq $true) `
        -Detail "isValid=$($wf.Data.validation.isValid); errors=[$($wf.Data.validation.errors -join '; ')]"

    $history = Invoke-ApiRequest -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)/history" -Headers $officerHeaders
    $steps = @($history.Data)
    $stepDetail = ($steps | Sort-Object stepOrder | ForEach-Object { "$($_.stepOrder):$($_.agentRole)/$($_.status)" }) -join ' -> '
        $rolesOk = ((@($steps | Where-Object { $_.agentRole -eq 'ProcurementPlanningAgent'            -and $_.status -eq 'Completed' })).Count -eq 1) -and
               ((@($steps | Where-Object { $_.agentRole -eq 'QuotationSupplierAnalysisAgent'      -and $_.status -eq 'Completed' })).Count -eq 1) -and
               ((@($steps | Where-Object { $_.agentRole -eq 'ProcurementValidationAgent'          -and $_.status -eq 'Completed' })).Count -eq 1)
    Add-Check -Group 'Agent path' -Label 'Audit trail stores the planning, analysis and validation steps' -Pass $rolesOk -Detail $stepDetail
}

# --------------------------------------------------------------------------- 8. human-in-the-loop gate
function Invoke-ApprovalGateSection {
    Write-Section '8. Human-in-the-loop gate (officer refused; no purchase order before approval)'

    if (-not $script:servicesUp -or $script:workflowId -le 0) {
        Add-Check -Group 'Gate' -Label 'Officer refused Manager-only decision (403)' -Skip -SkipReason 'no workflow was produced in section 7'
        Add-Check -Group 'Gate' -Label 'Purchase order blocked before approval (no write)' -Skip -SkipReason 'no workflow was produced in section 7'
        return
    }

    $officerHeaders = Get-AuthHeaders -Email $officerEmail
    $managerHeaders = Get-AuthHeaders -Email $managerEmail
    if (-not $officerHeaders -or -not $managerHeaders) {
        Add-Check -Group 'Gate' -Label 'Officer + Manager demo accounts authenticate' -Pass $false -Detail 'demo login failed'
        return
    }
    Add-Check -Group 'Gate' -Label 'Officer + Manager demo accounts authenticate' -Pass $true -Detail "$officerEmail / $managerEmail"

    # The /decision endpoint is [Authorize(Roles = "ProcurementManager,Administrator")], so an
    # authenticated Officer must be refused before any row is written.
    $decision = Invoke-ApiRequest -Method Post `
        -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)/decision" `
        -Headers $officerHeaders `
        -Body (@{ decision = 'Approve'; comment = 'must be refused (officer)'; reviewedByUserId = 1 } | ConvertTo-Json -Compress)
    Add-Check -Group 'Gate' -Label 'Officer is refused the Manager-only decision endpoint (403)' `
        -Pass ($decision.Status -eq 403) -Detail "HTTP $($decision.Status)"

    # Rule §5.8/§5.9/§5.10: a purchase order needs an Approved decision and a clean request.
    $before = Invoke-ApiRequest -Uri "$ApiBase/api/purchase-orders?pageSize=200" -Headers $managerHeaders
    $po = Invoke-ApiRequest -Method Post `
        -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)/purchase-order" `
        -Headers $managerHeaders
    $after = Invoke-ApiRequest -Uri "$ApiBase/api/purchase-orders?pageSize=200" -Headers $managerHeaders
    $beforeCount = if ($before.Data) { $before.Data.total } else { 0 }
    $afterCount  = if ($after.Data)  { $after.Data.total }  else { 0 }
    Add-Check -Group 'Gate' -Label 'Purchase order is refused before approval (400, nothing written)' `
        -Pass (($po.Status -eq 400) -and ($beforeCount -eq $afterCount)) `
        -Detail "HTTP $($po.Status); purchase order count [$beforeCount -> $afterCount]"

    # And the refused attempts must not have advanced the workflow to Completed/Failed.
    $wf = Invoke-ApiRequest -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)" -Headers $managerHeaders
    $wfOk = if ($wf.Data) { ($wf.Data.status -eq 'AwaitingApproval') -and ($wf.Data.approvalStatus -eq 'Pending') } else { $false }
    Add-Check -Group 'Gate' -Label 'Workflow is still AwaitingApproval (no state leaked)' -Pass $wfOk `
        -Detail "status=$($wf.Data.status); approvalStatus=$($wf.Data.approvalStatus)"
}

# --------------------------------------------------------------------------- 9. manager approval -> purchase order
function Invoke-ApprovalPathSection {
    Write-Section '9. Manager approval path (approval -> PO -> quotations rejected)'

    if (-not $script:servicesUp -or $script:workflowId -le 0) {
        Add-Check -Group 'Approval' -Label 'Manager approves and a purchase order is created' -Skip -SkipReason 'no workflow produced'
        Add-Check -Group 'Approval' -Label 'Accepted quotations are rejected when the PO saves' -Skip -SkipReason 'no workflow produced'
        return
    }

    $managerHeaders = Get-AuthHeaders -Email $managerEmail
    if (-not $managerHeaders) {
        Add-Check -Group 'Approval' -Label 'Manager demo account authenticates' -Pass $false -Detail 'login failed'
        return
    }

    # Spec §5.10: a purchase order is created once per request. If one already
    # exists (e.g. from a prior approval), the auto-PO step throws inside
    # RecordDecisionAsync AFTER the workflow status is saved. The HTTP status
    # may be 400 in that case, but the workflow is still Approved.
    $decision = Invoke-ApiRequest -Method Post `
        -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)/decision" `
        -Headers $managerHeaders `
        -Body (@{ decision = 'Approve'; comment = 'verified by verify-component2.ps1'; reviewedByUserId = 2 } | ConvertTo-Json -Compress)

    $wf = Invoke-ApiRequest -Uri "$ApiBase/api/procurement-workflow/$($script:workflowId)" -Headers $managerHeaders
    $wfOk = ($wf.Data.status -eq 'Completed') -and ($wf.Data.approvalStatus -eq 'Approved')
    Add-Check -Group 'Approval' -Label 'Procurement Manager can approve the recommendation' `
        -Pass $wfOk `
        -Detail "decision HTTP $($decision.Status); status=$($wf.Data.status); approvalStatus=$($wf.Data.approvalStatus); $($wf.Data.finalOutcome)"

    # Spec §5.10: a purchase order is created once per request. If one already
    # exists (e.g. from a prior approval), the auto-PO step throws inside
    # RecordDecisionAsync AFTER the workflow status is saved, so the decision may
    # return HTTP 400 while the workflow is still Completed / Approved.
    $poId = if ($wf.Data -and $wf.Data.purchaseOrderId) { $wf.Data.purchaseOrderId } else { $null }
    $po = if ($poId) { Invoke-ApiRequest -Uri "$ApiBase/api/purchase-orders/$poId" -Headers $managerHeaders } else { $null }
    $poOk = if ($po) { ($po.Status -eq 200) -and ($null -ne $po.Data) } else { $false }

    if ($poOk) {
        $poDetail = "PO #$poId returned"
    } elseif ($decision.Status -ne 200 -and $wfOk) {
        # Decision returned 400 (§5.10 duplicate block) but workflow is Approved —
        # find the existing PO for this request and verify it used the winner.
        $existingPOs = Invoke-ApiRequest -Uri "$ApiBase/api/purchase-orders?materialRequestId=$($script:requestId)" -Headers $managerHeaders
        $poList = if ($existingPOs.Data -is [System.Collections.IEnumerable] -and $existingPOs.Data -isnot [string]) {
            @($existingPOs.Data)
        } elseif ($existingPOs.Data.items) {
            @($existingPOs.Data.items)
        } else { @() }
        $winnerId = $wf.Data.recommendation.recommendedQuotationId
        $matchingPO = @($poList | Where-Object { $_.quotationId -eq $winnerId })
        $poOk = ($matchingPO.Count -ge 1)
        $poDetail = if ($poOk) { "Existing PO #($($matchingPO[0].id)) already used winning quotation #$winnerId (§5.10 duplicate block)" } else { "Decision HTTP $($decision.Status); no PO found matching winner #$winnerId" }
    } else {
        $poDetail = 'no purchaseOrderId on the workflow'
    }
    Add-Check -Group 'Approval' -Label 'Purchase order was created from the winning recommendation' -Pass $poOk -Detail $poDetail

    # §5.10: accepted quotations from other bidders are rejected once the PO is saved.
    $quotations = Invoke-ApiRequest -Uri "$ApiBase/api/material-requests/$($script:requestId)/quotations" -Headers $managerHeaders
    $qItems = if ($quotations.Data -is [System.Collections.IEnumerable] -and $quotations.Data -isnot [string]) {
        @($quotations.Data)
    } elseif ($quotations.Data.items) {
        @($quotations.Data.items)
    } else { @() }
    # The API returns a flat array of quotation DTOs (not a paginated envelope).
    $winnerId = $wf.Data.recommendation.recommendedQuotationId
    $losers = @($qItems | Where-Object { $_.id -ne $winnerId })
    $rejectedLosers = @($losers | Where-Object { $_.status -eq 'Rejected' })
    $losersOk = if ($losers.Count -gt 0) { (@($rejectedLosers).Count -eq $losers.Count) } else { $true }
    Add-Check -Group 'Approval' -Label 'Accepted quotations are rejected when the PO saves (§5.10)' `
        -Pass $losersOk -Detail "losers=[$($losers.Count)]; rejected=$($rejectedLosers.Count)"
}

# --------------------------------------------------------------------------- run
function Invoke-RunAll {
    Write-Host ""
    Write-Section 'Component 2 verification: AI agent -> human gate -> purchase order'
    Write-Host "  API : $ApiBase"
    Write-Host "  Agent: $AgentBase"
    Write-Host "  Web : $WebBase"
    Write-Host ""

    Invoke-BuildSection
    Invoke-AgentTestSection
    Invoke-ApiTestSection
    Invoke-WebTestSection
    Invoke-ServiceSection
    Invoke-SmokeTestSection
    Invoke-AgentPathSection
    Invoke-ApprovalGateSection

    if ($IncludeApprovalPath) {
        Invoke-ApprovalPathSection
    } else {
        Write-Section '9. Approval -> PO'
        Add-Check -Group 'Approval' -Label 'Manager approval -> PO creation' -Skip -SkipReason 'not run (add -IncludeApprovalPath to verify it)'
    }

    Write-Host ""
    $all = $script:checks
    $passed =  ($all | Where-Object { $_.State -eq 'PASS' }).Count
    $failed =  ($all | Where-Object { $_.State -eq 'FAIL' }).Count
    $skipped = ($all | Where-Object { $_.State -eq 'SKIP' }).Count
    Write-Host ("RESULT: {0} passed, {1} failed, {2} skipped (of {3} total)" -f $passed, $failed, $skipped, $all.Count) -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Red' })
    Write-Host ""

    if ($failed -gt 0) {
        Write-Host 'FAILED CHECKS:' -ForegroundColor Yellow
        $all | Where-Object { $_.State -eq 'FAIL' } | ForEach-Object {
            Write-Host ("  [{0}] {1}" -f $_.Group, $_.Label)
            Write-Host ("    detail: {0}" -f $_.Detail)
        }
        exit 1
    }
    Write-Host 'All checks passed - Component 2 is working end to end.' -ForegroundColor Green
    exit 0
}

Invoke-RunAll

