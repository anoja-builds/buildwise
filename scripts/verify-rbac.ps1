param(
    [string]$ApiBase = 'http://127.0.0.1:5078/api'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$password = 'Passw0rd!'
$results = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $results.Add([pscustomobject]@{ Check = $Name; Status = $(if ($Passed) { 'PASS' } else { 'FAIL' }); Detail = $Detail })
    Write-Host "[$(if ($Passed) { 'PASS' } else { 'FAIL' })] $Name - $Detail"
}

function Login([string]$Email) {
    $body = @{ email = $Email; password = $password } | ConvertTo-Json -Compress
    return (Invoke-RestMethod -Method Post -Uri "$ApiBase/auth/login" -ContentType 'application/json' -Body $body).token
}

function Request-Status([string]$Method, [string]$Path, [string]$Token = '', [object]$Body = $null) {
    $headers = @{}
    if ($Token) { $headers.Authorization = "Bearer $Token" }
    $params = @{
        Method = $Method
        Uri = "$ApiBase$Path"
        Headers = $headers
        UseBasicParsing = $true
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json'
        $params.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }
    try {
        return (Invoke-WebRequest @params).StatusCode
    } catch {
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $global:LASTEXITCODE = 0
            return $status
        }
        throw
    }
}

$accounts = @{
    SiteEngineer = 'site.engineer@buildwise.demo'
    SiteOfficer = 'site.officer@buildwise.demo'
    SiteManager = 'site.manager@buildwise.demo'
    ProcurementOfficer = 'procurement.officer@buildwise.demo'
    ProcurementManager = 'procurement.manager@buildwise.demo'
    QualityInspector = 'quality.inspector@buildwise.demo'
    Administrator = 'admin@buildwise.demo'
}
$tokens = @{}
foreach ($entry in $accounts.GetEnumerator()) {
    try {
        $tokens[$entry.Key] = Login $entry.Value
        Add-Check "$($entry.Key) login" $true $entry.Value
    } catch {
        Add-Check "$($entry.Key) login" $false $_.Exception.Message
    }
}

$status = Request-Status Get '/material-requests'
Add-Check 'Anonymous request is rejected' ($status -eq 401) "HTTP $status"

$status = Request-Status Get '/material-requests' $tokens.QualityInspector
Add-Check 'Quality Inspector cannot read C1' ($status -eq 403) "HTTP $status"

$status = Request-Status Get '/material-requests/my' $tokens.SiteEngineer
Add-Check 'Site Engineer reads own requests' ($status -eq 200) "HTTP $status"

$invalidRequest = @{
    projectId = 1
    requiredDate = (Get-Date).AddDays(1).ToString('yyyy-MM-dd')
    reason = 'RBAC validation'
    items = @(@{ materialId = 1; requestedQuantity = 10 })
}
$status = Request-Status Post '/material-requests' $tokens.SiteEngineer $invalidRequest
Add-Check 'C1 three-day lead time is enforced' ($status -eq 400) "HTTP $status"

$status = Request-Status Post '/material-requests' $tokens.ProcurementOfficer $invalidRequest
Add-Check 'Procurement Officer cannot create C1' ($status -eq 403) "HTTP $status"

$supplierStatus = @{ status = 'Active' }
$status = Request-Status Patch '/suppliers/999999/status' $tokens.ProcurementOfficer $supplierStatus
Add-Check 'Procurement Officer passes supplier mutation guard' ($status -eq 404) "HTTP $status (expected 404 for missing supplier)"
$status = Request-Status Patch '/suppliers/999999/status' $tokens.ProcurementManager $supplierStatus
Add-Check 'Procurement Manager has read-only supplier access' ($status -eq 403) "HTTP $status"

$missingDelivery = @{
    purchaseOrderId = 999999
    deliveryReference = 'RBAC-NONEXISTENT'
    items = @(@{ materialId = 1; receivedQuantity = 1; damagedQuantity = 0 })
}
$status = Request-Status Post '/deliveries' $tokens.SiteOfficer $missingDelivery
Add-Check 'Site Officer passes C3 write guard' ($status -eq 400) "HTTP $status (confirmed-PO rule)"
$status = Request-Status Post '/deliveries' $tokens.QualityInspector $missingDelivery
Add-Check 'Quality Inspector cannot record C3' ($status -eq 403) "HTTP $status"

$status = Request-Status Post '/agent/analyze-request/1' $tokens.ProcurementManager
Add-Check 'Manager triggers request agent bridge' ($status -eq 200) "HTTP $status"
$status = Request-Status Post '/agent/analyze-request/1' $tokens.ProcurementOfficer
Add-Check 'Officer cannot trigger manager request analysis' ($status -eq 403) "HTTP $status"

$status = Request-Status Get '/agent-workflows?page=1&pageSize=5' $tokens.ProcurementManager
Add-Check 'Procurement Manager reads agent execution history' ($status -eq 200) "HTTP $status"
$status = Request-Status Get '/agent-workflows?page=1&pageSize=5' $tokens.QualityInspector
Add-Check 'Quality Inspector cannot read agent execution history' ($status -eq 403) "HTTP $status"

$status = Request-Status Get '/quality-inspections/non-conformances' $tokens.QualityInspector
Add-Check 'Quality Inspector reads NCRs' ($status -eq 200) "HTTP $status"
$status = Request-Status Get '/quality-inspections/non-conformances' $tokens.ProcurementOfficer
Add-Check 'Procurement Officer cannot read NCRs' ($status -eq 403) "HTTP $status"
$status = Request-Status Put '/quality-inspections/non-conformances/999999/status' $tokens.QualityInspector @{ newStatus = 'Resolved' }
Add-Check 'Quality Inspector cannot manage NCR status' ($status -eq 403) "HTTP $status"

$privilegedRegistration = @{
    fullName = 'Unauthorized Manager'
    email = "unauthorized.manager.$([DateTime]::UtcNow.Ticks)@buildwise.test"
    password = 'SuperSecret1'
    roleName = 'ProcurementManager'
}
$status = Request-Status Post '/auth/register' '' $privilegedRegistration
Add-Check 'Privileged self-registration is blocked' ($status -eq 400) "HTTP $status"

$failed = @($results | Where-Object Status -eq 'FAIL')
Write-Host "`nRBAC summary: $($results.Count - $failed.Count)/$($results.Count) passed." -ForegroundColor $(if ($failed.Count -eq 0) { 'Green' } else { 'Yellow' })
if ($failed.Count -gt 0) { exit 1 }
exit 0
