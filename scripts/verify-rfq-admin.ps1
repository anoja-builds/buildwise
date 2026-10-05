[CmdletBinding()]
param(
    [string]$ApiBase = 'http://127.0.0.1:5078/api',
    [string]$Password = 'Passw0rd!'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Login([string]$Email) {
    $body = @{ email = $Email; password = $Password } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}
function Headers([string]$Token) { return @{ Authorization = "Bearer $Token" } }
function JsonHeaders([string]$Token) { return @{ Authorization = "Bearer $Token"; 'Content-Type' = 'application/json' } }
function Check([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host "[$(if ($Passed) { 'PASS' } else { 'FAIL' })] $Name - $Detail"
    if (-not $Passed) { throw "$Name failed: $Detail" }
}

$officer = Login 'procurement.officer@buildwise.demo'
$admin = Login 'admin@buildwise.demo'
$officerHeaders = Headers $officer
$adminHeaders = Headers $admin
$requests = Invoke-RestMethod -Uri "$ApiBase/material-requests?status=Approved" -Headers $officerHeaders
Check 'Approved request exists' ($requests.Count -gt 0) "count=$($requests.Count)"
$request = $requests | Select-Object -First 1
if ($null -eq $request) { throw 'No approved material request returned by the API.' }
$requestId = [int]($request.id)
$supplierPage = Invoke-RestMethod -Uri "$ApiBase/suppliers?page=1&pageSize=100&status=Active" -Headers $officerHeaders
$supplier = @($supplierPage.items) | Select-Object -First 1
if ($null -eq $supplier) { throw 'No active supplier returned by the API.' }
$supplierId = [int]($supplier.id)
$rfqBody = @{ materialRequestId = $requestId; requiredResponseDate = (Get-Date).AddDays(7).ToString('yyyy-MM-dd'); notes = 'Automated RFQ verification'; supplierIds = @($supplierId) } | ConvertTo-Json -Depth 5
$rfq = Invoke-RestMethod -Method Post -Uri "$ApiBase/rfqs" -Headers (JsonHeaders $officer) -Body $rfqBody
Check 'RFQ create' ($rfq.status -eq 'Issued' -and @($rfq.suppliers).Count -eq 1) "id=$($rfq.id)"
$closed = Invoke-RestMethod -Method Post -Uri "$ApiBase/rfqs/$($rfq.id)/close" -Headers (JsonHeaders $officer) -Body (@{ reason = 'Automated verification close' } | ConvertTo-Json)
Check 'RFQ close' ($closed.status -eq 'Closed') "id=$($rfq.id)"
$users = Invoke-RestMethod -Uri "$ApiBase/admin/users" -Headers $adminHeaders
Check 'Admin user directory' (@($users).Count -ge 7) "users=$(@($users).Count)"
$health = Invoke-RestMethod -Uri "$ApiBase/admin/health" -Headers $adminHeaders
Check 'Admin health' ($health.database -and -not ($health.services.Values -contains $false)) "database=$($health.database), services=$($health.services.Count)"
$deniedStatus = 0
try { Invoke-WebRequest -Uri "$ApiBase/admin/users" -Headers $officerHeaders -UseBasicParsing -ErrorAction Stop | Out-Null; $deniedStatus = 200 } catch { $deniedStatus = [int]$_.Exception.Response.StatusCode }
Check 'RBAC admin boundary' ($deniedStatus -eq 403) "officer HTTP $deniedStatus"
$audit = Invoke-RestMethod -Uri "$ApiBase/admin/audit-logs?pageSize=50" -Headers $adminHeaders
Check 'Audit log records RFQ mutation' (@($audit | Where-Object { $_.requestPath -match 'rfqs' }).Count -gt 0) "auditRows=$(@($audit).Count)"
Write-Host 'RFQ/Admin verification passed.' -ForegroundColor Green
exit 0
