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
$db = (Get-TestTarget).Database
# The running instance is proved to write into the disposable database before anything is written.
Assert-TestInstanceIdentity | Out-Null
$rows = New-Object System.Collections.ArrayList

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }
function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $req.Headers['Authorization'] = 'Bearer ' + $Token }
  if ($null -ne $Body) { $req.Body = (ConvertTo-Json -InputObject $Body -Depth 6 -Compress); $req.ContentType = 'application/json' }
  try { $r = Invoke-WebRequest @req; return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content } }
  catch { $resp = $_.Exception.Response; $t = ''
    if ($resp) { $t = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $t } }
}
function Check([string]$Id, [string]$Desc, $Expected, $Actual) {
  $ok = ("$Expected" -eq "$Actual"); $res = if ($ok) { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Result = $res })
  Write-Output ("{0,-8} {1,-4} exp={2,-22} act={3,-22} {4}" -f $Id, $res, $Expected, $Actual, $Desc)
}
function Q([string]$sql) { ((sqlcmd -S $sqlServer -E -d $db -W -h -1 -Q "SET NOCOUNT ON; $sql" 2>&1) -join ' ').Trim() }
# Phase 1: the landlord supplies only the governorate, the location identifiers and the area.
# $slope is retained in New-Land's signature so existing call sites stay valid; the region, the
# grid and the technical values are now recorded through PUT /api/lands/{id}/assessment.
function New-Land([string]$ln, [string]$parcel, [decimal]$slope) {
  @{ governorateId = $fx.GovernorateId; landNumber = $ln; village = 'Village-ADM'; basin = 'Basin-ADM';
     parcelNumber = $parcel; areaDonum = 100 }
}
function Assess-Land([int]$landId, [decimal]$slope, [string]$adminTok) {
  $r = Call 'PUT' "/api/lands/$landId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $fx.PrimaryGridId;
     slopePercentage = $slope; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100;
     note = 'S4 admin fixture assessment' } $adminTok
  if ($r.Status -ne 200) { throw "land $landId assessment failed: $($r.Status) body=$($r.Body)" }
}

# --- seed SuperAdmin credentials: dedicated disposable-environment variables, never printed ---
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
if (-not $repoRoot) { Write-Output 'BLOCKED: NibrasWeb.csproj not found above the script directory'; exit 2 }
$sec = @{}
# The SuperAdmin credentials come from the dedicated disposable-environment variables.
# The user secrets are never enumerated: the suite reads exactly the two values it needs,
# and it never prints them.
$adminEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
$adminPass = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
if (-not $adminEmail -or -not $adminPass) { Write-Output 'BLOCKED: NIBRAS_TEST_SUPERADMIN_EMAIL / NIBRAS_TEST_SUPERADMIN_PASSWORD are not set'; exit 2 }

Write-Output '== setup (credentials never printed) =='
$r = Call 'POST' '/api/auth/login' @{ email = $adminEmail; password = $adminPass }
Check 'A-00' 'seeded SuperAdmin can log in' 200 $r.Status
if ($r.Status -ne 200) { Write-Output ('admin login body (no secret): ' + $r.Body); exit 3 }
$admin = ($r.Body | ConvertFrom-Json).token

$laEmail = "s4adm-la-$stamp@example.test"
$baseline = New-OutboxBaseline
Call 'POST' '/api/auth/register' @{ fullName = 'Admin-Test Landlord A'; email = $laEmail; phone = (New-Phone); password = $pw; roleId = 1 } | Out-Null

  $v = Confirm-VerifiedAccount -Email $laEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $laEmail failed ($($v.Status))" }
$la = (Call 'POST' '/api/auth/login' @{ email = $laEmail; password = $pw } | ForEach-Object { ($_.Body | ConvertFrom-Json).token })

$invEmail = "s4adm-inv-$stamp@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'Admin-Test Investor'; email = $invEmail; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'Solar Co'; companyRegistrationNumber = 'REG-ADM-001' }

  $v = Confirm-VerifiedAccount -Email $invEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $invEmail failed ($($v.Status))" }
Check 'A-01' 'register investor (unapproved)' 201 $r.Status
$invId = ($r.Body | ConvertFrom-Json).id
$inv = (Call 'POST' '/api/auth/login' @{ email = $invEmail; password = $pw } | ForEach-Object { ($_.Body | ConvertFrom-Json).token })

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

# --- create three pending lands ---
$r = Call 'POST' '/api/lands' (New-Land "ADM-L1-$stamp" "PA1-$stamp" 5) $la
Check 'A-02' 'create L1' 201 $r.Status
$l1 = ($r.Body | ConvertFrom-Json).id
Assess-Land $l1 5 $admin
$r = Call 'POST' "/api/lands/$l1/documents/metadata" @{ documentTypeId = 1; filePath = "docs/$stamp/title.pdf" } $la
Check 'A-03' 'upload required type 1 on L1' 201 $r.Status
$l1d1 = ($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$l1/documents/metadata" @{ documentTypeId = 2; filePath = "docs/$stamp/enc.pdf" } $la
$l1d2 = ($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$l1/submit" $null $la
Check 'A-04' 'submit L1 -> PendingVerification' 200 $r.Status

$r = Call 'POST' '/api/lands' (New-Land "ADM-L2-$stamp" "PA2-$stamp" 5) $la
$l2 = ($r.Body | ConvertFrom-Json).id
Assess-Land $l2 5 $admin
$r = Call 'POST' "/api/lands/$l2/documents/metadata" @{ documentTypeId = 1; filePath = "docs/$stamp/l2-title.pdf" } $la
$l2d1 = ($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$l2/documents/metadata" @{ documentTypeId = 2; filePath = "docs/$stamp/l2-enc.pdf" } $la
$l2d2 = ($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$l2/submit" $null $la
Check 'A-05' 'submit L2 -> PendingVerification' 200 $r.Status
$r = Call 'PUT' "/api/lands/$l2/documents/$l2d1/review" @{ approved = $true } $admin
Check 'A-07' 'admin approves the L2 title document' 200 $r.Status
$r = Call 'PUT' "/api/lands/$l2/documents/$l2d2/review" @{ approved = $true } $admin
Check 'A-08' 'admin approves the L2 encumbrance document' 200 $r.Status

$r = Call 'POST' '/api/lands' (New-Land "ADM-L3-$stamp" "PA3-$stamp" 5) $la
$l3 = ($r.Body | ConvertFrom-Json).id
Call 'POST' "/api/lands/$l3/documents/metadata" @{ documentTypeId = 1; filePath = "docs/$stamp/l3-title.pdf" } $la | Out-Null
$r = Call 'POST' "/api/lands/$l3/submit" $null $la
Check 'A-06' 'submit L3 -> PendingVerification' 200 $r.Status

# Phase 1: an unassessed land cannot be verified â€” a missing measurement is never a pass.
$r = Call 'POST' "/api/lands/$l3/verify" $null $admin
Check 'A-06b' 'verify an unassessed land -> 409' 409 $r.Status
Check 'A-06c' 'message names the incomplete technical assessment' 'True' ([string]($r.Body -match 'Technical assessment is incomplete'))
Assess-Land $l3 5 $admin

Write-Output ''
Write-Output '== SEC-09 admin browse + D2/D1 + verification lifecycle =='
$r = Call 'GET' '/api/lands?status=PendingVerification&pageSize=50' $null $admin
Check 'S4-55' 'admin lists PendingVerification (admin sees all)' 200 $r.Status

$r = Call 'POST' "/api/lands/$l1/verify" $null $admin
Check 'S4-50' 'verify while a required document is Pending -> 409' 409 $r.Status

$before = Q "SELECT CONVERT(varchar(200), FILE_PATH) + '|' + CAST(DOCUMENT_TYPE_ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE ID=$l1d1;"
$r = Call 'PUT' "/api/lands/$l1/documents/$l1d1/review" @{ approved = $false } $admin
Check 'S4-56a' 'reject a document without a note -> 400' 400 $r.Status
$r = Call 'PUT' "/api/lands/$l1/documents/$l1d1/review" @{ approved = $true } $admin
Check 'S4-56b' 'admin approves the L1 title document' 200 $r.Status
Call 'PUT' "/api/lands/$l1/documents/$l1d2/review" @{ approved = $true } $admin | Out-Null
$after = Q "SELECT CONVERT(varchar(200), FILE_PATH) + '|' + CAST(DOCUMENT_TYPE_ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE ID=$l1d1;"
Check 'S4-56c' 'review leaves filePath/documentTypeId unchanged' $before $after

$r = Call 'POST' "/api/lands/$l1/verify" $null $admin
Check 'S4-51a' 'verify L1 with every required doc approved -> 200' 200 $r.Status
$state = Q "SELECT CAST(LAND_STATUS_ID AS varchar(3)) + '|' + CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CASE WHEN VERIFIED_AGAINST_CRITERION_ID IS NULL THEN 'NULL' ELSE 'SET' END FROM LANDS WHERE ID=$l1;"
Check 'S4-51b' 'DB: L1 status=3, dataVerified=1, criterion recorded' '3|1|SET' $state
$v1 = Q "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID=$l1;"

$r = Call 'PUT' "/api/lands/$l1" (New-Land "ADM-L1-$stamp" "PA1-$stamp" 6) $la
Check 'S4-53' 'owner edit of a Verified land -> 409' 409 $r.Status

$r = Call 'PUT' "/api/lands/$l1/documents/$l1d1/review" @{ approved = $false; note = 'recheck' } $admin
Check 'S4-60' 'D1: review a document of a Verified land -> 409' 409 $r.Status

$r = Call 'POST' "/api/users/$invId/approve" $null $admin
Check 'S4-54a' 'admin approves the investor -> 204' 204 $r.Status
$r = Call 'GET' '/api/lands?pageSize=50' $null $inv
$onlyVerified = $true
if ($r.Status -eq 200) { foreach ($it in ($r.Body | ConvertFrom-Json).items) { if ($it.status -ne 'Verified') { $onlyVerified = $false } } } else { $onlyVerified = $false }
Check 'S4-54b' 'approved investor lists lands: 200 and every item Verified' '200|True' ("$($r.Status)|$onlyVerified")

Write-Output ''
Write-Output '== reject -> revise -> resubmit -> verify (second cycle) =='
$r = Call 'POST' "/api/lands/$l2/reject" @{ reason = 'Missing clarity in evidence' } $admin
Check 'S4-52a' 'admin rejects L2 with a reason -> 200' 200 $r.Status
$r = Call 'POST' "/api/lands/$l2/revise" $null $la
Check 'S4-52b' 'owner revises L2 -> Draft' 200 $r.Status
$r = Call 'PUT' "/api/lands/$l2" (New-Land "ADM-L2-$stamp" "PA2-$stamp" 5) $la
Check 'S4-52c' 'owner edits the revised L2 -> 204' 204 $r.Status
$r = Call 'POST' "/api/lands/$l2/submit" $null $la
Check 'S4-52d' 'owner resubmits L2 -> 200' 200 $r.Status
$r = Call 'POST' "/api/lands/$l2/verify" $null $admin
Check 'S4-52e' 'admin verifies L2 on the second cycle -> 200' 200 $r.Status

Write-Output ''
Write-Output '== SEC-06 versioned criteria (4.7) =='
$criteriaBefore = Call 'GET' '/api/criteria/current' $null $admin
if ($criteriaBefore.Status -ne 200) { throw 'Current criteria unavailable before SuperAdmin replacement' }
$r = Call 'POST' '/api/criteria' @{ expectedCurrentId = [int](($criteriaBefore.Body | ConvertFrom-Json).id); minAreaDonum = 20; maxSlopePct = 8; maxGridDistanceKm = 15; minSolarIrradiance = 5; minElevationM = $null; reason = 'Step4 admin test v2' } $admin
Check 'S4-57a' 'SuperAdmin creates criteria replacement -> 201' 201 $r.Status
$v2 = ($r.Body | ConvertFrom-Json).id
$r = Call 'GET' '/api/criteria/history' $null $admin
Check 'S4-57b' 'admin lists criteria history (>= 2 versions)' 200 $r.Status
$hist = ($r.Body | ConvertFrom-Json)
Check 'S4-57c' 'history contains both versions' 'True' ("$($hist.Count -ge 2)")

$stillV1 = Q "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID=$l1;"
Check 'S4-58a' 'a Verified land keeps its original criterion id' $v1 $stillV1
# Phase 1: the landlord creates the land (area only); the admin records the technical values.
$r = Call 'POST' '/api/lands' (New-Land "ADM-L4-$stamp" "PA4-$stamp" 7) $la
Check 'S4-58b' 'new land accepted under v2' 201 $r.Status
$l4 = ($r.Body | ConvertFrom-Json).id
$elig = (Call 'GET' "/api/lands/$l4/eligibility" $null $la | ForEach-Object { $_.Body | ConvertFrom-Json })
Check 'S4-58c' 'new land is evaluated against v2' "$v2" "$($elig.criterionId)"
$r = Call 'GET' "/api/lands/$l4/eligibility" $null $la
$slopeBefore = @(($r.Body | ConvertFrom-Json).results | Where-Object { $_.criterion -eq 'slope' })[0]
Check 'S4-58c2' 'unassessed slope is NotAssessed (never a pass)' 'NotAssessed' "$($slopeBefore.result)"
Assess-Land $l4 7 $admin
$r = Call 'GET' "/api/lands/$l4/eligibility" $null $la
$slopeAfter = @(($r.Body | ConvertFrom-Json).results | Where-Object { $_.criterion -eq 'slope' })[0]
Check 'S4-58c3' 'assessed slope 7 passes under v2 (max 8)' 'Passed' "$($slopeAfter.result)"

$r = Call 'POST' '/api/lands' (New-Land "ADM-L5-$stamp" "PA5-$stamp" 9) $la
Check 'S4-58d' 'new land accepted (technical values are not landlord-supplied)' 201 $r.Status
$l5 = ($r.Body | ConvertFrom-Json).id
Assess-Land $l5 9 $admin
$r = Call 'GET' "/api/lands/$l5/eligibility" $null $la
$slopeFail = @(($r.Body | ConvertFrom-Json).results | Where-Object { $_.criterion -eq 'slope' })[0]
Check 'S4-58e' 'assessed slope 9 fails v2 (max 8) -> Failed' 'Failed' "$($slopeFail.result)"

Write-Output ''
Write-Output '== document types: delete guard and flag change (D3/D2) =='
$r = Call 'POST' '/api/document-types' @{ name = "TempType-$stamp"; isRequiredForVerification = $false } $admin
$tempType = ($r.Body | ConvertFrom-Json).id
$r = Call 'DELETE' "/api/document-types/$tempType" $null $admin
Check 'S4-59a' 'admin deletes an unused document type -> 204' 204 $r.Status
$r = Call 'DELETE' '/api/document-types/1' $null $admin
Check 'S4-59b' 'admin deleting a used document type -> 409' 409 $r.Status

try {
  $snapBefore = Q "SELECT CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID=$l1;"
  $r = Call 'PUT' '/api/document-types/2' @{ name = 'EncumbranceStatement'; isRequiredForVerification = $false } $admin
  Check 'S4-62a' 'admin clears the required flag on a document type -> 200' 200 $r.Status
  $snapAfter = Q "SELECT CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID=$l1;"
  Check 'S4-62b' 'D3: existing Verified snapshot is unchanged by the flag change' $snapBefore $snapAfter

  $r = Call 'PUT' '/api/document-types/1' @{ name = 'TitleDeed'; isRequiredForVerification = $false } $admin
  Check 'S4-61a' 'admin clears the last required flag -> 200' 200 $r.Status
  $r = Call 'POST' "/api/lands/$l3/verify" $null $admin
  Check 'S4-61b' 'D2: verify with no required document type -> 409' 409 $r.Status
}
finally {
  Call 'PUT' '/api/document-types/1' @{ name = 'TitleDeed'; isRequiredForVerification = $true } $admin | Out-Null
  Call 'PUT' '/api/document-types/2' @{ name = 'EncumbranceStatement'; isRequiredForVerification = $true } $admin | Out-Null
}
$restored = Q "SELECT CAST(SUM(CASE WHEN IS_REQUIRED_FOR_VERIFICATION=1 THEN 1 ELSE 0 END) AS varchar(3)) FROM DOCUMENT_TYPES;"
Check 'S4-61c' 'required flags restored (2 required types)' '2' $restored

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output ("ADMIN TOTAL PASS=$pass FAIL=$fail")
if ($fail -gt 0) { exit 1 }
}
finally {
  Complete-TestApiCleanup -Identity $harness
}
