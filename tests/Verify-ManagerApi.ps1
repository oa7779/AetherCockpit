param(
    [Parameter(Mandatory=$true)][string]$DataDirectory,
    [string]$BaseUrl = 'http://127.0.0.1:5197'
)
$ErrorActionPreference = 'Stop'
$uri = [Uri]$BaseUrl
if (!$uri.IsLoopback -or $uri.Scheme -notin @('http','https')) { throw 'Dieser Test darf nur auf einer isolierten Loopback-API laufen.' }
if ([IO.Path]::GetFileName($DataDirectory.TrimEnd('\')) -ne 'manager-validation') { throw 'Isolierten manager-validation-Datenordner verwenden.' }
$admin = (Get-Content -LiteralPath (Join-Path $DataDirectory 'admin-token.txt') -Raw).Trim()
$script:assertions = 0
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message" }
    $script:assertions++
}
function Request([string]$Path, [string]$Method='GET', [object]$Body=$null, [string]$Token=$admin) {
    $parameters = @{ Uri="$BaseUrl$Path"; Method=$Method; UseBasicParsing=$true; ErrorAction='Stop' }
    if ($Token) { $parameters.Headers = @{Authorization="Bearer $Token"} }
    if ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $parameters.ContentType='application/json' }
    Invoke-WebRequest @parameters
}
function Json([object]$Response) { $Response.Content | ConvertFrom-Json }
function Rejected([string]$Path,[string]$Method,[object]$Body,[string]$Token,[int]$Expected) {
    try { $null = Request $Path $Method $Body $Token }
    catch { Check ([int]$_.Exception.Response.StatusCode -eq $Expected) "Expected HTTP $Expected"; return }
    throw "Request was unexpectedly accepted: $Path"
}
$page = Request '/' 'GET' $null ''
Check ($page.StatusCode -eq 200 -and $page.Content.Contains('Zentralverwaltung')) 'Web administration served'
Check (($page.Headers['Content-Security-Policy'] -join ';').Contains("frame-ancestors 'none'")) 'Web CSP present'
Check ((Request '/management.js' 'GET' $null '').StatusCode -eq 200) 'UI script served'
Check ((Request '/openapi/v1.json' 'GET' $null '').Content.Contains('/api/v1/management/claim')) 'Management API documented'
Rejected '/api/v1/admin/devices' 'GET' $null '' 401
$code = Json (Request '/api/v1/admin/enrollment-codes' 'POST')
$device = Json (Request '/api/v1/enroll' 'POST' @{code=$code.code;machineName='API-SYNTHETIC';cockpitVersion='test'} '')
Check ($device.state -eq 'pending') 'New device pending'
Rejected '/api/v1/management/heartbeat' 'POST' @{clientVersion='test';allowChanges=$false;allowAutoRepair=$false} $device.deviceToken 403
$null = Request "/api/v1/admin/devices/$($device.deviceId)/approve" 'POST'
$heartbeat = Json (Request '/api/v1/management/heartbeat' 'POST' @{clientVersion='test';allowChanges=$false;allowAutoRepair=$false} $device.deviceToken)
Check (!$heartbeat.policy.allowChanges) 'Remote changes initially off'
$actions = Json (Request '/api/v1/admin/management/actions')
Check (@($actions | Where-Object canAutoRepair).Count -eq 1) 'Only narrow DNS auto repair exposed'
Check (@($actions | Where-Object id -eq 'windows-update-install').Count -eq 1) 'Typed update action exposed'
Rejected "/api/v1/admin/management/devices/$($device.deviceId)/jobs" 'POST' @{actionId='dns-cache-clear';confirmChanges=$true} $admin 409
$jobResponse = Request "/api/v1/admin/management/devices/$($device.deviceId)/jobs" 'POST' @{actionId='inventory'}
$job = Json $jobResponse
Check ($jobResponse.StatusCode -eq 201 -and $jobResponse.Headers.Location) 'Job created with resource location'
$claim = Json (Request '/api/v1/management/claim' 'POST' $null $device.deviceToken)
Check ($claim.job.id -eq $job.id) 'Synthetic job claimed'
Check ((Request '/api/v1/management/claim' 'POST' $null $device.deviceToken).StatusCode -eq 204) 'Claim not redispatched'
Rejected "/api/v1/management/jobs/$($job.id)/complete" 'POST' @{claimToken=('x'*64);exitCode=0;output='fake';truncated=$false} $device.deviceToken 409
$result = Json (Request "/api/v1/management/jobs/$($job.id)/complete" 'POST' @{claimToken=$claim.claimToken;exitCode=0;output='synthetic result, no action executed';truncated=$false} $device.deviceToken)
Check ($result.state -eq 'completed') 'Result persisted'
$null = Request "/api/v1/admin/management/devices/$($device.deviceId)/policy" 'PUT' @{allowChanges=$true;allowAutoRepair=$false}
$null = Request '/api/v1/management/heartbeat' 'POST' @{clientVersion='test';allowChanges=$true;allowAutoRepair=$false} $device.deviceToken
Rejected "/api/v1/admin/management/devices/$($device.deviceId)/jobs" 'POST' @{actionId='windows-update-install';confirmChanges=$true;update=@{updateId=[Guid]::NewGuid().ToString();revision=1;eulaHash=('A'*64);acceptEula=$true}} $admin 409
$report = Request '/api/v1/reports' 'POST' @{capturedAt=[DateTimeOffset]::UtcNow.ToString('o');report=@{machineName='synthetic';findings=@()}} $device.deviceToken
Check ($report.StatusCode -eq 201) 'Synthetic report submitted'
$listed = Json (Request "/api/v1/admin/reports?deviceId=$($device.deviceId)&limit=1")
Check ($listed.totalCount -eq 1) 'Report listed'
$null = Request "/api/v1/admin/devices/$($device.deviceId)/revoke" 'POST'
Rejected '/api/v1/management/claim' 'POST' $null $device.deviceToken 403
Check (@(Json (Request '/api/v1/admin/management/audit')).Count -ge 5) 'Audit transitions recorded'
Write-Output "PASS $script:assertions HTTP/API assertions; only synthetic data, no device action executed."
