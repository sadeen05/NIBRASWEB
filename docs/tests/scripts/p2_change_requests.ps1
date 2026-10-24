# NIBRAS Phase-2 change-request / evidence-visibility suite (PREPARED â€” Phase D only).
#
# Run ONLY after 20261018090000_Phase2_LandCorrectionAndChangeRequests has been applied and
# explicitly approved (see docs/tests/PHASE2_MATRIX.md), against the confirmed target
# the disposable database on SADEEN\SQLEXPRESS, with the app started via s4_app.ps1
# (-AuthPerMinute 1000). Never run against NEBRAS or NEBRASDB. Never executed during
# preparation; this file is a plan artifact until Phase D is authorized.
#
# - Disposable fixtures only: stamped emails/land numbers/parcels/file paths, returned ids.
# - FIXTURE-LOCAL UPDATEs touch only rows this run created (ids read from its own API
#   responses); no pre-existing business row is ever modified or deleted.
# - SuperAdmin credentials come from the disposable-environment variables and are never printed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
Add-Type -AssemblyName System.Net.Http | Out-Null   # Send-ParallelPost (concurrency section)
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
$db = (Get-TestTarget).Database
# The running instance is proved to write into the disposable database before anything is written.
Assert-TestInstanceIdentity | Out-Null
$rows = New-Object System.Collections.ArrayList

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $req.Headers['Authorization'] = 'Bearer ' + $Token }
  if ($null -ne $Body) {
    $req.Body = (ConvertTo-Json -InputObject $Body -Depth 6 -Compress)
    $req.ContentType = 'application/json'
  }
  try {
    $r = Invoke-WebRequest @req
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content; Location = [string]$r.Headers['Location'] }
  } catch {
    $resp = $_.Exception.Response
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $text; Location = '' }
  }
}

function Check([string]$Id, [string]$Case, $Expected, $Actual, [string]$Note = '') {
  $exp = [string]$Expected
  $act = [string]$Actual
  $res = if ($exp -eq $act) { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Case = $Case; Expected = $exp; Actual = $act; Result = $res; Note = $Note })
  Write-Output ("{0,-9} {1,-4} exp={2,-6} act={3,-6} {4} {5}" -f $Id, $res, $exp, $act, $Case, $Note)
}

# PS 5.1: ConvertFrom-Json emits a JSON array as ONE item (an Object[]); unroll it so
# callers can filter the elements (ForEach-Object enumerates the array).
function FromJsonItems([string]$Text) { return @($Text | ConvertFrom-Json | ForEach-Object { $_ }) }

function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -I -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}

function SqlExec([string]$Query) {
  # FIXTURE-LOCAL ONLY: every caller passes a WHERE that identifies exactly one row this
  # run created (its change-request / land id taken from this run's API response).
  # -I (QUOTED_IDENTIFIER ON) is required: LANDS and LAND_CHANGE_REQUESTS both carry
  # filtered indexes, and sqlcmd defaults QUOTED_IDENTIFIER OFF (Msg 1934 otherwise).
  # The query goes through a temp .sql file: with -Q, Windows PowerShell would splice
  # embedded double quotes (the malformed-JSON fixture) into separate native arguments.
  $sqlFile = Join-Path ([IO.Path]::GetTempPath()) ("p2_sqlexec_" + [guid]::NewGuid().ToString('N') + ".sql")
  Set-Content -Path $sqlFile -Value ("SET NOCOUNT ON; " + $Query) -Encoding Default
  try {
    $out = & sqlcmd -S $sqlServer -E -d $db -I -b -W -h -1 -i $sqlFile 2>&1
  } finally {
    Remove-Item $sqlFile -ErrorAction SilentlyContinue
  }
  if ($LASTEXITCODE -ne 0) { throw "fixture-local SQL failed: $out" }
}

function Login([string]$Email, [string]$Pass = $pw) {
  $r = Call 'POST' '/api/auth/login' @{ email = $Email; password = $Pass }
  if ($r.Status -ne 200) { throw "login failed: $($r.Status)" }
  return ($r.Body | ConvertFrom-Json).token
}

# Investor list containment: reads the last page of the ascending-id list, where a fresh
# fixture must appear when it is visible.
function List-Contains([string]$Token, [int]$LandId) {
  $r = Call 'GET' '/api/lands?pageSize=50' $null $Token
  if ($r.Status -ne 200) { throw "land list failed: $($r.Status)" }
  $j = $r.Body | ConvertFrom-Json
  $pages = [math]::Max(1, [int][math]::Ceiling([double]$j.totalCount / 50.0))
  if ($pages -gt 1) {
    $r = Call 'GET' ("/api/lands?pageSize=50&pageNumber=$pages") $null $Token
    $j = $r.Body | ConvertFrom-Json
  }
  return (@($j.items | Where-Object { [int]$_.id -eq $LandId }).Count -eq 1)
}

# Two genuinely overlapping HTTP requests (two tasks, one client), for the change-request races.
function Send-ParallelPost([string]$Path, $Bodies, [string]$Token) {
  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  $tasks = New-Object System.Collections.ArrayList
  foreach ($b in $Bodies) {
    $json = ConvertTo-Json -InputObject $b -Depth 6 -Compress
    $req = New-Object System.Net.Http.HttpRequestMessage -ArgumentList ([System.Net.Http.HttpMethod]::Post), ($base + $Path)
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue -ArgumentList 'Bearer', $Token
    $req.Content = New-Object System.Net.Http.StringContent -ArgumentList $json, ([System.Text.Encoding]::UTF8), 'application/json'
    [void]$tasks.Add($client.SendAsync($req))
  }
  [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())
  $out = @()
  foreach ($t in $tasks) { $resp = $t.Result; $out += [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $resp.Content.ReadAsStringAsync().Result } }
  $client.Dispose()
  return ,$out
}

function New-Land([string]$LandNumber, [string]$Parcel, [string]$Token) {
  $r = Call 'POST' '/api/lands' @{ governorateId = $fx.GovernorateId; landNumber = $LandNumber; village = 'Village-P2'; basin = 'Basin-P2'; parcelNumber = $Parcel; areaDonum = [decimal]25 } $Token
  if ($r.Status -ne 201) { throw "land create failed: $($r.Status) $($r.Body)" }
  return [int](($r.Body | ConvertFrom-Json).id)
}

function Assess-Land([int]$LandId) {
  $r = Call 'PUT' "/api/lands/$LandId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $fx.PrimaryGridId; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100; note = 'P2 fixture assessment' } $saTok
  if ($r.Status -ne 200) { throw "assessment failed: $($r.Status) $($r.Body)" }
}

function Verify-Land([int]$LandId, [string]$OwnerToken) {
  $docIds = @()
  foreach ($dt in 1..2) {
    $r = Call 'POST' "/api/lands/$LandId/documents/metadata" @{ documentTypeId = $dt; filePath = "docs/p2-$stamp-$dt.pdf" } $OwnerToken
    if ($r.Status -ne 201) { throw "document upload ($dt) failed: $($r.Status) $($r.Body)" }
    $docIds += [int](($r.Body | ConvertFrom-Json).id)
  }
  $r = Call 'POST' "/api/lands/$LandId/submit" $null $OwnerToken
  if ($r.Status -ne 200) { throw "submit failed: $($r.Status) $($r.Body)" }
  foreach ($docId in $docIds) {
    $r = Call 'PUT' "/api/lands/$LandId/documents/$docId/review" @{ approved = $true } $saTok
    if ($r.Status -ne 200) { throw "document review failed: $($r.Status) $($r.Body)" }
  }
  $r = Call 'POST' "/api/lands/$LandId/verify" $null $saTok
  if ($r.Status -ne 200) { throw "verify failed: $($r.Status) $($r.Body)" }
}

function Cr-Body([int]$LandId, [string]$LandNumber, [string]$Parcel, [decimal]$Area, [string]$Reason, [string]$Description = $null) {
  $b = @{ landId = $LandId; reason = $Reason; governorateId = $fx.GovernorateId; landNumber = $LandNumber; village = 'Village-P2'; basin = 'Basin-P2'; parcelNumber = $Parcel; areaDonum = $Area }
  if ($null -ne $Description) { $b.description = $Description }
  return $b
}

function Submit-Cr($Body, [string]$Token) {
  $r = Call 'POST' '/api/land-change-requests' $Body $Token
  if ($r.Status -ne 201) { throw "change request submit failed: $($r.Status) $($r.Body)" }
  return [int](($r.Body | ConvertFrom-Json).id)
}

function Decide([int]$CrId, [bool]$Approved, [string]$Reason, [string]$Token = $saTok) {
  return Call 'POST' "/api/land-change-requests/$CrId/decision" @{ approved = $Approved; decisionReason = $Reason } $Token
}

Write-Output '== setup (users, fixtures; credentials and tokens never printed) =='
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
if (-not $repoRoot) { Write-Output 'FATAL: NibrasWeb.csproj not found above the script directory'; exit 2 }
$sec = @{}
# The SuperAdmin credentials come from the dedicated disposable-environment variables.
# The user secrets are never enumerated: the suite reads exactly the two values it needs,
# and it never prints them.
$saEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
$saPass = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
if (-not $saEmail -or -not $saPass) { Write-Output 'BLOCKED: NIBRAS_TEST_SUPERADMIN_EMAIL / NIBRAS_TEST_SUPERADMIN_PASSWORD are not set'; exit 2 }
$saTok = Login $saEmail $saPass

$laEmail = "p2-$stamp-la@example.test"; $lbEmail = "p2-$stamp-lb@example.test"; $invEmail = "p2-$stamp-in@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'P2 Landlord A'; email = $laEmail; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $laEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $laEmail failed ($($v.Status))" }
if ($r.Status -ne 201) { throw "landlord A register failed: $($r.Status)" }
$laId = [int](($r.Body | ConvertFrom-Json).id)
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'P2 Landlord B'; email = $lbEmail; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $lbEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $lbEmail failed ($($v.Status))" }
if ($r.Status -ne 201) { throw "landlord B register failed: $($r.Status)" }
$lbId = [int](($r.Body | ConvertFrom-Json).id)
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'P2 Investor'; email = $invEmail; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'P2 Co'; companyRegistrationNumber = "P2-REG-$stamp" }

  $v = Confirm-VerifiedAccount -Email $invEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $invEmail failed ($($v.Status))" }
if ($r.Status -ne 201) { throw "investor register failed: $($r.Status)" }
$invId = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/users/$invId/approve" $null $saTok
if ($r.Status -ne 204) { throw "investor approve failed: $($r.Status)" }
$laTok = Login $laEmail
$lbTok = Login $lbEmail
$invTok = Login $invEmail
Write-Output "users: landlord A id=$laId, landlord B id=$lbId, investor id=$invId (approved)"

. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) governorate=$($fx.GovernorateId)"

$baseVerified = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 0 AND LAND_STATUS_ID = 3;")
$baseUnderContract = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 0 AND LAND_STATUS_ID = 5;")
$baseDeleted = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 1;")

Write-Output ''
Write-Output '== fixtures: three verified lands (A, B, C) with complete evidence =='
$aId = New-Land "P2-A-$stamp" "PA-$stamp" $laTok; Assess-Land $aId; Verify-Land $aId $laTok
$bId = New-Land "P2-B-$stamp" "PB-$stamp" $laTok; Assess-Land $bId; Verify-Land $bId $laTok
$cId = New-Land "P2-C-$stamp" "PC-$stamp" $laTok; Assess-Land $cId; Verify-Land $cId $laTok
Write-Output "lands: A=$aId B=$bId C=$cId (all Verified, complete evidence)"
Check 'P2-SET-01' 'investor reads verified fixture A' 200 (Call 'GET' "/api/lands/$aId" $null $invTok).Status
Check 'P2-SET-02' 'investor list contains A' 'True' ([string](List-Contains $invTok $aId))

Write-Output ''
Write-Output '== authorization and ownership =='
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'foreign landlord attempt') $lbTok
Check 'P2-A-05' 'non-owner landlord submits -> 403' 403 $r.Status
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'investor attempt') $invTok
Check 'P2-A-06' 'investor submits -> 403' 403 $r.Status
$r = Call 'POST' '/api/land-change-requests' (Cr-Body 2147480000 "P2-X-$stamp" "PX-$stamp" ([decimal]25) 'missing land attempt') $laTok
Check 'P2-A-07' 'change request for a missing land -> 404' 404 $r.Status
Check 'P2-A-08a' 'foreign landlord reads the land''s requests -> 403' 403 (Call 'GET' "/api/land-change-requests?landId=$aId" $null $lbTok).Status
Check 'P2-A-08b' 'landlord reads the pending queue -> 403' 403 (Call 'GET' '/api/land-change-requests/pending' $null $lbTok).Status
Check 'P2-A-09' 'anonymous pending queue -> 401' 401 (Call 'GET' '/api/land-change-requests/pending').Status

Write-Output ''
Write-Output '== validation =='
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) '') $laTok
Check 'P2-E-03a' 'empty reason -> 400' 400 $r.Status
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]0) 'zero area') $laTok
Check 'P2-E-03b' 'area 0 -> 400' 400 $r.Status
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]100000000) 'overflow area') $laTok
Check 'P2-E-03c' 'area above the maximum -> 400' 400 $r.Status
$b = Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'unknown supporting type'
$b.supportingDocuments = @(@{ documentTypeId = 999; filePath = "docs/p2-unknown-$stamp.pdf" })
$r = Call 'POST' '/api/land-change-requests' $b $laTok
Check 'P2-E-03d' 'unknown supporting document type -> 400' 400 $r.Status
$dId = New-Land "P2-D-$stamp" "PD-$stamp" $laTok
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $dId "P2-D-$stamp" "PD-$stamp" ([decimal]25) 'draft land attempt') $laTok
Check 'P2-B-02' 'change request on a Draft land -> 409' 409 $r.Status

Write-Output ''
Write-Output '== E: valid submit, one-pending guard, hidden flag, investor visibility =='
$e1 = Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'P2: correct the description' 'P2 original description'
$e1.supportingDocuments = @(@{ documentTypeId = 1; filePath = "docs/p2-support-$stamp.pdf" })
$r = Call 'POST' '/api/land-change-requests' $e1 $laTok
Check 'P2-E-01a' 'valid submit with a supporting document -> 201' 201 $r.Status
if ($r.Status -ne 201) { Write-Output 'BLOCKED: fixture submit failed'; exit 3 }
$cr1 = [int](($r.Body | ConvertFrom-Json).id)
Check 'P2-E-01b' 'Location points at the per-land collection' 'True' ([string]($r.Location -match "landId=$aId"))
$dbv = Sql "SELECT STATUS + '|' + CAST(ISJSON(OLD_SNAPSHOT) AS varchar(1)) + '|' + CAST(ISJSON(NEW_SNAPSHOT) AS varchar(1)) + '|' + CAST(ISJSON(SUPPORTING_DOCUMENTS) AS varchar(1)) + '|' + CAST(REQUESTED_BY_ID AS varchar(10)) FROM LAND_CHANGE_REQUESTS WHERE ID = $cr1;"
Check 'P2-E-01c' 'DB row: Pending, three valid JSON payloads, owner recorded' "Pending|1|1|1|$laId" $dbv
$r = Call 'POST' '/api/land-change-requests' (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'second attempt') $laTok
Check 'P2-E-02' 'second submit while one is pending -> 409' 409 $r.Status
$dbv = Sql "SELECT CAST(IS_HIDDEN_FROM_INVESTORS AS varchar(1)) + '|' + CAST(LAND_STATUS_ID AS varchar(3)) FROM LANDS WHERE ID = $aId;"
Check 'P2-F-01' 'DB: hidden flag set, status stays Verified(3)' '1|3' $dbv
Check 'P2-F-02a' 'investor detail of the hidden land -> 403' 403 (Call 'GET' "/api/lands/$aId" $null $invTok).Status
Check 'P2-F-02b' 'investor list no longer contains A' 'False' ([string](List-Contains $invTok $aId))
Check 'P2-F-03' 'owner still reads the land' 200 (Call 'GET' "/api/lands/$aId" $null $laTok).Status
$r = Call 'POST' '/api/offers' @{ landId = $aId; requiredCapacityMw = 5; connectionMechanism = 1; landlordSharePct = 30 } $invTok
Check 'P2-F-04' 'investor offer create on the hidden land -> 409' 409 $r.Status
Check 'P2-F-04b' 'message names the open change request' 'True' ([string]($r.Body -match 'change request is under review'))
$r = Call 'GET' "/api/land-change-requests?landId=$aId" $null $laTok
$found = @(FromJsonItems $r.Body | Where-Object { [int]$_.id -eq $cr1 }).Count -eq 1
Check 'P2-E-04a' 'owner lists the land''s change requests (contains the pending one)' '200|True' ("$($r.Status)|$([string]$found)")
$r = Call 'GET' '/api/land-change-requests/pending' $null $saTok
$found = @(FromJsonItems $r.Body | Where-Object { [int]$_.id -eq $cr1 }).Count -eq 1
Check 'P2-E-04b' 'SuperAdmin pending queue contains the request' '200|True' ("$($r.Status)|$([string]$found)")
Check 'P2-G-02' 'landlord decision -> 403' 403 (Decide $cr1 $true 'landlord attempt' $laTok).Status
$r = Call 'POST' "/api/land-change-requests/$cr1/decision" @{ approved = $true; decisionReason = '' } $saTok
Check 'P2-E-09' 'empty decision reason -> 400' 400 $r.Status
Check 'P2-E-08' 'decision on a missing request -> 404' 404 (Decide 2147480000 $true 'missing').Status

Write-Output ''
Write-Output '== H: malformed stored snapshots (FIXTURE-LOCAL updates on this run''s own rows) =='
$areaBefore = Sql "SELECT CAST(AREA_DONUM AS varchar(20)) FROM LANDS WHERE ID = $aId;"
$villageBefore = Sql "SELECT VILLAGE FROM LANDS WHERE ID = $aId;"
SqlExec "UPDATE LAND_CHANGE_REQUESTS SET OLD_SNAPSHOT = N'{}' WHERE ID = $cr1 AND LAND_ID = $aId;"
$r = Decide $cr1 $true 'P2 malformed old snapshot'
Check 'P2-H-01a' 'approve with an unreadable before-change snapshot -> 409' 409 $r.Status
Check 'P2-H-01b' 'message names the before-change snapshot' 'True' ([string]($r.Body -match 'before-change snapshot'))
$dbv = Sql "SELECT CAST(AREA_DONUM AS varchar(20)) + '|' + VILLAGE FROM LANDS WHERE ID = $aId;"
Check 'P2-H-01c' 'land fields unchanged by the refused approval (DB)' "$areaBefore|$villageBefore" $dbv
Check 'P2-H-02a' 'stored snapshot left untouched (never repaired)' '{}' (Sql "SELECT OLD_SNAPSHOT FROM LAND_CHANGE_REQUESTS WHERE ID = $cr1;")
$r = Decide $cr1 $false 'P2 reject after unreadable snapshot'
Check 'P2-H-02b' 'rejection still possible -> 200' 200 $r.Status
$r = Call 'GET' "/api/land-change-requests?landId=$aId" $null $laTok
$row = @(FromJsonItems $r.Body | Where-Object { [int]$_.id -eq $cr1 })[0]
Check 'P2-H-04a' 'GET reports the unreadable snapshot as null' 'True' ([string]($null -eq $row.originalFields))
Check 'P2-M-01' 'investor sees the land again after the rejection' 200 (Call 'GET' "/api/lands/$aId" $null $invTok).Status
Check 'P2-F-05a' 'DB: hidden flag cleared by the rejection' '0' (Sql "SELECT CAST(IS_HIDDEN_FROM_INVESTORS AS varchar(1)) FROM LANDS WHERE ID = $aId;")
$cr2 = Submit-Cr (Cr-Body $aId "P2-A-$stamp" "PA-$stamp" ([decimal]25) 'P2 second attempt' 'P2 second description') $laTok
$badJson = '{"governorateId":' + $fx.GovernorateId + ',"landNumber":"","village":"Village-P2","basin":"Basin-P2","parcelNumber":"PX-' + $stamp + '","areaDonum":5}'
SqlExec ("UPDATE LAND_CHANGE_REQUESTS SET NEW_SNAPSHOT = N'" + $badJson + "' WHERE ID = " + $cr2 + " AND LAND_ID = $aId;")
$r = Decide $cr2 $true 'P2 malformed new snapshot'
Check 'P2-H-03b' 'approve with an unusable proposed snapshot -> 409' 409 $r.Status
Check 'P2-H-03c' 'message names the proposed-change snapshot' 'True' ([string]($r.Body -match 'proposed-change snapshot'))
$r = Decide $cr2 $false 'P2 reject second attempt'
Check 'P2-H-03d' 'rejection still possible -> 200' 200 $r.Status
$r = Call 'GET' "/api/land-change-requests?landId=$aId" $null $laTok
$row = @(FromJsonItems $r.Body | Where-Object { [int]$_.id -eq $cr2 })[0]
Check 'P2-H-04b' 'GET reports the unusable proposed snapshot as null' 'True' ([string]($null -eq $row.proposedFields))

Write-Output ''
Write-Output '== E/F/J/L: approval applies values and invalidates the evidence basis (land B) =='
$criterionBefore = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;"
$crB1 = Submit-Cr (Cr-Body $bId "P2-B-$stamp" "PB-$stamp" ([decimal]30) 'P2 area correction' 'P2 B approved description') $laTok
$r = Decide $crB1 $true 'P2 approve the area change'
Check 'P2-E-06a' 'approve applies the proposed values -> 200' 200 $r.Status
$dbv = Sql "SELECT CAST(AREA_DONUM AS varchar(20)) + '|' + ISNULL(DESCRIPTION,'') + '|' + CAST(IS_HIDDEN_FROM_INVESTORS AS varchar(1)) + '|' + CAST(LAND_STATUS_ID AS varchar(3)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;"
Check 'P2-E-06b' 'DB: new area/description, pin and status preserved, flag cleared' "30.00|P2 B approved description|0|3|$criterionBefore" $dbv
$dbv = Sql "SELECT CAST(SUM(CASE WHEN STATUS='Pending' THEN 1 ELSE 0 END) AS varchar(3)) + '|' + CAST(SUM(CASE WHEN STATUS='Approved' THEN 1 ELSE 0 END) AS varchar(3)) + '|' + CAST(COUNT(*) AS varchar(3)) FROM LAND_DOCUMENTS WHERE LAND_ID = $bId;"
Check 'P2-J-02' 'DB: both approvals invalidated in place (Pending|Approved|rows)' '2|0|2' $dbv
Check 'P2-F-02c' 'investor detail of B -> 403 (outstanding evidence)' 403 (Call 'GET' "/api/lands/$bId" $null $invTok).Status
$r = Call 'POST' '/api/offers' @{ landId = $bId; requiredCapacityMw = 5; connectionMechanism = 1; landlordSharePct = 30 } $invTok
Check 'P2-L-02' 'offer create while the documents are outstanding -> 409' 409 $r.Status
Check 'P2-L-02b' 'message names the document correction' 'True' ([string]($r.Body -match 'required documents are being corrected'))

# M-02: a rejection while the evidence is still outstanding clears the flag but leaves the land invisible.
$crB2 = Submit-Cr (Cr-Body $bId "P2-B-$stamp" "PB-$stamp" ([decimal]30) 'P2 while outstanding' 'P2 B while outstanding') $laTok
$r = Decide $crB2 $false 'P2 reject while outstanding'
Check 'P2-M-02a' 'rejection while outstanding -> 200; flag cleared' '200|0' ("$($r.Status)|" + (Sql "SELECT CAST(IS_HIDDEN_FROM_INVESTORS AS varchar(1)) FROM LANDS WHERE ID = $bId;"))
Check 'P2-M-02b' 'B stays invisible (evidence outstanding)' 403 (Call 'GET' "/api/lands/$bId" $null $invTok).Status

# J-03/04: correction upload only for the outstanding type; SuperAdmin re-approval.
$r = Call 'POST' "/api/lands/$bId/documents/metadata" @{ documentTypeId = 1; filePath = "docs/p2-b-title-correction-$stamp.pdf" } $laTok
Check 'P2-J-03' 'correction upload for the outstanding type -> 201' 201 $r.Status
$bDoc1v2 = [int](($r.Body | ConvertFrom-Json).id)
Check 'P2-J-03b' 'correction is a new Pending version (v2)' '2|Pending' (Sql "SELECT CAST(VERSION AS varchar(3)) + '|' + STATUS FROM LAND_DOCUMENTS WHERE ID = $bDoc1v2;")
$r = Call 'PUT' "/api/lands/$bId/documents/$bDoc1v2/review" @{ approved = $true } $saTok
Check 'P2-J-05b' 'SuperAdmin approves the corrected version -> 200' 200 $r.Status
$r = Call 'POST' "/api/lands/$bId/documents/metadata" @{ documentTypeId = 1; filePath = "docs/p2-b-extra-$stamp.pdf" } $laTok
Check 'P2-J-04' 'upload for a type whose latest is approved -> 409' 409 $r.Status
Check 'P2-J-04b' 'message names the correction restriction' 'True' ([string]($r.Body -match 'only the document types whose latest version is not approved'))

# J-05: a regular Admin can never re-review an approved land's evidence.
$admEmail = "p2-$stamp-adm@example.test"
$r = Call 'POST' '/api/users' @{ fullName = 'P2 Disposable Admin'; email = $admEmail; phone = (New-Phone); password = $pw; roleId = 3 } $saTok
if ($r.Status -ne 201) { throw "disposable admin create failed: $($r.Status)" }
$admId = [int](($r.Body | ConvertFrom-Json).id)
# A new administrator account has no usable password: it is activated with the activation
# link and a password of this suite's choosing.
  Activate-AdminAccount -Email $admEmail -Password $pw -Baseline $baseline | Out-Null
$admTok = Login $admEmail
$bDoc2v1 = [int](Sql "SELECT CAST(ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE LAND_ID = $bId AND DOCUMENT_TYPE_ID = 2 AND VERSION = 1;")
$r = Call 'PUT' "/api/lands/$bId/documents/$bDoc2v1/review" @{ approved = $true } $admTok
Check 'P2-J-05' 'regular Admin re-review on an approved land -> 403' 403 $r.Status

# I-03: an unavailable criteria basis blocks the re-approval but never a rejection; the pin is
# restored afterwards. The basis is made unavailable by clearing the pin to NULL ("not
# recorded"): FK_LANDS_VERIFIED_CRITERION forbids pointing it at a non-existent row, and both
# guards treat a missing pin and a missing row identically (LandService lines 1095/1133).
$pinB = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;"
if ([string]::IsNullOrWhiteSpace($pinB) -or $pinB -eq 'NULL') { throw 'P2 setup: land B has no criterion pin to restore' }
$pinSql = [string]$pinB
try {
  SqlExec "UPDATE LANDS SET VERIFIED_AGAINST_CRITERION_ID = NULL WHERE ID = $bId;"
  $r = Call 'PUT' "/api/lands/$bId/documents/$bDoc2v1/review" @{ approved = $true } $saTok
  Check 'P2-I-03a' 're-approval with an unrecorded pin -> 409' 409 $r.Status
  Check 'P2-I-03b2' 'message names the missing criteria basis' 'True' ([string]($r.Body -match 'criteria version this land was approved against is unavailable'))
  $r = Call 'PUT' "/api/lands/$bId/documents/$bDoc2v1/review" @{ approved = $false; note = 'P2 recheck' } $saTok
  Check 'P2-I-03b' 'rejection is unaffected by the pin -> 200' 200 $r.Status
}
finally {
  # This pin is the fixture's approval basis. Restore it even when an assertion or
  # request throws; the independent read below proves the restoration took effect.
  SqlExec "UPDATE LANDS SET VERIFIED_AGAINST_CRITERION_ID = $pinSql WHERE ID = $bId;"
}
$pinAfter = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;"
if ($pinAfter -ne $pinB) { throw "fixture pin restoration failed: expected '$pinB', got '$pinAfter'" }
$r = Call 'POST' "/api/lands/$bId/documents/metadata" @{ documentTypeId = 2; filePath = "docs/p2-b-enc-correction-$stamp.pdf" } $laTok
Check 'P2-I-03c' 'corrected upload for the rejected type -> 201' 201 $r.Status
$bDoc2v2 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'PUT' "/api/lands/$bId/documents/$bDoc2v2/review" @{ approved = $true } $saTok
Check 'P2-I-03d' 're-approval after restoring the pin -> 200' 200 $r.Status
Check 'P2-J-07' 'investor sees B again once every required type is re-approved' 200 (Call 'GET' "/api/lands/$bId" $null $invTok).Status
Check 'P2-I-03e' 'DB: pin restored to the original criteria version' "$pinB" (Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;")
$r = Call 'PUT' "/api/lands/$bId/documents/$bDoc2v2/review" @{ approved = $true } $saTok
Check 'P2-J-06' 're-review with complete evidence -> 409 (before authority)' 409 $r.Status

Write-Output ''
Write-Output '== I: pinned-criteria guards on the decision path (land B) =='
$crB3 = Submit-Cr (Cr-Body $bId "P2-B-$stamp" "PB-$stamp" ([decimal]1) 'P2 below-minimal area' 'P2 below minimal') $laTok
$r = Decide $crB3 $true 'P2 try below-minimum approval'
Check 'P2-I-01a' 'area below the pinned minimum -> 409' 409 $r.Status
Check 'P2-I-01b' 'message names the pinned minimum' 'True' ([string]($r.Body -match 'below the minimum'))
$r = Decide $crB3 $false 'P2 reject after the refused approval'
Check 'P2-I-01c' 'rejection still possible after the refusal -> 200' 200 $r.Status
Check 'P2-M-01b' 'investor sees B again after that rejection' 200 (Call 'GET' "/api/lands/$bId" $null $invTok).Status
try {
  SqlExec "UPDATE LANDS SET VERIFIED_AGAINST_CRITERION_ID = NULL WHERE ID = $bId;"
  $crB4 = Submit-Cr (Cr-Body $bId "P2-B-$stamp" "PB-$stamp" ([decimal]30) 'P2 unrecorded pin' 'P2 unrecorded pin description') $laTok
  $r = Decide $crB4 $true 'P2 approve under an unrecorded pin'
  Check 'P2-I-02a' 'approve with an unrecorded pin -> 409' 409 $r.Status
  Check 'P2-I-02b2' 'message names the unavailable criteria version' 'True' ([string]($r.Body -match 'criteria version this land was approved against is unavailable'))
  $r = Decide $crB4 $false 'P2 reject under an unrecorded pin'
  Check 'P2-I-02b' 'rejection still possible -> 200' 200 $r.Status
}
finally {
  SqlExec "UPDATE LANDS SET VERIFIED_AGAINST_CRITERION_ID = $pinSql WHERE ID = $bId;"
}
$pinFinal = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $bId;"
if ($pinFinal -ne $pinB) { throw "fixture pin restoration failed: expected '$pinB', got '$pinFinal'" }

Write-Output ''
Write-Output '== P: change-request concurrency (land C) =='
$cBody = Cr-Body $cId "P2-C-$stamp" "PC-$stamp" ([decimal]25) 'P2 race change request' 'P2 race refined'
$res = Send-ParallelPost '/api/land-change-requests' @($cBody, $cBody) $laTok
$codes = @($res | ForEach-Object { $_.Status })
Check 'P2-P-01a' 'submit race: exactly one 201' 1 @($codes | Where-Object { $_ -eq 201 }).Count ("codes=" + ($codes -join ','))
Check 'P2-P-01b' 'submit race: the other is 409' 1 @($codes | Where-Object { $_ -eq 409 }).Count
Check 'P2-P-01c' 'submit race: no 5xx' 0 @($codes | Where-Object { $_ -ge 500 }).Count
Check 'P2-P-01d' 'submit race: no SQL/index text in any body' 0 @($res | Where-Object { $_.Body -match 'UX_|SqlException|Cannot insert|Microsoft\.' }).Count
Check 'P2-P-01e' 'DB: exactly one pending request for C' 1 ([int](Sql "SELECT COUNT(*) FROM LAND_CHANGE_REQUESTS WHERE LAND_ID = $cId AND STATUS = 'Pending';"))
$crC = [int](Sql "SELECT CAST(MAX(ID) AS varchar(10)) FROM LAND_CHANGE_REQUESTS WHERE LAND_ID = $cId;")
$dec = @{ approved = $true; decisionReason = 'P2 race decision' }
$res = Send-ParallelPost "/api/land-change-requests/$crC/decision" @($dec, $dec) $saTok
$codes = @($res | ForEach-Object { $_.Status })
Check 'P2-P-02a' 'decision race: exactly one 200' 1 @($codes | Where-Object { $_ -eq 200 }).Count ("codes=" + ($codes -join ','))
Check 'P2-P-02b' 'decision race: the other is 409' 1 @($codes | Where-Object { $_ -eq 409 }).Count
Check 'P2-P-02c' 'decision race: no 5xx' 0 @($codes | Where-Object { $_ -ge 500 }).Count
$dbv = Sql "SELECT c.STATUS + '|' + CAST(l.IS_HIDDEN_FROM_INVESTORS AS varchar(1)) FROM LAND_CHANGE_REQUESTS c JOIN LANDS l ON l.ID = c.LAND_ID WHERE c.ID = $crC;"
Check 'P2-P-02d' 'DB: decided once, hidden flag cleared' 'Approved|0' $dbv
Check 'P2-P-02e' 'DB: exactly one decision history row' 1 ([int](Sql "SELECT COUNT(*) FROM LAND_STATUS_HISTORY WHERE LAND_ID = $cId AND REASON LIKE 'Change request #$crC approved%';"))
Check 'P2-P-02f' 'investor sees C after the description-only approval' 200 (Call 'GET' "/api/lands/$cId" $null $invTok).Status

Write-Output ''
Write-Output '== regression invariants =='
$vNow = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 0 AND LAND_STATUS_ID = 3;")
$ucNow = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 0 AND LAND_STATUS_ID = 5;")
$delNow = [int](Sql "SELECT COUNT(*) FROM LANDS WHERE IS_DELETED = 1;")
Check 'P2-REG-01' 'Verified count did not shrink (fixtures only added)' 'True' ([string]($vNow -ge $baseVerified)) ("baseline=$baseVerified now=$vNow")
Check 'P2-REG-02' 'UnderContract count unchanged' "$baseUnderContract" "$ucNow"
Check 'P2-REG-03' 'no soft-deleted lands' "$baseDeleted" "$delNow"
$outstanding = [int](Sql "SELECT COUNT(*) FROM LANDS l WHERE l.IS_DELETED = 0 AND l.LAND_STATUS_ID = 3 AND EXISTS (SELECT 1 FROM DOCUMENT_TYPES t WHERE t.IS_REQUIRED_FOR_VERIFICATION = 1 AND NOT EXISTS (SELECT 1 FROM LAND_DOCUMENTS d WHERE d.LAND_ID = l.ID AND d.DOCUMENT_TYPE_ID = t.ID AND d.STATUS = 'Approved' AND d.VERSION = (SELECT MAX(d2.VERSION) FROM LAND_DOCUMENTS d2 WHERE d2.LAND_ID = l.ID AND d2.DOCUMENT_TYPE_ID = t.ID)));")
Check 'P2-REG-04' 'no verified land left with outstanding evidence' 0 $outstanding

Write-Output ''
Write-Output "fixture ids: A=$aId B=$bId C=$cId; CRs=$cr1,$cr2,$crB1,$crB2,$crB3,$crB4,$crC; admin=$admId"
$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "PHASE2 TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
}
finally {
  Complete-TestApiCleanup -Identity $harness
}
