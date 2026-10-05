[CmdletBinding()]
param(
    [switch]$SkipLive,
    [switch]$SkipMobile,
    [switch]$SkipPerformance,
    [switch]$IncludeFullJourney,
    [int]$WorkflowId = 1
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$results = [System.Collections.Generic.List[object]]::new()

function Add-Result {
    param([string]$Name, [bool]$Passed, [string]$Detail)
    $status = if ($Passed) { 'PASS' } else { 'FAIL' }
    $results.Add([pscustomobject]@{ Check = $Name; Status = $status; Detail = $Detail })
    Write-Host "[$status] $Name - $Detail" -ForegroundColor $(if ($Passed) { 'Green' } else { 'Red' })
}

function Invoke-NativeGate {
    param(
        [string]$Name,
        [string]$Directory,
        [string]$Executable,
        [string[]]$Arguments
    )
    if (-not (Get-Command $Executable -ErrorAction SilentlyContinue)) {
        Add-Result $Name $false "command not found: $Executable"
        return
    }
    Push-Location $Directory
    try {
        & $Executable @Arguments
        $code = $LASTEXITCODE
    } catch {
        Add-Result $Name $false $_.Exception.Message
        return
    } finally {
        Pop-Location
    }
    Add-Result $Name ($code -eq 0) "$Executable $($Arguments -join ' ')"
}

function Invoke-AgentContract {
    param(
        [int]$Port,
        [string]$Uri,
        [hashtable]$Body,
        [string]$ExpectedProperty,
        [object]$ExpectedValue,
        [string]$ExpectedArrayContains
    )
    try {
        $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 `
            -Method Post -Uri "http://127.0.0.1:$Port$Uri" `
            -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
        $result = $response.Content | ConvertFrom-Json
        $passed = $response.StatusCode -eq 200 -and
            $result.PSObject.Properties.Name -contains $ExpectedProperty
        if ($ExpectedArrayContains) {
            $passed = $passed -and $result.$ExpectedProperty -contains $ExpectedArrayContains
        } else {
            $passed = $passed -and $result.$ExpectedProperty -eq $ExpectedValue
        }
        Add-Result "Agent $Port contract $Uri" $passed `
            "HTTP $($response.StatusCode), $ExpectedProperty=$($result.$ExpectedProperty)"
    } catch {
        Add-Result "Agent $Port contract $Uri" $false $_.Exception.Message
    }
}

Write-Host 'BuildWise complete project verification' -ForegroundColor Cyan
Write-Host 'Automated tests' -ForegroundColor Cyan

Invoke-NativeGate 'Backend tests (.NET 8)' $repoRoot 'dotnet' @(
    'test', 'backend\BuildWise.Api.Tests\BuildWise.Api.Tests.csproj',
    '-c', 'Release', '--nologo', '--verbosity', 'minimal'
)
Invoke-NativeGate 'All four Python agents' $repoRoot 'python' @(
    '-m', 'pytest', '-q', 'backend\agent_service'
)
Invoke-NativeGate 'Web lint' "$repoRoot\web\buildwise-web" 'npm' @('run', 'lint')
Invoke-NativeGate 'Web tests' "$repoRoot\web\buildwise-web" 'npm' @('test', '--', '--run')
Invoke-NativeGate 'Web production build' "$repoRoot\web\buildwise-web" 'npm' @('run', 'build')

if (-not $SkipMobile) {
    Invoke-NativeGate 'Flutter static analysis' "$repoRoot\mobile\buildwise_mobile" 'flutter' @('analyze')
    Invoke-NativeGate 'Flutter tests' "$repoRoot\mobile\buildwise_mobile" 'flutter' @('test')
}

if (-not $SkipLive) {
    Write-Host 'Live stack checks' -ForegroundColor Cyan

    Invoke-NativeGate 'PostgreSQL + API + web + agent 8001' $repoRoot 'powershell.exe' @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $PSScriptRoot 'check-services.ps1')
    )

    $agentContracts = @(
        @{ Port = 8001; Service = 'quotation_agent' },
        @{ Port = 8002; Service = 'request_agent' },
        @{ Port = 8003; Service = 'delivery_agent' },
        @{ Port = 8004; Service = 'quality_risk_agent' }
    )
    foreach ($agent in $agentContracts) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -TimeoutSec 5 `
                -Uri "http://127.0.0.1:$($agent.Port)/health"
            $health = $response.Content | ConvertFrom-Json
            $passed = $response.StatusCode -eq 200 -and
                $health.status -eq 'healthy' -and
                $health.service -eq $agent.Service
            Add-Result "Agent $($agent.Port) health" $passed `
                "HTTP $($response.StatusCode), service=$($health.service)"
        } catch {
            Add-Result "Agent $($agent.Port) health" $false $_.Exception.Message
        }
    }

    Invoke-AgentContract 8002 '/api/agent/analyze-request' @{
        request_id = 1; project_name = 'Verification project'; required_date = '2030-01-01'
        reason = 'urgent foundation work'; items_count = 3
    } 'flags' @('HIGH_URGENCY') 'HIGH_URGENCY'
    Invoke-AgentContract 8003 '/api/agent/analyze-discrepancy' @{
        purchase_order_id = 1; material_name = 'Cement'; ordered_qty = 100
        received_qty = 90; damaged_qty = 2
    } 'shortage_detected' $true
    Invoke-AgentContract 8004 '/api/agent/analyze-quality-risk' @{
        delivery_id = 1
        items = @(@{
            material_name = 'Cement'; inspected_qty = 100; accepted_qty = 70
            rejected_qty = 30; major_defects = 3; critical_defects = 0
            rejection_reason = 'Cracked bags'
        })
    } 'requires_ncr' $true

    Invoke-NativeGate 'Live RBAC and validation matrix' $repoRoot 'powershell.exe' @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $PSScriptRoot 'verify-rbac.ps1')
    )

    if (-not $SkipPerformance) {
        Invoke-NativeGate 'Concurrent API and agent performance' $repoRoot 'powershell.exe' @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $PSScriptRoot 'run-performance-tests.ps1')
        )
    }

    Invoke-NativeGate 'Component 2 end-to-end smoke test' $repoRoot 'powershell.exe' @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
        (Join-Path $PSScriptRoot 'smoke-test.ps1'), '-WorkflowId', $WorkflowId
    )

    if ($IncludeFullJourney) {
        Invoke-NativeGate 'Complete C1-C4 agent journey' $repoRoot 'powershell.exe' @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
            (Join-Path $PSScriptRoot 'verify-full-journey.ps1')
        )
    }
}

$failed = @($results | Where-Object Status -eq 'FAIL')
Write-Host ''
Write-Host "Verification summary: $($results.Count - $failed.Count)/$($results.Count) passed." -ForegroundColor $(if ($failed.Count -eq 0) { 'Green' } else { 'Yellow' })
if ($failed.Count -gt 0) {
    $failed | Format-Table -AutoSize | Out-String | Write-Host
    Write-Host 'Automated checks do not replace the manual browser/device and RBAC walkthrough in docs/project_verification_guide.md.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'All requested automated checks passed. Complete the manual checks in docs/project_verification_guide.md before release.' -ForegroundColor Green
exit 0
