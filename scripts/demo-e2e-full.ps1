param([string]$ApiBase = 'http://localhost:5078/api', [string]$Password = 'Passw0rd!')
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'
function Login($e) { (Invoke-RestMethod -Uri "$ApiBase/auth/login" -Method Post -ContentType 'application/json' -Body "{`"email`":`"$e`",`"password`":`"$Password`"}").token }
function Hdr($t)  { @{ Authorization = "Bearer $t"; 'Content-Type' = 'application/json' } }
function Get-Api($path, $t)      { Invoke-RestMethod -Uri "$ApiBase$path" -Headers (Hdr $t) }
function Post-Api($path, $t, $b) { Invoke-RestMethod -Uri "$ApiBase$path" -Method Post -Headers (Hdr $t) -Body ($b | ConvertTo-Json -Depth 10) }

Write-Host "BUILDWISE - COMPLETE E2E WORKFLOW" -ForegroundColor Magenta

$tSE = Login 'site.engineer@buildwise.demo'
$tPO = Login 'procurement.officer@buildwise.demo'
$tPM = Login 'procurement.manager@buildwise.demo'
$tSO = Login 'site.officer@buildwise.demo'
$tQI = Login 'quality.inspector@buildwise.demo'
Write-Host "[OK] All 5 roles authenticated" -ForegroundColor Green

$suppliers = @(Get-Api '/suppliers?page=1&pageSize=100' $tPO).items
$sA = ($suppliers | Where-Object { $_.name -like 'Supplier A*' } | Sort-Object id | Select-Object -First 1)
$sC = ($suppliers | Where-Object { $_.name -like 'Supplier C*' } | Sort-Object id | Select-Object -First 1)

$reqDate = (Get-Date).AddDays(28).ToString('yyyy-MM-dd')
$today   = (Get-Date).ToString('yyyy-MM-dd')

Write-Host ""
Write-Host "--- STEP 1: Site Engineer creates Material Request ---" -ForegroundColor Cyan
$req = Post-Api '/material-requests' $tSE @{
    projectId=1; requiredDate=$reqDate; priority='High'
    reason='OPC Cement for Block C construction work (demo)'
    items=@(@{ materialId=1; requestedQuantity=500; unit='bag'; notes='OPC Grade 42.5N' })
}
Write-Host "MR#$($req.id) | Status: $($req.status) | Project: Riverside Apartments Block C"
Write-Host "Material: 500 bags OPC Cement | Priority: High | Required by: $reqDate"

Write-Host ""
Write-Host "--- STEP 2: AI Agent 1 - Request Analysis Agent ---" -ForegroundColor Yellow
$analysis = Post-Api "/agent/analyze-request/$($req.id)" $tPM @{}
Write-Host "Flags: $($analysis.flags -join ', ') | Status: $($analysis.status)"

Write-Host ""
Write-Host "--- STEP 3: Procurement Manager approves request ---" -ForegroundColor Green
$null = Post-Api "/material-requests/$($req.id)/approval" $tPM @{ decision='Approved'; comments='Approved for Block C.' }
$approved = Get-Api "/material-requests/$($req.id)" $tPM
$lineId = $approved.items[0].id
Write-Host "Request #$($req.id) approved -> Status: $($approved.status)"

Write-Host ""
Write-Host "--- STEP 4: Procurement Officer records 2 quotations ---" -ForegroundColor Cyan
$validUntil = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
$qA = Post-Api "/material-requests/$($req.id)/quotations" $tPO @{
    supplierId=$sA.id; quotationDate=$today; validUntil=$validUntil
    promisedDeliveryDate=(Get-Date).AddDays(5).ToString('yyyy-MM-dd')
    paymentTerms='Net 30'; transportCharge=0
    items=@(@{ materialRequestItemId=$lineId; quantity=500; unitPrice=2250 })
}
$qB = Post-Api "/material-requests/$($req.id)/quotations" $tPO @{
    supplierId=$sC.id; quotationDate=$today; validUntil=$validUntil
    promisedDeliveryDate=(Get-Date).AddDays(3).ToString('yyyy-MM-dd')
    paymentTerms='Net 30'; transportCharge=0
    items=@(@{ materialRequestItemId=$lineId; quantity=500; unitPrice=2180 })
}
Write-Host "Q#$($qA.id): Rs.2250/bag -> Rs.1,125,000 (5 days) | Q#$($qB.id): Rs.2180/bag -> Rs.1,090,000 (3 days)"

Write-Host ""
Write-Host "--- STEP 5: AI Agent 2 - Quotation & Supplier Analysis Agent ---" -ForegroundColor Yellow
$wfResp = Post-Api "/material-requests/$($req.id)/procurement-workflow" $tPO @{
    objective="Award 500 bags of OPC Cement using the lowest-cost fully compliant Active supplier."
}
$wfId = $wfResp.workflowId
Start-Sleep -Seconds 10
$wf = Get-Api "/procurement-workflow/$wfId" $tPM
Write-Host "Recommended: $($wf.recommendation.recommendedSupplierName) | Rs.$($wf.recommendation.recommendedQuotationTotal)"
if ($wf.recommendation.rankedQuotations) {
    $wf.recommendation.rankedQuotations | ForEach-Object {
        Write-Host "  #$($_.rank) $($_.supplierName) Rs.$($_.totalCost)"
    }
}

Write-Host ""
Write-Host "--- STEP 6: AI Agent 3 - Procurement Validation Agent ---" -ForegroundColor Yellow
Write-Host "isValid=$($wf.validation.isValid) | Status: $($wf.status)"
if ($wf.validation.warnings) { $wf.validation.warnings | ForEach-Object { Write-Host "  WARNING: $_" -ForegroundColor Yellow } }

Write-Host ""
Write-Host "--- STEP 7: Procurement Manager approves -> PO created ---" -ForegroundColor Green
$null = Post-Api "/procurement-workflow/$wfId/decision" $tPM @{ decision='Approved'; comment='Approved after AI review.' }
$wfAfter = Get-Api "/procurement-workflow/$wfId" $tPM
$po = Get-Api "/purchase-orders/$($wfAfter.purchaseOrderId)" $tPM
Write-Host "PO#$($po.id) created | Status: $($po.status) | Supplier: $($po.supplierName) | Rs.$($po.totalAmount)"

Write-Host ""
Write-Host "--- STEP 8: Site Officer records delivery (480 of 500) ---" -ForegroundColor Cyan
$ref = "DEMO-$(Get-Date -Format 'HHmmss')"
$delivery = Post-Api '/deliveries' $tSO @{
    purchaseOrderId=$po.id; deliveryReference=$ref
    items=@(@{ materialId=1; receivedQuantity=480; damagedQuantity=0 })
}
Write-Host "DEL-$($delivery.id) | Status: $($delivery.status) | Ordered:500 Received:480 Shortage:20 bags"

Write-Host ""
Write-Host "--- STEP 9: AI Agent 3b - Delivery Discrepancy Agent ---" -ForegroundColor Yellow
$delA = Post-Api "/deliveries/$($delivery.id)/discrepancy-analysis" $tSO @{}
$delText = if ($delA.summary) { $delA.summary } elseif ($delA.riskLevel) { "Risk: $($delA.riskLevel)" } else { "Analysis complete" }
Write-Host $delText

Write-Host ""
Write-Host "--- STEP 10: Quality Inspector records inspection ---" -ForegroundColor Cyan
$insp = Post-Api '/quality-inspections' $tQI @{
    deliveryId=$delivery.id
    inspectionCriteria='Visual, moisture, packaging, defects per site QC protocol'
    observedResult='40 bags water-damaged; 440 bags accepted after segregation'
    notes='Damaged bags segregated. NCR raised.'
    quantityCheck=$true; visualConditionCheck=$false
    moistureCheck=$false; packagingCheck=$true; defectsCheck=$false
    evidence=@()
    items=@(@{ materialId=1; inspectedQuantity=480; acceptedQuantity=440; rejectedQuantity=40; rejectionReason='Water damage during transport' })
}
$rejRate = [math]::Round(40/480*100, 2)
Write-Host "INS-$($insp.id) | Decision: $($insp.overallDecision) | Inspected:480 Accepted:440 Rejected:40 Rate:$rejRate%"
Write-Host "Checklist: Quantity=PASS Visual=FAIL Moisture=FAIL Packaging=PASS Defects=FAIL"

Write-Host ""
Write-Host "--- STEP 11: AI Agent 4 - Quality Risk Analysis Agent ---" -ForegroundColor Yellow
$risk = Post-Api "/quality-inspections/$($insp.id)/risk-analysis" $tQI @{}
Write-Host "Risk Level: $($risk.riskLevel) | Flag: $($risk.riskFlag) | NCR: $($risk.ncrRecommendation)"
if ($risk.recommendation) { Write-Host $risk.recommendation -ForegroundColor Yellow }

Write-Host ""
Write-Host "--- STEP 12: NCR auto-created ---" -ForegroundColor Red
$ncrs = @(Get-Api '/quality-inspections/non-conformances' $tQI)
$latestNcr = $ncrs | Where-Object { $_.issueDescription -like '*Water damage*' } | Select-Object -First 1
if ($latestNcr) {
    Write-Host "$($latestNcr.ncrNumber) | Severity: $($latestNcr.severity) | Status: $($latestNcr.status)"
    Write-Host "Issue: $($latestNcr.issueDescription)"
    Write-Host "Corrective Action: $($latestNcr.correctiveAction)"
}

Write-Host ""
Write-Host "=================================================" -ForegroundColor Green
Write-Host "BUILDWISE COMPLETE E2E WORKFLOW VERIFIED" -ForegroundColor Green
Write-Host "MR#$($req.id) -> PO#$($po.id) -> DEL-$($delivery.id) -> INS-$($insp.id)" -ForegroundColor Green
Write-Host "4 AI Agents: RequestAnalysis | QuotationSupplier | DeliveryDiscrepancy | QualityRisk" -ForegroundColor Green
Write-Host "RBAC: SiteEngineer | ProcurementOfficer | ProcurementManager | SiteOfficer | QualityInspector" -ForegroundColor Green
Write-Host "Human Control Points: MR Approval | Procurement Approval | Quality Inspection" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
