$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
Add-Type -AssemblyName System.Net.Http
# The suite owns its instance: the harness starts it on the dedicated port, with the
# explicit disposable connection string, and proves that it owns the port.
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
  if ($null -ne $Body) {
    $req.Body = (ConvertTo-Json -InputObject $Body -Depth 5 -Compress)
    $req.ContentType = 'application/json'
  }
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
  $exp = [string]$Expected
  $act = [string]$Actual
  $res = if ($exp -eq $act) { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Case = $Case; Expected = $exp; Actual = $act; Result = $res })
  Write-Output ("{0,-7} {1,-4} exp={2,-6} act={3,-6} {4} {5}" -f $Id, $res, $exp, $act, $Case, $Note)
}

function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $dbName -W -h -1 -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ }) -join "`n").Trim()
}

# Phase 1: only the governorate, the location identifiers and the area are landlord-supplied.
# The region, the grid and the four technical measurements are admin-only and are recorded by
# PUT /api/lands/{id}/assessment, never in the create body.
function LandBody([string]$LandNumber, [string]$Parcel) {
  return @{ governorateId = $fx.GovernorateId; landNumber = $LandNumber; village = 'Village-R'; basin = 'Basin-R'
    parcelNumber = $Parcel; areaDonum = 25 }
}

Write-Output '== setup =='
$email = "s4r-$stamp-la@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4R Landlord'; email = $email; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $email -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $email failed ($($v.Status))" }
$laId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' '/api/auth/login' @{ email = $email; password = $pw }
$tok = ($r.Body | ConvertFrom-Json).token
Write-Output "landlord id=$laId, login 200 (token held in memory only)"

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $dbName
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

Write-Output ''
Write-Output '== duplicate LandNumber: create and update (BR-L5 / UQ_LAND_NUMBER_REGION) =='
$rn = "RN-$stamp"
$r = Call 'POST' '/api/lands' (LandBody $rn "PA-$stamp") $tok
Check 'R-01' 'create land with LandNumber RN (first)' 201 $r.Status
$l1 = [int]($r.Body | ConvertFrom-Json).id

$r = Call 'POST' '/api/lands' (LandBody $rn "PB-$stamp") $tok
Check 'R-02' 'create second land, same LandNumber+Region, different parcel' 409 $r.Status
$leak = [bool]($r.Body -match 'UQ_|SqlException|Cannot insert|duplicate key|Microsoft\.')
Check 'R-03' 'conflict body exposes no SQL or index detail' 'False' ([string]$leak)

$rn3 = "RN3-$stamp"
$r = Call 'POST' '/api/lands' (LandBody $rn3 "PC-$stamp") $tok
Check 'R-04' 'create land with LandNumber RN3' 201 $r.Status
$l3 = [int]($r.Body | ConvertFrom-Json).id

$r = Call 'PUT' "/api/lands/$l3" (LandBody $rn "PC-$stamp") $tok
Check 'R-05' 'update land 3 to LandNumber RN (taken by land 1)' 409 $r.Status
$r = Call 'PUT' "/api/lands/$l3" (LandBody $rn3 "PC-$stamp") $tok
Check 'R-06' 'update land 3 keeping its own LandNumber (self excluded)' 204 $r.Status

Write-Output ''
Write-Output '== race: 5 parallel creates with the same LandNumber =='
$race = "RACE-$stamp"
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)
$tasks = New-Object System.Collections.ArrayList
for ($i = 1; $i -le 5; $i++) {
  $json = ConvertTo-Json -InputObject (LandBody $race "PR$i-$stamp") -Depth 5 -Compress
  $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + '/api/lands')
  $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $tok
  $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
  [void]$tasks.Add($client.SendAsync($req))
}
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
$codes = @($tasks | ForEach-Object { [int]$_.Result.StatusCode })
$created = @($codes | Where-Object { $_ -eq 201 }).Count
$conflict = @($codes | Where-Object { $_ -eq 409 }).Count
$server = @($codes | Where-Object { $_ -ge 500 }).Count
Check 'R-07' 'race: exactly one 201' 1 $created ("codes=" + ($codes -join ','))
Check 'R-08' 'race: the other four return 409' 4 $conflict
Check 'R-09' 'race: no 500 responses' 0 $server
# A landlord-created land carries no region until the admin assessment, so the raced number is
# scoped by its governorate (UQ_LAND_NUMBER_GOV), which is what arbitrates this race.
$rowsDb = Sql "SELECT COUNT(*) FROM LANDS WHERE LAND_NUMBER = N'$race' AND GOVERNORATE_ID = $($fx.GovernorateId);"
Check 'R-10' 'DB: exactly one row for the raced LandNumber' 1 $rowsDb
$client.Dispose()

Write-Output ''
Write-Output '== file path (SEC-13): drive letters (new rule) =='
$docs = "/api/lands/$l1/documents"
foreach ($c in @(
  @{ id = 'R-11'; p = 'C:\Windows\win.ini' },
  @{ id = 'R-12'; p = 'c:/windows/win.ini' },
  @{ id = 'R-13'; p = 'C:Windows\win.ini' },
  @{ id = 'R-14'; p = 'D:relative\file.pdf' }
)) {
  $r = Call 'POST' $docs @{ documentTypeId = 2; filePath = $c.p } $tok
  Check $c.id ("drive-letter path rejected: " + $c.p) 400 $r.Status
}

Write-Output ''
Write-Output '== file path (SEC-13): existing rules still enforced =='
foreach ($c in @(
  @{ id = 'R-15'; p = 'http://169.254.169.254/' },
  @{ id = 'R-16'; p = 'file:///c:/windows/win.ini' },
  @{ id = 'R-17'; p = '\\server\share' },
  @{ id = 'R-18'; p = '..\..\x' },
  @{ id = 'R-19'; p = '/etc/passwd' },
  @{ id = 'R-20'; p = ('a' * 501) }
)) {
  $r = Call 'POST' $docs @{ documentTypeId = 2; filePath = $c.p } $tok
  Check $c.id ("path rejected: " + $(if ($c.p.Length -gt 40) { '501-character path' } else { $c.p })) 400 $r.Status
}
$r = Call 'POST' $docs @{ documentTypeId = 2; filePath = 'docs/titledeed-001.pdf' } $tok
Check 'R-21' 'accepted relative path docs/titledeed-001.pdf' 201 $r.Status
$r = Call 'POST' $docs @{ documentTypeId = 2; filePath = 'docs/C-drive-notes.pdf' } $tok
Check 'R-22' 'accepted: a C letter not in the drive position (no false positive)' 201 $r.Status
$cnt = Sql "SELECT COUNT(*) FROM LAND_DOCUMENTS WHERE LAND_ID = $l1;"
Check 'R-23' 'DB: rejected drive-letter and traversal paths created no rows (2 accepted)' 2 $cnt

Write-Output ''
Write-Output '== M3 visibility convention: missing -> 404, other owner -> 403 =='
$lbEmail2 = "s4r-$stamp-lb@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4R Landlord B'; email = $lbEmail2; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $lbEmail2 -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $lbEmail2 failed ($($v.Status))" }
$r = Call 'POST' '/api/auth/login' @{ email = $lbEmail2; password = $pw }
$btok = ($r.Body | ConvertFrom-Json).token
$r = Call 'GET' '/api/lands/999999' $null $tok
Check 'R-24' 'missing land -> 404' 404 $r.Status
$r = Call 'GET' "/api/lands/$l1" $null $btok
Check 'R-25' 'existing land of another landlord -> 403' 403 $r.Status
$r = Call 'GET' "/api/lands/$l1/documents" $null $btok
Check 'R-26' 'documents of another landlord''s land -> 403' 403 $r.Status
$r = Call 'GET' '/api/lands/999999/documents' $null $tok
Check 'R-27' 'documents of a missing land -> 404' 404 $r.Status
$r = Call 'GET' "/api/lands/$l1/eligibility" $null $btok
Check 'R-28' 'eligibility of another landlord''s land -> 403' 403 $r.Status

Write-Output ''
Write-Output '== M1 race: 5 parallel creates with the same parcel (UQ_LAND_LOCATION) =='
$pr = "PARCEL-RACE-$stamp"
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)
$tasks = New-Object System.Collections.ArrayList
for ($i = 1; $i -le 5; $i++) {
  $json = ConvertTo-Json -InputObject (LandBody "PL$i-$stamp" $pr) -Depth 5 -Compress
  $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + '/api/lands')
  $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $tok
  $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
  [void]$tasks.Add($client.SendAsync($req))
}
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
$pcodes = @($tasks | ForEach-Object { [int]$_.Result.StatusCode })
$pbodies = @($tasks | ForEach-Object { $_.Result.Content.ReadAsStringAsync().Result })
Check 'R-29' 'parcel race: exactly one 201' 1 @($pcodes | Where-Object { $_ -eq 201 }).Count ("codes=" + ($pcodes -join ','))
Check 'R-30' 'parcel race: the other four return 409' 4 @($pcodes | Where-Object { $_ -eq 409 }).Count
Check 'R-31' 'parcel race: no 500 responses' 0 @($pcodes | Where-Object { $_ -ge 500 }).Count
Check 'R-32' 'parcel race: no SQL/index text in any body' 0 @($pbodies | Where-Object { $_ -match 'UQ_|SqlException|Cannot insert|duplicate key|Microsoft\.' }).Count
# Same as R-10: the parcel identity is governorate-scoped before any assessment (UQ_LAND_LOCATION_GOV).
$pdb = Sql "SELECT COUNT(*) FROM LANDS WHERE GOVERNORATE_ID = $($fx.GovernorateId) AND VILLAGE = N'Village-R' AND BASIN = N'Basin-R' AND PARCEL_NUMBER = N'$pr' AND IS_DELETED = 0;"
Check 'R-33' 'DB: exactly one live row for the raced parcel' 1 $pdb
$client.Dispose()

Write-Output ''
Write-Output '== H1 race: 3 rounds of 5 parallel uploads, same land and document type =='
$h1land = $l1
for ($round = 1; $round -le 3; $round++) {
  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  $tasks = New-Object System.Collections.ArrayList
  for ($i = 1; $i -le 5; $i++) {
    $json = ConvertTo-Json -InputObject @{ documentTypeId = 1; filePath = "docs/concurrent-$round-$i.pdf" } -Depth 5 -Compress
    $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + "/api/lands/$h1land/documents")
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $tok
    $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
    [void]$tasks.Add($client.SendAsync($req))
  }
  [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
  $hcodes = @($tasks | ForEach-Object { [int]$_.Result.StatusCode })
  Check "R-34.$round" "upload round ${round}: all five are 201 (no 500)" 5 @($hcodes | Where-Object { $_ -eq 201 }).Count ("codes=" + ($hcodes -join ','))
  $client.Dispose()
}
$hcount = Sql "SELECT COUNT(*) FROM LAND_DOCUMENTS WHERE LAND_ID = $h1land AND DOCUMENT_TYPE_ID = 1;"
$hdistinct = Sql "SELECT COUNT(DISTINCT VERSION) FROM LAND_DOCUMENTS WHERE LAND_ID = $h1land AND DOCUMENT_TYPE_ID = 1;"
$hminmax = Sql "SELECT CAST(MIN(VERSION) AS varchar(5)) + '-' + CAST(MAX(VERSION) AS varchar(5)) FROM LAND_DOCUMENTS WHERE LAND_ID = $h1land AND DOCUMENT_TYPE_ID = 1;"
Check 'R-35' 'DB: 15 uploads persisted' 15 $hcount
Check 'R-36' 'DB: 15 distinct versions (no duplicates)' 15 $hdistinct
Check 'R-37' 'DB: versions are sequential 1-15' '1-15' $hminmax

Write-Output ''
Write-Output '== M2 SEC-17 free text: tab/newline allowed, other control chars rejected =='
$nl = [string][char]10
$tab = [string][char]9
$r = Call 'POST' "/api/lands/$l1/reject" @{ reason = ("line one" + $nl + "line two") } $tok
Check 'R-38' 'reject reason with newline passes validation, then 403 for a landlord' 403 $r.Status
$r = Call 'POST' "/api/lands/$l1/reject" @{ reason = ("tab" + $tab + "here") } $tok
Check 'R-39' 'reject reason with tab passes validation, then 403 for a landlord' 403 $r.Status
$r = Call 'POST' "/api/lands/$l1/reject" @{ reason = ("bad" + [char]1 + "reason") } $tok
Check 'R-40' 'reject reason with U+0001 is rejected by validation' 400 $r.Status
$r = Call 'PUT' "/api/lands/$l1/documents/1/review" @{ approved = $true; note = ("line one" + $nl + "line two") } $tok
Check 'R-41' 'review note with newline passes validation, then 403 for a landlord' 403 $r.Status
$r = Call 'PUT' "/api/lands/$l1/documents/1/review" @{ approved = $true; note = ("bad" + [char]1) } $tok
Check 'R-42' 'review note with U+0001 is rejected by validation' 400 $r.Status

Write-Output ''
Write-Output '== SEC-14 numeric bounds: values that used to overflow the DB column / the page offset =='
$x = LandBody "OVF-$stamp" "POVF-$stamp"; $x.areaDonum = 200000000
$r = Call 'POST' '/api/lands' $x $tok
Check 'R-43' 'areaDonum above decimal(10,2) range is rejected 400 (no DB-overflow 500)' 400 $r.Status
$x = LandBody "OVF2-$stamp" "POVF2-$stamp"; $x.areaDonum = 99999999.99
$r = Call 'POST' '/api/lands' $x $tok
Check 'R-44' 'areaDonum at decimal(10,2) max is accepted 201' 201 $r.Status
$r = Call 'GET' '/api/lands?pageNumber=2147483647&pageSize=50' $null $tok
Check 'R-45' 'pageNumber near Int32.Max returns an empty page (200), not an overflow 500' 200 $r.Status

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "REGRESSION TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
