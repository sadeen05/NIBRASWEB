$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$base = 'http://localhost:5181'
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
$sqlServer = 'SADEEN\SQLEXPRESS'
$dbName = 'NEBRASDB_Step1'
$rows = New-Object System.Collections.ArrayList

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $req.Headers['Authorization'] = 'Bearer ' + $Token }
  if ($null -ne $Body) { $req.Body = (ConvertTo-Json -InputObject $Body -Depth 5 -Compress); $req.ContentType = 'application/json' }
  try {
    $r = Invoke-WebRequest @req
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content }
  } catch {
    $resp = $_.Exception.Response
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $text }
  }
}

function Check([string]$Id, [string]$Case, $Expected, $Actual, [string]$Note = '') {
  $exp = [string]$Expected; $act = [string]$Actual
  $res = if ($exp -eq $act) { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Case = $Case; Expected = $exp; Actual = $act; Result = $res })
  Write-Output ("{0,-6} {1,-4} exp={2,-8} act={3,-8} {4} {5}" -f $Id, $res, $exp, $act, $Case, $Note)
}

function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $dbName -W -h -1 -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ }) -join "`n").Trim()
}

function LandBody([string]$LandNumber, [string]$Parcel) {
  return @{ regionId = 2; gridId = 2; landNumber = $LandNumber; village = 'Village-X'; basin = 'Basin-X'
    parcelNumber = $Parcel; areaDonum = 25; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100 }
}

$email = "s4x-$stamp-la@example.test"
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4X Landlord'; email = $email; phone = (New-Phone); password = $pw; roleId = 1 }
$r = Call 'POST' '/api/auth/login' @{ email = $email; password = $pw }
$tok = ($r.Body | ConvertFrom-Json).token
Write-Output 'landlord created and logged in (token in memory only)'

Write-Output ''
Write-Output '== SEC-13 / SEC-17: control characters in filePath =='
$r = Call 'POST' '/api/lands' (LandBody "CX-$stamp" "PX-$stamp") $tok
$land = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$land/documents" @{ documentTypeId = 2; filePath = ('docs/a' + [char]1 + '.pdf') } $tok
Check 'X-01' 'filePath with U+0001' 400 $r.Status
$r = Call 'POST' "/api/lands/$land/documents" @{ documentTypeId = 2; filePath = ("docs/a" + [char]9 + "b.pdf") } $tok
Check 'X-02' 'filePath with tab (SEC-13 lists control characters)' 400 $r.Status

Write-Output ''
Write-Output '== SEC-17: tab and newline in free text (spec: allowed in free text) =='
$r = Call 'POST' "/api/lands/$land/reject" @{ reason = ("line one" + [char]10 + "line two") } $tok
Check 'X-03' 'reject reason with newline reaches the role check (landlord -> 403)' 403 $r.Status ("body=" + ($r.Body.Substring(0, [Math]::Min(90, $r.Body.Length))))
$r = Call 'POST' "/api/lands/$land/reject" @{ reason = ("tab" + [char]9 + "here") } $tok
Check 'X-04' 'reject reason with tab reaches the role check (landlord -> 403)' 403 $r.Status
$r = Call 'POST' "/api/lands/$land/reject" @{ reason = ('bad' + [char]1 + 'reason') } $tok
Check 'X-05' 'reject reason with U+0001 is rejected' 400 $r.Status

Write-Output ''
Write-Output '== SEC-07: extra protected fields on PUT are ignored (DB checked) =='
$before = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(5)) + '|' + CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + ISNULL(CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(5)),'NULL') FROM LANDS WHERE ID = $land;"
$upd = LandBody "CX-$stamp" "PX-$stamp"
$upd.status = 'Verified'; $upd.dataVerifiedByAdmin = $true; $upd.verifiedAgainstCriterionId = 999; $upd.landlordId = 999999
$r = Call 'PUT' "/api/lands/$land" $upd $tok
Check 'X-06' 'PUT with status/dataVerifiedByAdmin/verifiedAgainstCriterionId/landlordId' 204 $r.Status
$after = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(5)) + '|' + CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + ISNULL(CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(5)),'NULL') FROM LANDS WHERE ID = $land;"
Check 'X-07' 'DB: protected fields unchanged by PUT' $before $after

Write-Output ''
Write-Output '== SEC-18: body of a real generic 500 (parcel race, 4 losers) =='
$bodies = @(); for ($i = 1; $i -le 5; $i++) { $bodies += ,(LandBody "RX$i-$stamp" "PXR-$stamp") }
$client = New-Object System.Net.Http.HttpClient
$tasks = New-Object System.Collections.ArrayList
foreach ($b in $bodies) {
  $json = ConvertTo-Json -InputObject $b -Depth 5 -Compress
  $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + '/api/lands')
  $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $tok
  $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
  [void]$tasks.Add($client.SendAsync($req))
}
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
$bodiesOut = @(); foreach ($t in $tasks) { $bodiesOut += [pscustomobject]@{ S = [int]$t.Result.StatusCode; B = $t.Result.Content.ReadAsStringAsync().Result } }
$client.Dispose()
$fivehundred = @($bodiesOut | Where-Object { $_.S -eq 500 })
$conflicts = @($bodiesOut | Where-Object { $_.S -eq 409 })
Write-Output ("500 responses in this burst: " + $fivehundred.Count + "; 409 responses: " + $conflicts.Count)
Check 'X-08' 'after the UQ_LAND_LOCATION fix the parcel race yields no 500' 0 $fivehundred.Count
Check 'X-09' 'after the fix the four parcel-race losers are 409' 4 $conflicts.Count
Write-Output 'NOTE X-10: a generic 500 body (generic detail + traceId, no SQL text) can no longer be triggered by the parcel race; the earlier observation is retained as code review only (see report).'

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "REVIEW2 TOTAL PASS=$pass FAIL=$fail"
