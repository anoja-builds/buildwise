[CmdletBinding()]
param([string]$ApiBase = 'http://127.0.0.1:5078/api')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Acquire-Token([string]$email) {
    $body = @{ email = $email; password = 'Passw0rd!' } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}
function Headers([string]$token, [bool]$json = $false) {
    $headers = @{ Authorization = "Bearer $token" }
    if ($json) { $headers['Content-Type'] = 'application/json' }
    return $headers
}
function Post([string]$path, [string]$token, [object]$body) {
    return Invoke-RestMethod -Method Post -Uri "$ApiBase$path" -Headers (Headers $token $true) -Body ($body | ConvertTo-Json -Depth 10)
}
function Get([string]$path, [string]$token) {
    return Invoke-RestMethod -Method Get -Uri "$ApiBase$path" -Headers (Headers $token)
}
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Host "[PASS] $message"
}

$engineer = Acquire-Token 'site.engineer@buildwise.demo'
$officer = Acquire-Token 'procurement.officer@buildwise.demo'
$manager = Acquire-Token 'procurement.manager@buildwise.demo'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$requiredDate = (Get-Date).AddDays(10).ToString('yyyy-MM-dd')
$request = Post '/material-requests' $engineer @{ projectId = 1; requiredDate = $requiredDate; reason = "Agent 4 late-delivery validation $stamp"; items = @(@{ materialId = 1; requestedQuantity = 250; notes = 'Safety-agent golden case' }) }
$null = Post "/material-requests/$($request.id)/approval" $manager @{ decision = 'Approved'; comments = 'Approved for Agent 4 validation test.' }
$detail = Get "/material-requests/$($request.id)" $manager
$itemId = $detail.items[0].id
$supplierResponse = Get '/suppliers?page=1&pageSize=100' $officer
$supplier = $supplierResponse.items | Where-Object { $_.status -eq 'Active' } | Select-Object -First 1
$quotationDate = (Get-Date).ToString('yyyy-MM-dd')
$validUntil = (Get-Date).AddDays(20).ToString('yyyy-MM-dd')
$lateDate = (Get-Date).AddDays(12).ToString('yyyy-MM-dd')
$quotation = Post "/material-requests/$($request.id)/quotations" $officer @{ supplierId = $supplier.id; quotationDate = $quotationDate; validUntil = $validUntil; promisedDeliveryDate = $lateDate; items = @(@{ materialRequestItemId = $itemId; quantity = 250; unitPrice = 2100 }) }
Assert-True ($quotation.promisedDeliveryDate -eq $lateDate) 'late promised-delivery quotation is persisted'

$start = Post "/material-requests/$($request.id)/procurement-workflow" $officer @{ objective = 'Validate that a late supplier recommendation requires revision.' }
Assert-True ($start.status -eq 'RevisionRequired') 'validation failure moves workflow to RevisionRequired'
$workflow = Get "/procurement-workflow/$($start.workflowId)" $manager
Assert-True ($workflow.validation.isValid -eq $false) 'ProcurementValidationAgent output is valid=false'
Assert-True (@($workflow.validation.errors).Count -ge 1) 'validation errors are returned without inventing an answer'
Assert-True ($workflow.approvalStatus -eq 'RevisionRequested') 'workflow approval state requests revision'

$history = Get "/agent-workflows/$($start.workflowId)" $manager
$validationStep = $history.steps | Where-Object { $_.agentRole -eq 'ProcurementValidationAgent' } | Select-Object -First 1
Assert-True ($null -ne $validationStep -and $validationStep.status -eq 'Completed') 'validation agent execution is persisted as completed'
$validationJson = $validationStep.validationResult | ConvertFrom-Json
Assert-True ($validationJson.valid -eq $false -and @($validationJson.errors).Count -ge 1) 'persisted validation JSON matches the required output contract'
Assert-True ($null -ne $validationJson.PSObject.Properties['warnings']) 'validation output includes warnings array'

$status = 0
try {
    $null = Post "/procurement-workflow/$($start.workflowId)/purchase-order" $manager @{}
    $status = 200
} catch {
    $status = [int]$_.Exception.Response.StatusCode
}
Assert-True ($status -eq 400) 'purchase-order authorization is blocked for RevisionRequired workflow'
Write-Host "Validation & Safety Agent verification passed: request #$($request.id), workflow #$($start.workflowId)." -ForegroundColor Green
