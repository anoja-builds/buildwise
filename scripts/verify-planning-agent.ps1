[CmdletBinding()]
param([string]$ApiBase = 'http://127.0.0.1:5078/api')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Acquire-Token([string]$email) {
    $body = @{ email = $email; password = 'Passw0rd!' } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Host "[PASS] $message"
}

$officer = Acquire-Token 'procurement.officer@buildwise.demo'
$manager = Acquire-Token 'procurement.manager@buildwise.demo'
$approved = Invoke-RestMethod -Uri "$ApiBase/material-requests?status=Approved" -Headers @{ Authorization = "Bearer $officer" }
$request = $approved | Select-Object -First 1
if ($null -eq $request) { throw 'No approved material request is available for planning-agent verification.' }

$plan = Invoke-RestMethod -Uri "$ApiBase/procurement-planning-agent/material-requests/$($request.id)?objective=Verify%20controlled%20planning%20agent" -Headers @{ Authorization = "Bearer $officer" }
Assert-True ($plan.agentRole -eq 'ProcurementPlanningAgent') 'Agent 1 identity is ProcurementPlanningAgent'
Assert-True ($plan.objective -eq 'Verify controlled planning agent') 'domain objective is retained'
Assert-True ((@($plan.allowedTools) -join ',') -eq 'GetMaterialRequest,GetMaterialDetails,GetProjectDetails,GetAvailableQuotations') 'only the four PRD allow-listed tools are exposed'
Assert-True ((@($plan.toolResults).Count) -eq 4) 'all four allow-listed tools return auditable summaries'
Assert-True ((@($plan.steps).Count) -ge 4) 'a structured multi-step plan is returned'
Assert-True (@($plan.steps | Where-Object { $_.delegatedAgentRole -eq 'DeliveryRiskAgent' }).Count -eq 1) 'delivery risk is explicitly delegated'
Assert-True (@($plan.steps | Where-Object { $_.requiresHumanApproval -and $_.delegatedAgentRole -eq 'ProcurementManager' }).Count -eq 1) 'plan pauses for Procurement Manager approval'
Assert-True ((@($plan.requiredChecks).Count) -ge 4) 'required deterministic checks are defined'
Assert-True ((@($plan.prohibitedCapabilities) -join ',') -eq 'Approve procurement,Issue purchase order,Modify supplier records') 'forbidden capabilities are explicit'
Assert-True ($plan.input.materialRequestId -eq $request.id) 'input facts are grounded in PostgreSQL request data'

$workflowId = $null
$workflows = Invoke-RestMethod -Uri "$ApiBase/agent-workflows?page=1&pageSize=100" -Headers @{ Authorization = "Bearer $manager" }
foreach ($workflow in @($workflows.items)) {
    if ($workflow.materialRequestId -eq $request.id) {
        $detail = Invoke-RestMethod -Uri "$ApiBase/agent-workflows/$($workflow.id)" -Headers @{ Authorization = "Bearer $manager" }
        if (@($detail.steps.agentRole) -contains 'ProcurementPlanningAgent') { $workflowId = $workflow.id; break }
    }
}
Assert-True ($null -ne $workflowId) 'a persisted workflow contains the Agent 1 planning step'
$history = Invoke-RestMethod -Uri "$ApiBase/agent-workflows/$workflowId" -Headers @{ Authorization = "Bearer $manager" }
$planningStep = $history.steps | Where-Object { $_.agentRole -eq 'ProcurementPlanningAgent' } | Select-Object -First 1
$persisted = $planningStep.structuredResult | ConvertFrom-Json
Assert-True ((@($persisted.allowedTools).Count) -eq 4) 'persisted step JSON contains the four-tool allow-list'
Assert-True (@($persisted.prohibitedCapabilities) -contains 'Issue purchase order') 'persisted step JSON contains the safety boundary'
Write-Host "Procurement Planning Agent verification passed for request #$($request.id), workflow #$workflowId." -ForegroundColor Green
