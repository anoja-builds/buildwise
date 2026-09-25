[CmdletBinding()]
param(
    [string]$ApiBase = 'http://127.0.0.1:5078/api',
    [string]$Password = 'Passw0rd!'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$Step, [bool]$Passed, [string]$Detail) {
    $results.Add([pscustomobject]@{ Step = $Step; Status = $(if ($Passed) { 'PASS' } else { 'FAIL' }); Detail = $Detail })
    Write-Host "[$(if ($Passed) { 'PASS' } else { 'FAIL' })] $Step - $Detail"
}

function Login([string]$Email) {
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}

function Headers([string]$Token) {
    return @{ Authorization = "Bearer $Token"; 'Content-Type' = 'application/json' }
}

function Post([string]$Path, [string]$Token, [object]$Body) {
    return Invoke-RestMethod -Method Post -Uri "$ApiBase$Path" -Headers (Headers $Token) -Body ($Body | ConvertTo-Json -Depth 10)
}

function Get([string]$Path, [string]$Token) {
    return Invoke-RestMethod -Method Get -Uri "$ApiBase$Path" -Headers (Headers $Token)
}

try {
    $engineer = Login 'site.engineer@buildwise.demo'
    $officer = Login 'site.officer@buildwise.demo'
    $procurementOfficer = Login 'procurement.officer@buildwise.demo'
    $manager = Login 'procurement.manager@buildwise.demo'
    $inspector = Login 'quality.inspector@buildwise.demo'

    $suppliers = @(Get '/suppliers?page=1&pageSize=100' $procurementOfficer).items
    $supplierA = $suppliers | Where-Object { $_.name -like 'Supplier A*' } | Select-Object -First 1
    $supplierB = $suppliers | Where-Object { $_.name -like 'Supplier B*' } | Select-Object -First 1
    $supplierC = $suppliers | Where-Object { $_.name -like 'Supplier C*' } | Select-Object -First 1
    Add-Check 'Seeded supplier scenario' ($supplierA.status -eq 'Active' -and $supplierB.status -eq 'Suspended' -and $supplierC.status -eq 'Active') "A=$($supplierA.status), B=$($supplierB.status), C=$($supplierC.status)"

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
    $request = Post '/material-requests' $engineer @{
        projectId = 1
        requiredDate = (Get-Date).AddDays(10).ToString('yyyy-MM-dd')
        reason = "Full journey $stamp - urgent foundation work"
        items = @(@{ materialId = 1; requestedQuantity = 250; notes = 'OPC Cement 250 bags' })
    }
    Add-Check 'C1 site user creates request' ($request.status -eq 'PendingApproval') "request #$($request.id)"

    $requestAnalysis = Post "/agent/analyze-request/$($request.id)" $manager @{}
    Add-Check 'C1 request agent 8002 runs through API' ($requestAnalysis.status -eq 'Analyzed') "flags=$($requestAnalysis.flags -join ',')"

    $null = Post "/material-requests/$($request.id)/approval" $manager @{ decision = 'Approved'; comments = 'Approved by full-journey verifier.' }
    $requestAfterApproval = Get "/material-requests/$($request.id)" $manager
    $requestItemId = $requestAfterApproval.items[0].id
    Add-Check 'C1 manager approves request' ($requestAfterApproval.status -eq 'Approved') "request #$($request.id)"

    $today = (Get-Date).ToString('yyyy-MM-dd')
    $validUntil = (Get-Date).AddDays(14).ToString('yyyy-MM-dd')
    $requiredDate = (Get-Date $requestAfterApproval.requiredDate).ToString('yyyy-MM-dd')
    $quotes = @(
        @{ supplierId = $supplierA.id; quotationDate = $today; validUntil = $validUntil; promisedDeliveryDate = $requiredDate; items = @(@{ materialRequestItemId = $requestItemId; quantity = 250; unitPrice = 2100 }) },
        @{ supplierId = $supplierB.id; quotationDate = $today; validUntil = $validUntil; promisedDeliveryDate = $requiredDate; items = @(@{ materialRequestItemId = $requestItemId; quantity = 250; unitPrice = 2040 }) },
        @{ supplierId = $supplierC.id; quotationDate = $today; validUntil = $validUntil; promisedDeliveryDate = $requiredDate; items = @(@{ materialRequestItemId = $requestItemId; quantity = 200; unitPrice = 1900 }) }
    )
    $createdQuotes = @($quotes | ForEach-Object { Post "/material-requests/$($request.id)/quotations" $procurementOfficer $_ })
    Add-Check 'C2 officer records three quotations' ($createdQuotes.Count -eq 3) "quotations=$($createdQuotes.id -join ',')"

    $workflowResponse = Post "/material-requests/$($request.id)/procurement-workflow" $procurementOfficer @{ objective = 'Award 250 bags of OPC Cement using the lowest-cost fully compliant Active supplier.' }
    $workflowId = $workflowResponse.workflowId
    $workflow = Get "/procurement-workflow/$workflowId" $procurementOfficer
    $recommendation = $workflow.recommendation
    $winner = $createdQuotes | Where-Object { $_.id -eq $recommendation.recommendedQuotationId } | Select-Object -First 1
    $winnerSupplier = $suppliers | Where-Object { $_.id -eq $winner.supplierId } | Select-Object -First 1
    Add-Check 'C2 quotation agent 8001 recommends Supplier A' ($workflow.status -eq 'AwaitingApproval' -and $winnerSupplier.name -like 'Supplier A*' -and $recommendation.warnings.Count -ge 2) "workflow #$workflowId winner=$($winnerSupplier.name)"
    Add-Check 'C2 deterministic validation passes' ($workflow.validation.isValid -eq $true) ($workflow.validation.errors -join '; ')

    $null = Post "/procurement-workflow/$workflowId/decision" $manager @{ decision = 'Approved'; comment = 'Approved after reviewing agent warnings and validation.' }
    $approvedWorkflow = Get "/procurement-workflow/$workflowId" $manager
    $purchaseOrder = Get "/purchase-orders/$($approvedWorkflow.purchaseOrderId)" $manager
    Add-Check 'C2 manager approval creates Confirmed PO' ($purchaseOrder.status -eq 'Confirmed' -and [decimal]$purchaseOrder.totalAmount -eq 525000) "PO #$($purchaseOrder.id), total=$($purchaseOrder.totalAmount)"

    $reference = "E2E-$stamp"
    $delivery = Post '/deliveries' $officer @{
        purchaseOrderId = $purchaseOrder.id
        deliveryReference = $reference
        items = @(@{ materialId = 1; receivedQuantity = 240; damagedQuantity = 5 })
    }
    Add-Check 'C3 site officer records short/damaged delivery' ($delivery.status -eq 'DiscrepancyReported') "delivery #$($delivery.id), ref=$reference"

    $inspection = Post '/quality-inspections' $inspector @{
        deliveryId = $delivery.id
        items = @(@{ materialId = 1; inspectedQuantity = 240; acceptedQuantity = 235; rejectedQuantity = 5; rejectionReason = 'Water damage during transport.' })
    }
    Add-Check 'C4 quality inspector completes inspection' ($inspection.status -eq 'Completed' -and $inspection.overallDecision -eq 'PartiallyAccepted') "inspection #$($inspection.id)"

    $ncrs = @(Get '/quality-inspections/non-conformances' $inspector)
    $ncr = $ncrs | Where-Object { $_.issueDescription -eq 'Water damage during transport.' } | Select-Object -First 1
    Add-Check 'C4 rejected quantity creates corrective-action NCR' ($null -ne $ncr -and $ncr.status -eq 'CorrectiveActionRequired') "NCR=$($ncr.ncrNumber)"

    $allWorkflows = @(Get '/agent-workflows?page=1&pageSize=100' $manager).items
    $c1 = $allWorkflows | Where-Object { $_.objective -eq "Identify planning risks for material request #$($request.id)." } | Select-Object -First 1
    $c2 = $allWorkflows | Where-Object { $_.id -eq $workflowId } | Select-Object -First 1
    $c3 = $allWorkflows | Where-Object { $_.objective -eq "Reconcile delivery $reference against its confirmed purchase order." } | Select-Object -First 1
    $c4 = $allWorkflows | Where-Object { $_.objective -eq "Assess quality risk for delivery #$($delivery.id) and determine NCR corrective action." } | Select-Object -First 1
    Add-Check 'C1-C4 agent histories are persisted' ($null -ne $c1 -and $null -ne $c2 -and $null -ne $c3 -and $null -ne $c4) "workflows=$($c1.id),$($c2.id),$($c3.id),$($c4.id)"

    $c2History = Get "/agent-workflows/$workflowId" $manager
    $c2Roles = @($c2History.steps.agentRole)
    $c3History = Get "/agent-workflows/$($c3.id)" $manager
    $c4History = Get "/agent-workflows/$($c4.id)" $manager
    $rolesValid = ($c2Roles -contains 'ProcurementPlanningAgent' -and $c2Roles -contains 'QuotationSupplierAnalysisAgent' -and $c2Roles -contains 'ProcurementValidationAgent') -and
        (@($c3History.steps.agentRole) -contains 'DeliveryDiscrepancyAgent') -and
        (@($c4History.steps.agentRole) -contains 'QualityRiskAnalysisAgent')
    Add-Check 'Four distinct agent contributions are visible' $rolesValid 'C2 planning/quotation/validation; C3 delivery; C4 quality'

    $planningStep = @($c2History.steps | Where-Object { $_.agentRole -eq 'ProcurementPlanningAgent' })[0]
    $planning = if ($null -ne $planningStep -and $planningStep.structuredResult) { $planningStep.structuredResult | ConvertFrom-Json } else { $null }
    $planningValid = $null -ne $planning -and
        @($planning.allowedTools).Count -eq 4 -and
        @($planning.allowedTools) -notcontains 'Approve procurement' -and
        @($planning.allowedTools) -notcontains 'Issue purchase order' -and
        @($planning.prohibitedCapabilities) -contains 'Modify supplier records' -and
        @($planning.steps).Count -ge 4 -and
        @($planning.requiredChecks).Count -ge 4
    Add-Check 'Agent 1 plan and safety boundary are auditable' $planningValid "tools=$(@($planning.allowedTools) -join ',') steps=$(@($planning.steps).Count)"

    $finalProcurement = Get "/material-requests/$($request.id)/procurement-status" $engineer
    Add-Check 'Initiating site user sees updated status' ($finalProcurement.status -eq 'PurchaseOrderCreated' -and $finalProcurement.purchaseOrderId -eq $purchaseOrder.id) "status=$($finalProcurement.status), PO #$($finalProcurement.purchaseOrderId)"
} catch {
    Add-Check 'Full journey execution' $false $_.Exception.Message
}

$failed = @($results | Where-Object Status -eq 'FAIL')
Write-Host "`nFull journey summary: $($results.Count - $failed.Count)/$($results.Count) passed." -ForegroundColor $(if ($failed.Count -eq 0) { 'Green' } else { 'Yellow' })
if ($failed.Count -gt 0) { exit 1 }
exit 0
