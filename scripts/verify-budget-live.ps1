# Live end-to-end proof of Step 5 (budget validation) against the running stack.
# Quoting 500 bags @ 2,250 = 1,125,000 against a 1,100,000 project budget.
$api = 'http://localhost:5078/api'

function Get-Token([string]$email) {
    $body = @{ email = $email; password = 'Passw0rd!' } | ConvertTo-Json
    (Invoke-RestMethod -Uri "$api/auth/login" -Method Post -ContentType 'application/json' -Body $body).token
}
function New-Auth([string]$email) { @{ Authorization = "Bearer $(Get-Token $email)" } }

$mgr = New-Auth 'procurement.manager@buildwise.demo'
$off = New-Auth 'procurement.officer@buildwise.demo'
$se  = New-Auth 'site.engineer@buildwise.demo'

# Budget: 1,100,000
Invoke-RestMethod -Uri "$api/projects/1/budget" -Method Put -Headers $mgr `
    -ContentType 'application/json' -Body '{"materialBudgetAmount":1100000}' | Out-Null
Write-Output 'Budget set to 1,100,000. Quoting 500 x 2,250 = 1,125,000 (25,000 OVER).'
Write-Output ''

# 1. Site Engineer raises the request
$reqBody = @{
    projectId    = 1
    requiredDate = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    reason       = 'Budget overrun demo'
    items        = @(@{ materialId = 1; requestedQuantity = 500; unit = 'bag'; notes = 'Grade 42.5N' })
} | ConvertTo-Json -Depth 5
$req = Invoke-RestMethod -Uri "$api/material-requests" -Method Post -Headers $se `
    -ContentType 'application/json' -Body $reqBody
Write-Output "1. Site Engineer created request #$($req.id)"

# 2. Manager approves
$apprBody = @{ decision = 'Approved'; comments = 'budget demo' } | ConvertTo-Json
Invoke-RestMethod -Uri "$api/material-requests/$($req.id)/approval" -Method Post -Headers $mgr `
    -ContentType 'application/json' -Body $apprBody | Out-Null
Write-Output '2. Manager approved'

# 3. Officer records the over-budget quotation
$lineId = (Invoke-RestMethod -Uri "$api/material-requests/$($req.id)" -Headers $mgr).items[0].id
$qBody = @{
    supplierId           = 1
    quotationDate        = (Get-Date).ToString('yyyy-MM-dd')
    validUntil           = (Get-Date).AddDays(30).ToString('yyyy-MM-dd')
    promisedDeliveryDate = (Get-Date).AddDays(5).ToString('yyyy-MM-dd')
    paymentTerms         = 'Net 30'
    transportCharge      = 0
    items                = @(@{ materialRequestItemId = $lineId; quantity = 500; unitPrice = 2250 })
} | ConvertTo-Json -Depth 5
Invoke-RestMethod -Uri "$api/material-requests/$($req.id)/quotations" -Method Post -Headers $off `
    -ContentType 'application/json' -Body $qBody | Out-Null
Write-Output '3. Quotation recorded: 500 x 2250 = 1,125,000'

# 4. Officer starts the AI workflow
$wfBody = @{ materialRequestId = $req.id; initiatedByUserId = 1 } | ConvertTo-Json
$wf = Invoke-RestMethod -Uri "$api/material-requests/$($req.id)/procurement-workflow" -Method Post `
    -Headers $off -ContentType 'application/json' -Body $wfBody
Write-Output "4. AI workflow #$($wf.workflowId) started"
Write-Output ''

$wfd = Invoke-RestMethod -Uri "$api/procurement-workflow/$($wf.workflowId)" -Headers $mgr
Write-Output "=== DETERMINISTIC VALIDATION WARNINGS ==="
$wfd.validation.warnings | ForEach-Object { "  - $_" }
Write-Output '=== AI RISK FLAGS ==='
$wfd.recommendation.riskFlags | ForEach-Object { "  - $_" }
Write-Output '=== AI WARNINGS ==='
$wfd.recommendation.warnings | ForEach-Object { "  - $_" }
Write-Output ''
Write-Output "Manager can still decide? IsValid=$($wfd.validation.isValid)  Status=$($wfd.status)"