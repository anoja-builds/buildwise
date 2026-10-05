<#
.SYNOPSIS
    End-to-end smoke test against the running BuildWise Component 2 stack.

.DESCRIPTION
    Where check-services.ps1 answers "is it up?", this answers "is it actually working?" by exercising the
    real API, database and agent service over HTTP and asserting the spec's cement scenario outcome:

      * POST /api/auth/login                      - authentication works and issues a JWT
      * GET  /api/suppliers                       - seeded suppliers come back with their statuses
      * GET  /api/purchase-orders [+ /{id}]       - purchase orders and their line items are readable
      * POST http://127.0.0.1:8001/analyze        - the agent recommends the Active, full-coverage, cheapest
                                                    quotation and warns about the Suspended / partial ones
      * (optional) GET /api/procurement-workflow/{id}/history - the audit trail is queryable

    Exit code 0 when every assertion passes, 1 otherwise.

.PARAMETER ApiBase
    Base URL of the BuildWise API. Default: http://127.0.0.1:5078

.PARAMETER AgentBase
    Base URL of the Python agent service. Default: http://127.0.0.1:8001

.PARAMETER WorkflowId
    When set (e.g. -WorkflowId 1), also reads that workflow's step history and reports its status.

.EXAMPLE
    .\smoke-test.ps1
.EXAMPLE
    .\smoke-test.ps1 -WorkflowId 1
#>
[CmdletBinding()]
param(
    [string]$ApiBase = 'http://127.0.0.1:5078',
    [string]$AgentBase = 'http://127.0.0.1:8001',
    [int]$WorkflowId = 0
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$script:failures = 0
$script:passes = 0

function Assert-True {
    param([string]$Label, [bool]$Condition, [string]$Detail)
    if ($Condition) {
        $script:passes++
        Write-Host ("  PASS  {0}" -f $Label) -ForegroundColor Green
    } else {
        $script:failures++
        Write-Host ("  FAIL  {0}" -f $Label) -ForegroundColor Red
    }
    if ($Detail) { Write-Host ("        {0}" -f $Detail) -ForegroundColor Gray }
}

function Get-Json {
    param([string]$Uri, [hashtable]$Headers)
    $response = Invoke-WebRequest -Uri $Uri -Headers $Headers -UseBasicParsing -TimeoutSec 15
    return $response.Content | ConvertFrom-Json
}

Write-Host ''
Write-Host 'BuildWise Component 2 - end-to-end smoke test' -ForegroundColor Cyan
Write-Host ('=' * 74)

# ------------------------------------------------------------------ 1. authentication
Write-Host ''
Write-Host 'Authentication' -ForegroundColor White
$managerToken = $null
try {
    $loginBody = @{ email = 'procurement.manager@buildwise.demo'; password = 'Passw0rd!' } | ConvertTo-Json -Compress
    $login = Invoke-WebRequest -Uri "$ApiBase/api/auth/login" -Method Post -ContentType 'application/json' `
        -Body $loginBody -UseBasicParsing -TimeoutSec 15
    $managerToken = ($login.Content | ConvertFrom-Json).token
    Assert-True -Label 'Manager login returns a JWT' -Condition ([bool]$managerToken) -Detail "token length $($managerToken.Length)"
} catch {
    Assert-True -Label 'Manager login returns a JWT' -Condition $false -Detail $_.Exception.Message
}
if (-not $managerToken) {
    Write-Host ''
    Write-Host 'Cannot continue without a token - is the API running and PostgreSQL seeded?' -ForegroundColor Red
    exit 1
}
$headers = @{ Authorization = "Bearer $managerToken" }

# ------------------------------------------------------------------ 2. suppliers
Write-Host ''
Write-Host 'Suppliers (seeded cement scenario)' -ForegroundColor White
$suppliers = Get-Json -Uri "$ApiBase/api/suppliers" -Headers $headers
Assert-True -Label 'At least three suppliers are returned' -Condition ($suppliers.total -ge 3) `
    -Detail (($suppliers.items | ForEach-Object { "$($_.name) [$($_.status)]" }) -join '; ')
$suspended = @($suppliers.items | Where-Object { $_.status -eq 'Suspended' })
$active = @($suppliers.items | Where-Object { $_.status -eq 'Active' })
Assert-True -Label 'Scenario has Active suppliers and at least one Suspended supplier' `
    -Condition (($suspended.Count -ge 1) -and ($active.Count -ge 2)) `
    -Detail "active=$($active.Count) suspended=$($suspended.Count)"

# ------------------------------------------------------------------ 3. purchase orders
Write-Host ''
Write-Host 'Purchase orders (Component 3 read contract)' -ForegroundColor White
$pos = Get-Json -Uri "$ApiBase/api/purchase-orders" -Headers $headers
Assert-True -Label 'GET /api/purchase-orders succeeds' -Condition ($null -ne $pos) -Detail "total=$($pos.total)"
if ($pos.total -ge 1) {
    $first = $pos.items[0]
    $detail = Get-Json -Uri "$ApiBase/api/purchase-orders/$($first.id)" -Headers $headers
    Assert-True -Label "PO #$($first.id) detail loads with line items" -Condition ($detail.items.Count -ge 1) `
        -Detail "$($detail.supplierName) | $($detail.status) | expected $($detail.expectedDeliveryDate) | total $($detail.totalAmount)"
    $lineTotal = 0
    foreach ($line in $detail.items) { $lineTotal += [decimal]$line.orderedQuantity * [decimal]$line.unitPrice }
    Assert-True -Label 'Line totals add up to the PO total' -Condition ([math]::Abs($lineTotal - [decimal]$detail.totalAmount) -lt 0.01) `
        -Detail "sum(lines)=$lineTotal vs total=$($detail.totalAmount)"
} else {
    Write-Host '  SKIP  no purchase orders yet - approve a procurement workflow to create one' -ForegroundColor Yellow
}

# ------------------------------------------------------------------ 4. agent decision quality
Write-Host ''
Write-Host 'Agent service decision (spec cement scenario)' -ForegroundColor White
$payload = @'
{"material_request_id":1,"requested_quantities":{"1":250.0},"quotations":[
 {"quotation_id":3,"supplier_id":1,"supplier_name":"Supplier A Building Materials","supplier_status":"Active","quantity_offered":{"1":250.0},"unit_prices":{"1":2100.0},"total_amount":525000.0,"valid":true},
 {"quotation_id":6,"supplier_id":2,"supplier_name":"Supplier B Traders","supplier_status":"Suspended","quantity_offered":{"1":250.0},"unit_prices":{"1":2040.0},"total_amount":510000.0,"valid":true},
 {"quotation_id":7,"supplier_id":3,"supplier_name":"Supplier C Wholesale","supplier_status":"Active","quantity_offered":{"1":200.0},"unit_prices":{"1":2050.0},"total_amount":410000.0,"valid":true},
 {"quotation_id":8,"supplier_id":4,"supplier_name":"Supplier D Expired","supplier_status":"Active","quantity_offered":{"1":250.0},"unit_prices":{"1":1990.0},"total_amount":497500.0,"valid":false}]}
'@

try {
    $rec = Invoke-WebRequest -Uri "$AgentBase/analyze" -Method Post -ContentType 'application/json' `
        -Body $payload -UseBasicParsing -TimeoutSec 20 | Select-Object -ExpandProperty Content | ConvertFrom-Json

    Assert-True -Label 'Agent recommends Supplier A (Quotation #3)' `
        -Condition ($rec.recommended_quotation_id -eq 3) `
        -Detail "recommended=$($rec.recommended_quotation_id) ($($rec.recommended_supplier_name))"

    $rankedIds = @($rec.ranked_alternatives | ForEach-Object { $_.quotation_id })
    Assert-True -Label 'Suspended supplier (Quotation #6) is not ranked or recommended' `
        -Condition ($rankedIds -notcontains 6) -Detail "ranked: $($rankedIds -join ', ')"

    Assert-True -Label 'Expired quotation (Quotation #8) is not ranked or recommended' `
        -Condition ($rankedIds -notcontains 8) -Detail 'regression 38bbca9: invalid quotes are filtered, not merely warned about'

    Assert-True -Label 'Cheaper but partial quotation (#7) is not awarded as sole winner' `
        -Condition ($rec.recommended_quotation_id -ne 7) -Detail "ranked: $($rankedIds -join ', ')"

    $warnings = ($rec.warnings -join ' | ')
    Assert-True -Label 'Warnings explain why B and C were excluded/flagged' `
        -Condition (($warnings -match 'Suspended') -and ($warnings -match '200/250')) -Detail $warnings
} catch {
    Assert-True -Label 'Agent service /analyze responds' -Condition $false -Detail $_.Exception.Message
}

# ------------------------------------------------------------------ 5. optional workflow history
if ($WorkflowId -gt 0) {
    Write-Host ''
    Write-Host "Workflow #$WorkflowId audit trail" -ForegroundColor White
    try {
        # Note: /history returns the step array directly. Assign it without @() - in Windows
        # PowerShell 5.1, "@(array-producing pipeline)" wraps the whole array as one element.
        $history = Get-Json -Uri "$ApiBase/api/procurement-workflow/$WorkflowId/history" -Headers $headers
        $stepList = @()
        foreach ($step in $history) { if ($step.agentRole) { $stepList += $step } }
        $summary = ($stepList | ForEach-Object { "$($_.agentRole)/$($_.status)" }) -join ' -> '
        Assert-True -Label 'Workflow history is queryable' -Condition ($stepList.Count -ge 1) -Detail $summary
    } catch {
        Assert-True -Label 'Workflow history is queryable' -Condition $false -Detail $_.Exception.Message
    }
}

# ------------------------------------------------------------------ summary
Write-Host ''
Write-Host ('=' * 74)
if ($script:failures -eq 0) {
    Write-Host "Smoke test passed: $($script:passes) assertions." -ForegroundColor Green
    Write-Host ''
    exit 0
}
Write-Host "Smoke test failed: $($script:failures) of $($script:passes + $script:failures) assertions." -ForegroundColor Red
Write-Host ''
exit 1
