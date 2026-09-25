[CmdletBinding()]
param([string]$ApiBase = 'http://127.0.0.1:5078/api')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Acquire-Token([string]$email) {
    $body = @{ email = $email; password = 'Passw0rd!' } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}
function Make-Headers([string]$token, [bool]$json = $false) {
    $headers = @{ Authorization = "Bearer $token" }
    if ($json) { $headers['Content-Type'] = 'application/json' }
    return $headers
}
function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    Write-Host "[PASS] $message"
}

$inspector = Acquire-Token 'quality.inspector@buildwise.demo'
$manager = Acquire-Token 'procurement.manager@buildwise.demo'
$engineer = Acquire-Token 'site.engineer@buildwise.demo'
$deliveries = Invoke-RestMethod -Uri "$ApiBase/deliveries" -Headers (Make-Headers $inspector)
$delivery = $deliveries | Where-Object { $_.items -and @($_.items).Count -gt 0 } | Select-Object -First 1
Assert-True ($null -ne $delivery) 'a delivery with material lines exists'
$item = $delivery.items | Select-Object -First 1
if ([decimal]$item.receivedQuantity -ge 5) { $rejected = 5 } else { $rejected = 1 }
$accepted = [decimal]$item.receivedQuantity - $rejected
$payload = @{
    deliveryId = $delivery.id
    inspectionCriteria = 'Quantity, visual condition, moisture and packaging verification'
    observedResult = 'Partially accepted; damaged material segregated'
    notes = 'Component 4 automated acceptance verification'
    evidence = @(@{
        fileName = 'component4-evidence.jpg'
        fileUrl = 'https://example.test/evidence/component4-evidence.jpg'
        contentType = 'image/jpeg'
        fileSizeBytes = 1200
    })
    items = @(@{
        materialId = $item.materialId
        inspectedQuantity = $item.receivedQuantity
        acceptedQuantity = $accepted
        rejectedQuantity = $rejected
        rejectionReason = 'Damaged packaging and moisture exposure'
    })
}
$inspection = Invoke-RestMethod -Method Post -Uri "$ApiBase/quality-inspections" -Headers (Make-Headers $inspector $true) -Body ($payload | ConvertTo-Json -Depth 10)
Assert-True ($inspection.id -gt 0 -and @($inspection.evidence).Count -eq 1) "inspection #$($inspection.id) persists criteria and evidence"
Assert-True ($inspection.overallDecision -eq 'PartiallyAccepted') 'inspection result is PartiallyAccepted'

$ncrs = Invoke-RestMethod -Uri "$ApiBase/quality-inspections/non-conformances" -Headers (Make-Headers $inspector)
$matches = $ncrs | Where-Object { [int]$_.inspectionItemId -eq [int]$inspection.items[0].id }
Assert-True (@($matches).Count -eq 1) 'one NCR is generated for the rejected inspection line'
$ncr = $matches | Select-Object -First 1
Assert-True ([int]$ncr.deliveryId -eq [int]$delivery.id -and [int]$ncr.materialId -eq [int]$item.materialId -and [decimal]$ncr.quantityAffected -eq $rejected) 'NCR links delivery, material and affected quantity'
Assert-True ([int]$ncr.supplierId -eq [int]$delivery.purchaseOrder.supplierId) 'NCR links the purchase-order supplier'

$events = Invoke-RestMethod -Uri "$ApiBase/notifications" -Headers (Make-Headers $engineer)
$eventMatches = $events | Where-Object { [int]$_.inspectionId -eq [int]$inspection.id }
Assert-True (@($eventMatches).Count -eq 1) 'initiating Site Engineer receives a durable inspection notification'
$event = $eventMatches | Select-Object -First 1
Assert-True ([int]$event.nonConformanceId -eq [int]$ncr.id) 'notification links the generated NCR'

$review = @{ status = 'UnderReview'; reviewNotes = 'Supplier review started'; resolution = $null } | ConvertTo-Json
$underReview = Invoke-RestMethod -Method Post -Uri "$ApiBase/quality-inspections/non-conformances/$($ncr.id)/transition" -Headers (Make-Headers $manager $true) -Body $review
Assert-True ($underReview.status -eq 'UnderReview') 'manager can move NCR to UnderReview'
$action = @{ status = 'CorrectiveActionRequired'; reviewNotes = 'Replacement requested'; resolution = 'Supplier replacement arranged' } | ConvertTo-Json
$resolvedAction = Invoke-RestMethod -Method Post -Uri "$ApiBase/quality-inspections/non-conformances/$($ncr.id)/transition" -Headers (Make-Headers $manager $true) -Body $action
Assert-True ($resolvedAction.status -eq 'CorrectiveActionRequired' -and $resolvedAction.resolution -eq 'Supplier replacement arranged') 'manager records corrective action and resolution'
Write-Host "Component 4 verification passed: inspection #$($inspection.id), NCR #$($ncr.ncrNumber)." -ForegroundColor Green
