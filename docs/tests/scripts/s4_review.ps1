$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
Add-Type -AssemblyName System.Net.Http
# The suite owns its instance: the harness starts it on the dedicated port, with the
# explicit disposable connection string, and proves that it owns the port.
$harness = $null
try {
$harness = Start-TestApiInstance
$base = Initialize-TestApi
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
. (Join-Path $PSScriptRoot 'test_target.ps1')
# The target is resolved from the effective connection string and is fail-closed:
# only the disposable database is accepted, and a mismatch stops the run.
$sqlServer = (Get-TestTarget).Server
$dbName = (Get-TestTarget).Database
# The running instance is proved to write into the disposable database before anything is written.
Assert-TestInstanceIdentity | Out-Null
$rows = New-Object System.Collections.ArrayList

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $req.Headers['Authorization'] = 'Bearer ' + $Token }
  if ($null -ne $Body) { $req.Body = (ConvertTo-Json -InputObject $Body -Depth 5 -Compress); $req.ContentType = 'application/json' }
  try {
    $r = Invoke-WebRequest @req
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content; Csp = [string]$r.Headers['Content-Security-Policy']; Nosniff = [string]$r.Headers['X-Content-Type-Options']; Frame = [string]$r.Headers['X-Frame-Options']; Referrer = [string]$r.Headers['Referrer-Policy'] }
  } catch {
    $resp = $_.Exception.Response
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $text; Csp = [string]$resp.Headers['Content-Security-Policy']; Nosniff = [string]$resp.Headers['X-Content-Type-Options']; Frame = [string]$resp.Headers['X-Frame-Options']; Referrer = [string]$resp.Headers['Referrer-Policy'] }
  }
}

function Check([string]$Id, [string]$Case, $Expected, $Actual, [string]$Note = '') {
  $exp = [string]$Expected; $act = [string]$Actual
  $res = if ($exp -eq $act) { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Case = $Case; Expected = $exp; Actual = $act; Result = $res })
  Write-Output ("{0,-7} {1,-4} exp={2,-40} act={3,-40} {4} {5}" -f $Id, $res, $exp, $act, $Case, $Note)
}

function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $dbName -W -h -1 -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ }) -join "`n").Trim()
}

function LandBody([string]$LandNumber, [string]$Parcel) {
  return @{ governorateId = $fx.GovernorateId; landNumber = $LandNumber; village = 'Village-V'; basin = 'Basin-V'
    parcelNumber = $Parcel; areaDonum = 25 }
}

function Send-Parallel([string]$Method, [string]$Path, $Bodies, [string]$Token) {
  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  $tasks = New-Object System.Collections.ArrayList
  foreach ($b in $Bodies) {
    $json = ConvertTo-Json -InputObject $b -Depth 5 -Compress
    $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + $Path)
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $Token
    $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
    [void]$tasks.Add($client.SendAsync($req))
  }
  [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
  $out = @()
  foreach ($t in $tasks) { $resp = $t.Result; $out += [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $resp.Content.ReadAsStringAsync().Result } }
  $client.Dispose()
  return $out
}

Write-Output '== setup (no secrets printed) =='
$email = "s4v-$stamp-la@example.test"
$lbEmail = "s4v-$stamp-lb@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4V Landlord A'; email = $email; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $email -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $email failed ($($v.Status))" }
$laId = [int]($r.Body | ConvertFrom-Json).id
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4V Landlord B'; email = $lbEmail; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $lbEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $lbEmail failed ($($v.Status))" }
$r = Call 'POST' '/api/auth/login' @{ email = $email; password = $pw }
$aTok = ($r.Body | ConvertFrom-Json).token
$r = Call 'POST' '/api/auth/login' @{ email = $lbEmail; password = $pw }
$bTok = ($r.Body | ConvertFrom-Json).token
Write-Output 'two landlords created and logged in (tokens in memory only)'

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $dbName
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

Write-Output ''
Write-Output '== SEC-15 exact headers on error responses =='
$csp = "default-src 'none'; frame-ancestors 'none'"
$r = Call 'GET' '/api/lands/999999' $null $aTok
Check 'V-01' 'CSP on 404 equals SEC-15 value' $csp $r.Csp
Check 'V-02' 'nosniff / DENY / no-referrer on 404' 'nosniff|DENY|no-referrer' ("{0}|{1}|{2}" -f $r.Nosniff, $r.Frame, $r.Referrer)
$r = Call 'POST' '/api/lands' (LandBody "HDR-$stamp" "PH-$stamp") $bTok
Check 'V-03' 'CSP on a 201 is set (land created by B)' $csp $r.Csp
$r = Call 'GET' '/api/lands' $null
Check 'V-04' 'CSP on 401 equals SEC-15 value' $csp $r.Csp

Write-Output ''
Write-Output '== SEC-09 ownership convention: missing vs other-owner =='
$r1 = Call 'GET' '/api/lands/999999' $null $bTok
$r2 = Call 'POST' '/api/lands' (LandBody "OWN-$stamp" "PO-$stamp") $aTok
$ownId = [int]($r2.Body | ConvertFrom-Json).id
$r3 = Call 'GET' "/api/lands/$ownId" $null $bTok
Check 'V-05' 'landlord B: missing land' 404 $r1.Status
Check 'V-06' 'landlord B: A''s existing land' 403 $r3.Status
Write-Output 'NOTE V-05/V-06: 404 for missing and 403 for existing reveal existence to other users (see report).'

Write-Output ''
Write-Output '== race: 5 parallel creates with the SAME parcel (UQ_LAND_LOCATION, filtered) =='
$bodies = @(); for ($i = 1; $i -le 5; $i++) { $bodies += ,(LandBody "RP$i-$stamp" "PRACE-$stamp") }
$res = Send-Parallel 'POST' '/api/lands' $bodies $aTok
$codes = @($res | ForEach-Object { $_.Status })
Write-Output ("parallel parcel codes: " + ($codes -join ','))
Check 'V-07' 'parcel race: exactly one 201' 1 @($codes | Where-Object { $_ -eq 201 }).Count
Check 'V-08' 'parcel race: the other four are 409' 4 @($codes | Where-Object { $_ -eq 409 }).Count
Check 'V-09' 'parcel race: no 500 responses' 0 @($codes | Where-Object { $_ -ge 500 }).Count
$leakBodies = @($res | Where-Object { $_.Body -match 'UQ_|SqlException|Cannot insert' }).Count
Check 'V-10' 'parcel race: no SQL/index text in any response body' 0 $leakBodies
# Landlord-created lands have no region before the admin assessment; the unique scope is the governorate.
$dbParcel = Sql "SELECT COUNT(*) FROM LANDS WHERE GOVERNORATE_ID = $($fx.GovernorateId) AND VILLAGE = N'Village-V' AND BASIN = N'Basin-V' AND PARCEL_NUMBER = N'PRACE-$stamp' AND IS_DELETED = 0;"
Check 'V-11' 'DB: exactly one live row for the raced parcel' 1 $dbParcel

Write-Output ''
Write-Output '== race: 5 parallel document uploads, same land and type (version uniqueness) =='
$landBody = LandBody "DOC-$stamp" "PD-$stamp"
$r = Call 'POST' '/api/lands' $landBody $aTok
$docLand = [int]($r.Body | ConvertFrom-Json).id
$dbodies = @(); for ($i = 1; $i -le 5; $i++) { $dbodies += ,@{ documentTypeId = 2; filePath = "docs/race-$i.pdf" } }
$res = Send-Parallel 'POST' "/api/lands/$docLand/documents/metadata" $dbodies $aTok
$dcodes = @($res | ForEach-Object { $_.Status })
Write-Output ("parallel upload codes: " + ($dcodes -join ','))
Check 'V-12' 'doc race: all five uploads 201' 5 @($dcodes | Where-Object { $_ -eq 201 }).Count
$total = Sql "SELECT COUNT(*) FROM LAND_DOCUMENTS WHERE LAND_ID = $docLand AND DOCUMENT_TYPE_ID = 2;"
$distinct = Sql "SELECT COUNT(DISTINCT VERSION) FROM LAND_DOCUMENTS WHERE LAND_ID = $docLand AND DOCUMENT_TYPE_ID = 2;"
Check 'V-13' 'doc race: five rows persisted' 5 $total
Check 'V-14' 'doc race: five distinct version numbers' 5 $distinct ("versions=" + (Sql "SELECT STRING_AGG(CAST(VERSION AS varchar(5)), ',') FROM LAND_DOCUMENTS WHERE LAND_ID = $docLand AND DOCUMENT_TYPE_ID = 2;"))

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "REVIEW TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
}
finally {
  Complete-TestApiCleanup -Identity $harness
}
