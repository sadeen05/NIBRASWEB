# Step 6 - Contracts MVP acceptance suite (never prints credentials or tokens).
# Starts the app once with RateLimit__AuthPerMinute=1000, then drives the contract lifecycle
# through the public API and asserts database state. Target database: the disposable one, resolved from the connection string.
# The concurrency blocks use in-process concurrent HttpClient requests: no session JWT is
# passed to a PowerShell job or a child process.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
Add-Type -AssemblyName System.Net.Http

$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
. (Join-Path $PSScriptRoot 'test_target.ps1')
# The target is resolved from the effective connection string and is fail-closed:
# only the disposable database is accepted, and a mismatch stops the run.
$sqlServer = (Get-TestTarget).Server
$db = (Get-TestTarget).Database
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
  $res = if ("$Expected" -eq "$Actual") { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Result = $res })
  Write-Output ("{0,-8} {1,-4} exp={2,-22} act={3,-22} {4}" -f $Id, $res, $Expected, $Actual, $Desc)
}
function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}
function Login([string]$Email, [string]$Pass) {
  $r = Call 'POST' '/api/auth/login' @{ email = $Email; password = $Pass }
  if ($r.Status -ne 200) { throw "login failed ($($r.Status))" }
  return ($r.Body | ConvertFrom-Json).token
}
function Reg([string]$FullName, [string]$Email, [int]$RoleId) {
  $body = @{ fullName = $FullName; email = $Email; phone = (New-Phone); password = $pw; roleId = $RoleId }
  if ($RoleId -eq 2) { $body.companyName = 'S6 Co'; $body.companyRegistrationNumber = "S6-REG-$stamp" }
  $baseline = New-OutboxBaseline
  $r = Call 'POST' '/api/auth/register' $body
  if ($r.Status -ne 201) { throw "register failed ($($r.Status))" }
  # The account can only sign in after its address was confirmed.
  $v = Confirm-VerifiedAccount -Email $Email -Baseline $baseline
  if ($v.Status -ne 204) { throw "the verification of $Email failed ($($v.Status))" }
  return [int](($r.Body | ConvertFrom-Json).id)
}
function Verify-Land([int]$landId, [string]$laTok, [string]$adminTok) {
  $docIds = @()
  foreach ($dt in 1..2) {
    $r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = $dt; filePath = 'docs/titledeed-001.pdf' } $laTok
    if ($r.Status -ne 201) { throw "land $landId document upload ($dt) failed: $($r.Status)" }
    $docIds += [int](($r.Body | ConvertFrom-Json).id)
  }
  $r = Call 'POST' "/api/lands/$landId/submit" $null $laTok
  if ($r.Status -ne 200) { throw "land $landId submit failed: $($r.Status)" }
  foreach ($docId in $docIds) {
    $r = Call 'PUT' "/api/lands/$landId/documents/$docId/review" @{ approved = $true } $adminTok
    if ($r.Status -ne 204 -and $r.Status -ne 200) { throw "land $landId review failed: $($r.Status)" }
  }
  $r = Call 'POST' "/api/lands/$landId/verify" $null $adminTok
  if ($r.Status -ne 200) { throw "land $landId verify failed: $($r.Status)" }
}
# Phase 1: the landlord supplies only governorate/location/area; the region, grid and the four
# technical measurements are admin-only and are recorded through PUT /api/lands/{id}/assessment.
function New-Land-Body([string]$ln, [string]$parcel, [int]$gid) {
  @{ governorateId = $fx.GovernorateId; landNumber = $ln; village = 'V-S6'; basin = 'B-S6';
     parcelNumber = $parcel; areaDonum = 100 }
}
function Assess-Land([int]$landId, [int]$gid, [string]$adminTok) {
  $r = Call 'PUT' "/api/lands/$landId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $gid;
     slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100;
     note = 'S6 fixture assessment' } $adminTok
  if ($r.Status -ne 200) { throw "land $landId assessment failed: $($r.Status) body=$($r.Body)" }
}
function Create-Land([string]$ln, [string]$parcel, [int]$gid, [string]$laTok, [string]$adminTok) {
  $r = Call 'POST' '/api/lands' (New-Land-Body $ln $parcel $gid) $laTok
  if ($r.Status -ne 201) { throw "land create failed: $($r.Status)" }
  $landId = [int](($r.Body | ConvertFrom-Json).id)
  Assess-Land $landId $gid $adminTok
  Verify-Land $landId $laTok $adminTok
  return $landId
}

# --- target verification BEFORE any database access ---
Write-Output "== S6 setup ($stamp) - credentials never printed =="
$target = Sql "SELECT DB_NAME();"
if ($target -ne $db) { Write-Output "BLOCKED: expected $db but connected to '$target'"; exit 2 }
Check 'S6-00' 'database target is the configured disposable database' $db $target

# --- start the app once for this run ---
. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = $null
try {
$app = Start-S4App  -AuthPerMinute 1000
# The base URL comes from the harness-owned instance, and the gate proves that
# the instance is the one the harness started - before anything is written.
$base = Initialize-TestApi
# The instance that was just started is proved to write into the disposable database.
Assert-TestInstanceIdentity | Out-Null

$pass = 0; $fail = 0
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

  $admin = Login $adminEmail $adminPass
  Check 'S6-01' 'seeded SuperAdmin login' 200 200

  # --- disposable users ---
  $laEmail = "s6-$stamp-la@example.test"; $lbEmail = "s6-$stamp-lb@example.test"
  $i1Email = "s6-$stamp-i1@example.test"; $i2Email = "s6-$stamp-i2@example.test"
  $laId = Reg 'S6 Landlord A' $laEmail 1; $laTok = Login $laEmail $pw
  $lbId = Reg 'S6 Landlord B' $lbEmail 1; $lbTok = Login $lbEmail $pw
  $i1Id = Reg 'S6 Investor 1' $i1Email 2; $r = Call 'POST' "/api/users/$i1Id/approve" $null $admin; if ($r.Status -ne 204) { throw "approve i1 failed" }; $i1Tok = Login $i1Email $pw
  $i2Id = Reg 'S6 Investor 2' $i2Email 2; $r = Call 'POST' "/api/users/$i2Id/approve" $null $admin; if ($r.Status -ne 204) { throw "approve i2 failed" }; $i2Tok = Login $i2Email $pw
  # A regular Admin is needed to prove that the SuperAdmin-only escalation decision is not
  # available to an Admin. The invitation is created by the SuperAdmin and activated through
  # the real endpoint with a token read from the disposable outbox.
  $admEmail = "s6-$stamp-adm@example.test"
  $admBaseline = New-OutboxBaseline
  $r = Call 'POST' '/api/users' @{ fullName = 'S6 Admin'; email = $admEmail; phone = (New-Phone); password = $pw; roleId = 3 } $admin
  if ($r.Status -ne 201) { throw "admin create failed ($($r.Status))" }
  $admActivation = Get-VerificationTokenFromOutbox -Email $admEmail -Baseline $admBaseline -SubjectLike '*activation*'
  $r = Complete-AdminActivation -Token $admActivation -Password $pw
  if ($r.Status -ne 204) { throw "admin activation failed ($($r.Status))" }
  $adminTok = Login $admEmail $pw
  Check 'S6-01a' 'disposable regular Admin activated and signs in' 200 200
  Write-Output "users: la=$laId lb=$lbId i1=$i1Id i2=$i2Id adm=created"

  # Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
  # assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
  . (Join-Path $PSScriptRoot 'test_fixtures.ps1')
  $fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
  Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

  # --- grid + verified lands ---
  $r = Call 'POST' '/api/grids' @{ regionId = $fx.PrimaryRegionId; name = "S6-G-$stamp"; capacityMw = 100; status = 'Operational' } $admin
  Check 'S6-02' 'admin creates grid' 201 $r.Status
  $grid = [int](($r.Body | ConvertFrom-Json).id)

  $L1 = Create-Land "S6-L1-$stamp" "S6P-L1-$stamp" $grid $laTok $admin
  $L2 = Create-Land "S6-L2-$stamp" "S6P-L2-$stamp" $grid $laTok $admin
  $L3 = Create-Land "S6-L3-$stamp" "S6P-L3-$stamp" $grid $laTok $admin
  $L4 = Create-Land "S6-L4-$stamp" "S6P-L4-$stamp" $grid $laTok $admin
  Write-Output "lands: L1=$L1 L2=$L2 L3=$L3 L4=$L4 grid=$grid"

  # Step 7: a COMPETING offer on L1 by a second investor, created while the land is still
  # Verified. It stays non-terminal (frozen) while L1 is UnderContract and must be closed
  # with a CompetitorClosed history row when the contract on L1 becomes Active.
  $r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 25 } $i2Tok
  Check 'S6-03' 'competing offer on L1 created (land Verified)' 201 $r.Status
  $oc1 = [int](($r.Body | ConvertFrom-Json).id)

  # --- accepted offers (L1/L2/L3) + a non-accepted offer (L4) ---
  function Accept-Approve([int]$land, [string]$iTok) {
    $r = Call 'POST' '/api/offers' @{ landId = $land; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $iTok
    if ($r.Status -ne 201) { throw "offer create on land $land failed: $($r.Status)" }
    $oid = [int](($r.Body | ConvertFrom-Json).id)
    $r = Call 'POST' "/api/offers/$oid/accept" $null $laTok
    if ($r.Status -ne 200) { throw "offer accept failed: $($r.Status)" }
    $r = Call 'POST' "/api/offers/$oid/admin-approve" $null $admin
    if ($r.Status -ne 200) { throw "offer approve failed: $($r.Status)" }
    return $oid
  }
  $o1 = Accept-Approve $L1 $i1Tok
  $o2 = Accept-Approve $L2 $i1Tok
  $o3 = Accept-Approve $L3 $i1Tok
  $r = Call 'POST' '/api/offers' @{ landId = $L4; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
  $o4 = [int](($r.Body | ConvertFrom-Json).id)
  Write-Output "offers: o1=$o1 o2=$o2 o3=$o3 (accepted) o4=$o4 (pending)"

  $criterion1 = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $L1;"
  $accVer1 = Sql "SELECT CAST(ACCEPTED_VERSION_ID AS varchar(10)) FROM OFFERS WHERE ID = $o1;"

  # ================= Creation =================
  Write-Output ''
  Write-Output '== Creation =='
  $r = Call 'POST' '/api/contracts' @{ offerId = $o1 }
  Check 'S6-10' 'anonymous create contract -> 401' 401 $r.Status
  $r = Call 'POST' '/api/contracts' @{ offerId = $o1 } $laTok
  Check 'S6-11' 'landlord create contract -> 403' 403 $r.Status
  $r = Call 'POST' '/api/contracts' @{ offerId = $o1 } $i1Tok
  Check 'S6-12' 'investor create contract -> 403' 403 $r.Status
  $r = Call 'POST' '/api/contracts' @{ offerId = 2147480000 } $admin
  Check 'S6-13' 'unknown offer -> 404' 404 $r.Status
  $r = Call 'POST' '/api/contracts' @{ offerId = $o4 } $admin
  Check 'S6-14' 'offer not Accepted -> 409' 409 $r.Status

  $r = Call 'POST' '/api/contracts' @{ offerId = $o1 } $admin
  Check 'S6-15' 'valid accepted offer -> 201' 201 $r.Status
  $c1 = [int](($r.Body | ConvertFrom-Json).id)
  Check 'S6-16' 'new contract status PendingSignatures' 'PendingSignatures' ([string](($r.Body | ConvertFrom-Json).status))
  $dbCrit = Sql "SELECT CAST(CRITERION_ID AS varchar(10)) FROM CONTRACTS WHERE ID = $c1;"
  Check 'S6-17' 'contract criterion = land VerifiedAgainstCriterionId' $criterion1 $dbCrit
  $dbVer = Sql "SELECT CAST(OFFER_VERSION_ID AS varchar(10)) FROM CONTRACTS WHERE ID = $c1;"
  Check 'S6-18' 'contract offer version = offer AcceptedVersionId' $accVer1 $dbVer
  $resContract = Sql "SELECT CAST(CONTRACT_ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $o1;"
  Check 'S6-19' 'reservation receives the new ContractId' ("$c1") $resContract
  $oStatus = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $o1;"
  Check 'S6-20' 'offer closed (Closed=9)' 9 $oStatus
  $oHist = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $o1 AND ACTION_TYPE = 8;"
  Check 'S6-21' 'ClosedByContract history recorded' 1 $oHist
  $r = Call 'POST' '/api/contracts' @{ offerId = $o1 } $admin
  Check 'S6-22' 'duplicate contract for same offer -> 409' 409 $r.Status

  # extra JSON properties must not bypass validation or set server-controlled fields
  $r = Call 'POST' '/api/contracts' @{ offerId = $o2; id = 12345; status = 'Active'; investorId = 999 } $admin
  Check 'S6-23' 'extra JSON properties ignored -> 201' 201 $r.Status
  $c2 = [int](($r.Body | ConvertFrom-Json).id)
  Check 'S6-24' 'server-controlled status not overridden' 'PendingSignatures' ([string](($r.Body | ConvertFrom-Json).status))
  Check 'S6-25' 'server-generated id not overridden' 'True' ([string]($c2 -ne 12345))
  $r = Call 'POST' '/api/contracts' @{ offerId = $o3 } $admin
  Check 'S6-26' 'third contract (for review-before-sign case) -> 201' 201 $r.Status
  $c3 = [int](($r.Body | ConvertFrom-Json).id)

  # ================= Signing =================
  Write-Output ''
  Write-Output '== Signing =='
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $i2Tok
  Check 'S6-30' 'non-party sign -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $admin
  Check 'S6-31' 'admin sign -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $i1Tok
  Check 'S6-32' 'investor signs once -> 200' 200 $r.Status
  Check 'S6-33' 'investor signed timestamp set' 'True' ([string]($null -ne ($r.Body | ConvertFrom-Json).investorSignedAt))
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $i1Tok
  Check 'S6-34' 'repeated investor signature -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $laTok
  Check 'S6-35' 'landlord signs once -> 200' 200 $r.Status
  Check 'S6-36' 'landlord signed timestamp set' 'True' ([string]($null -ne ($r.Body | ConvertFrom-Json).landlordSignedAt))

  # ================= Review =================
  Write-Output ''
  Write-Output '== Review =='
  $r = Call 'POST' "/api/contracts/$c3/sign" $null $i1Tok
  Check 'S6-40' 'C3 investor signs (landlord not yet)' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/review" @{ approve = $true } $admin
  Check 'S6-41' 'review before both signed -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/review" @{ approve = $true } $laTok
  Check 'S6-42' 'landlord review -> 403' 403 $r.Status
  $resBefore = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $grid;"
  $r = Call 'POST' "/api/contracts/$c1/review" @{ approve = $true } $admin
  Check 'S6-43' 'successful approval -> 200' 200 $r.Status
  Check 'S6-44' 'contract Active after approval' 'Active' ([string](($r.Body | ConvertFrom-Json).status))
  Check 'S6-45' 'admin review timestamp set' 'True' ([string]($null -ne ($r.Body | ConvertFrom-Json).adminReviewedAt))
  $resAfter = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $grid;"
  Check 'S6-46' 'review does not create a reservation' $resBefore $resAfter
  $appr = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c1 AND DECISION = 'Approved';"
  Check 'S6-47' 'Approved review row recorded' 1 $appr
  $compStatus = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oc1;"
  Check 'S6-47a' 'competing offer closed when contract becomes Active (Closed=9)' 9 $compStatus
  $compHist = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oc1 AND ACTION_TYPE = 9;"
  Check 'S6-47b' 'CompetitorClosed history row recorded' 1 $compHist
  $r = Call 'POST' "/api/contracts/$c1/review" @{ approve = $true } $admin
  Check 'S6-48' 'repeated review -> 409' 409 $r.Status
  $r = Call 'GET' "/api/contracts/$c1/reviews" $null $admin
  Check 'S6-49' 'reviews readable by admin -> 200' 200 $r.Status

  # ================= Rejection =================
  Write-Output ''
  Write-Output '== Rejection =='
  $r = Call 'POST' "/api/contracts/$c2/sign" $null $i1Tok; Check 'S6-50' 'C2 investor signs' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c2/sign" $null $laTok; Check 'S6-51' 'C2 landlord signs' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c2/review" @{ approve = $false } $admin
  Check 'S6-52' 'reject without reason -> 400' 400 $r.Status
  $r = Call 'POST' "/api/contracts/$c2/review" @{ approve = $false; reason = 'Terms not acceptable' } $admin
  Check 'S6-53' 'reject with reason -> 200' 200 $r.Status
  Check 'S6-54' 'contract Rejected' 'Rejected' ([string](($r.Body | ConvertFrom-Json).status))
  $relC2 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c2 AND RELEASED_AT IS NOT NULL;"
  Check 'S6-55' 'reservation released on rejection' 1 $relC2
  $landC2 = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L2;"
  Check 'S6-56' 'land back to Verified(3) after rejection' 3 $landC2
  $histC2 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM LAND_STATUS_HISTORY WHERE LAND_ID = $L2 AND STATUS_ID = 3;"
  Check 'S6-57' 'land history row for Verified' 'True' ([string]([int]$histC2 -ge 1))
  $r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 25 } $i2Tok
  Check 'S6-58' 'new offer allowed after rejection (land Verified)' 201 $r.Status

  # ================= Termination =================
  Write-Output ''
  Write-Output '== Termination =='
  $r = Call 'POST' "/api/contracts/$c1/terminate" @{ reason = 'Project cancelled' } $laTok
  Check 'S6-60' 'landlord terminate -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/terminate" @{ reason = 'Project cancelled' } $admin
  Check 'S6-61' 'terminate non-Active contract -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/terminate" @{ reason = '' } $admin
  Check 'S6-62' 'terminate without reason -> 400' 400 $r.Status
  $r = Call 'POST' "/api/contracts/$c1/terminate" @{ reason = 'Project cancelled' } $admin
  Check 'S6-63' 'terminate active contract -> 200' 200 $r.Status
  Check 'S6-64' 'contract Terminated' 'Terminated' ([string](($r.Body | ConvertFrom-Json).status))
  $relC1 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c1 AND RELEASED_AT IS NOT NULL;"
  Check 'S6-65' 'reservation released on termination' 1 $relC1
  $landC1 = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L1;"
  Check 'S6-66' 'land back to Verified(3) after termination' 3 $landC1
  $r = Call 'POST' "/api/contracts/$c1/sign" $null $i1Tok
  Check 'S6-67' 'sign a non-PendingSignatures contract -> 409' 409 $r.Status

  # ================= Visibility & validation =================
  Write-Output ''
  Write-Output '== Visibility & validation =='
  $r = Call 'GET' "/api/contracts/$c1" $null $i2Tok
  Check 'S6-70' 'third-party read existing contract -> 403' 403 $r.Status
  $r = Call 'GET' "/api/contracts/$c1/reviews" $null $i2Tok
  Check 'S6-71' 'third-party read reviews -> 403' 403 $r.Status
  $r = Call 'GET' "/api/contracts/2147480000" $null $admin
  Check 'S6-72' 'missing contract -> 404' 404 $r.Status
  $r = Call 'GET' '/api/contracts' $null $lbTok
  $lbItems = 0; if ($r.Status -eq 200) { $lbItems = @(($r.Body | ConvertFrom-Json).items).Count }
  Check 'S6-73' 'other landlord lists no contracts' 0 $lbItems
  $r = Call 'GET' '/api/contracts' $null $i2Tok
  $i2Items = 0; if ($r.Status -eq 200) { $i2Items = @(($r.Body | ConvertFrom-Json).items).Count }
  Check 'S6-74' 'third-party investor lists no contracts' 0 $i2Items
  # S6-75: the admin's list must contain c1. The list is paged (pageSize <= 50) and ordered by
  # Id ascending, so a contract created by this run sits on a LATER page once the table holds
  # more rows than one page. Walk the pages using the response's own pagination metadata
  # (totalPages) and parse the JSON structurally instead of matching raw text. Empty or error
  # responses end the walk safely with $admHas = $false.
  $admHas = $false
  $page = 1
  do {
    $r = Call 'GET' ("/api/contracts?pageNumber=$page&pageSize=50") $null $admin
    if ($r.Status -ne 200) { break }
    $body = $r.Body | ConvertFrom-Json
    if (@($body.items | Where-Object { $_.id -eq $c1 }).Count -gt 0) { $admHas = $true; break }
    $totalPages = [int]$body.totalPages
    $page++
  } while ($page -le $totalPages -and $page -le 100)
  Check 'S6-75' 'admin lists all contracts (contains c1)' 'True' ([string]$admHas)
  $r = Call 'GET' "/api/contracts/$c1" $null $i1Tok
  Check 'S6-76' 'investor party reads own contract -> 200' 200 $r.Status
  $r = Call 'GET' '/api/contracts?status=Bogus' $null $admin
  Check 'S6-77' 'invalid status -> 400' 400 $r.Status
  $r = Call 'GET' '/api/contracts?pageSize=1000' $null $admin
  Check 'S6-78' 'pageSize=1000 -> 400' 400 $r.Status
  $r = Call 'GET' '/api/contracts?status=Active' $null $admin
  Check 'S6-79' 'valid status filter -> 200' 200 $r.Status

  # ================= Criteria compatibility =================
  Write-Output ''
  Write-Output '== Criteria compatibility =='
  $criteriaBefore = Call 'GET' '/api/criteria/current' $null $admin
  if ($criteriaBefore.Status -ne 200) { throw 'Current criteria unavailable before SuperAdmin replacement' }
  $r = Call 'POST' '/api/criteria' @{ expectedCurrentId = [int](($criteriaBefore.Body | ConvertFrom-Json).id); minAreaDonum = 20; maxSlopePct = 8; maxGridDistanceKm = 15; minSolarIrradiance = 5; minElevationM = $null; reason = 'S6 stricter criterion' } $admin
  Check 'S6-80' 'SuperAdmin adds a stricter criterion -> 201' 201 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/sign" $null $laTok
  Check 'S6-81' 'C3 landlord signs after criterion change' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/review" @{ approve = $true } $admin
  Check 'S6-82' 'review of land verified under older criterion still succeeds -> 200' 200 $r.Status
  Check 'S6-83' 'C3 becomes Active (no re-verification during review)' 'Active' ([string](($r.Body | ConvertFrom-Json).status))

  # ================= M-1: concurrent operations on the same contract =================
  Write-Output ''
  Write-Output '== M-1: concurrent operations on the same contract =='
  # C4: two concurrent investor signatures
  $L5 = Create-Land "S6-L5-$stamp" "S6P-L5-$stamp" $grid $laTok $admin
  $o5 = Accept-Approve $L5 $i1Tok
  $r = Call 'POST' '/api/contracts' @{ offerId = $o5 } $admin
  $c4 = [int](($r.Body | ConvertFrom-Json).id)
  $signRace = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = "/api/contracts/$c4/sign"; BearerToken = $i1Tok },
    @{ Method = 'POST'; Path = "/api/contracts/$c4/sign"; BearerToken = $i1Tok }
  ) -TimeoutSeconds 60
  $sg1 = [int]$signRace[0].Status
  $sg2 = [int]$signRace[1].Status
  Check 'S6-90' 'concurrent same-party sign: exactly one 200, one 409' '200 409' ((@($sg1, $sg2) | Sort-Object) -join ' ')
  $signCount = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACTS WHERE ID = $c4 AND INVESTOR_SIGNED_AT IS NOT NULL;"
  Check 'S6-91' 'exactly one investor signature persisted' 1 $signCount

  # C4: two concurrent reviews (after both parties signed)
  $r = Call 'POST' "/api/contracts/$c4/sign" $null $laTok
  Check 'S6-92' 'C4 landlord signs' 200 $r.Status
  $reviewBody = (ConvertTo-Json -InputObject @{ approve = $true } -Compress)
  $reviewRace = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = "/api/contracts/$c4/review"; Json = $reviewBody; BearerToken = $admin },
    @{ Method = 'POST'; Path = "/api/contracts/$c4/review"; Json = $reviewBody; BearerToken = $admin }
  ) -TimeoutSeconds 60
  $rv1 = [int]$reviewRace[0].Status
  $rv2 = [int]$reviewRace[1].Status
  Check 'S6-93' 'concurrent review: exactly one 200, one 409' '200 409' ((@($rv1, $rv2) | Sort-Object) -join ' ')
  $apprCount = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c4 AND DECISION = 'Approved';"
  Check 'S6-94' 'exactly one Approved review row persisted' 1 $apprCount
  $c4Status = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM CONTRACTS WHERE ID = $c4;"
  Check 'S6-95' 'C4 Active(2) after the race' 2 $c4Status

  # C6: concurrent review (approve) vs terminate on the same contract
  $L6 = Create-Land "S6-L6-$stamp" "S6P-L6-$stamp" $grid $laTok $admin
  $o6 = Accept-Approve $L6 $i1Tok
  $r = Call 'POST' '/api/contracts' @{ offerId = $o6 } $admin
  $c6 = [int](($r.Body | ConvertFrom-Json).id)
  $null = Call 'POST' "/api/contracts/$c6/sign" $null $i1Tok
  $null = Call 'POST' "/api/contracts/$c6/sign" $null $laTok
  $termBody = (ConvertTo-Json -InputObject @{ reason = 'race' } -Compress)
  $reviewTerminate = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = "/api/contracts/$c6/review"; Json = $reviewBody; BearerToken = $admin },
    @{ Method = 'POST'; Path = "/api/contracts/$c6/terminate"; Json = $termBody; BearerToken = $admin }
  ) -TimeoutSeconds 60
  $sR = [int]$reviewTerminate[0].Status
  $sT = [int]$reviewTerminate[1].Status
  Check 'S6-96' 'race: review(approve) succeeds (200)' 200 $sR
  Check 'S6-97' 'race: terminate is 200 (after approval) or 409 (before), never 500' 'True' ([string]($sT -eq 200 -or $sT -eq 409))
  $c6Status = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM CONTRACTS WHERE ID = $c6;"
  $expC6 = if ($sT -eq 200) { '3' } else { '2' }
  Check 'S6-98' 'race: C6 state consistent (Terminated if terminate won, else Active)' $expC6 $c6Status
  $c6Reviews = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c6;"
  $expReviews = if ($sT -eq 200) { '2' } else { '1' }
  Check 'S6-99' 'race: no duplicate/conflicting review rows' $expReviews $c6Reviews
  $c6Rel = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c6 AND RELEASED_AT IS NOT NULL;"
  $expRel = if ($sT -eq 200) { '1' } else { '0' }
  Check 'S6-100' 'race: reservation released iff terminated' $expRel $c6Rel

  # ================= Step 7 F1/F2: reservation expiry + unsigned rejection =================
  Write-Output ''
  Write-Output '== Step 7 F1/F2: reservation expiry + unsigned rejection =='

  # F1: approval clears the reservation's hold expiry (EXPIRES_AT -> NULL)
  $L7 = Create-Land "S6-L7-$stamp" "S6P-L7-$stamp" $grid $laTok $admin
  $o7 = Accept-Approve $L7 $i1Tok
  $r = Call 'POST' '/api/contracts' @{ offerId = $o7 } $admin
  if ($r.Status -ne 201) { throw "F1 setup: contract create failed ($($r.Status))" }
  $c7 = [int](($r.Body | ConvertFrom-Json).id)
  $expBefore = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c7 AND EXPIRES_AT IS NOT NULL;"
  Check 'S6-101' 'F1: reservation carries a hold expiry before approval' 1 $expBefore
  $null = Call 'POST' "/api/contracts/$c7/sign" $null $i1Tok
  $null = Call 'POST' "/api/contracts/$c7/sign" $null $laTok
  $r = Call 'POST' "/api/contracts/$c7/review" @{ approve = $true } $admin
  Check 'S6-102' 'F1: approval succeeds once both parties signed' 200 $r.Status
  $expAfter = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c7 AND EXPIRES_AT IS NULL;"
  Check 'S6-103' 'F1: approval clears the reservation hold expiry' 1 $expAfter

  # F1: an expired hold cannot become a contract
  $L8 = Create-Land "S6-L8-$stamp" "S6P-L8-$stamp" $grid $laTok $admin
  $o8 = Accept-Approve $L8 $i1Tok
  $expOriginal = Sql "SELECT CONVERT(varchar(33), EXPIRES_AT, 126) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $o8;"
  if ([string]::IsNullOrWhiteSpace($expOriginal)) { throw 'F1 setup: the reservation has no original EXPIRES_AT to restore' }
  $expRestore = "CONVERT(datetime2, '$expOriginal', 126)"
  try {
    # sqlcmd defaults QUOTED_IDENTIFIER/ANSI_NULLS OFF; this table requires them ON for DML.
    $null = Sql "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; UPDATE GRID_CAPACITY_RESERVATIONS SET EXPIRES_AT = DATEADD(day, -1, GETUTCDATE()) WHERE OFFER_ID = $o8;"
    $expSet = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $o8 AND EXPIRES_AT <= GETUTCDATE();"
    if ($expSet -ne '1') { throw "F1 setup: failed to expire the reservation (count=$expSet)" }
    $r = Call 'POST' '/api/contracts' @{ offerId = $o8 } $admin
    Check 'S6-104' 'F1: expired reservation -> 409' 409 $r.Status
    Check 'S6-105' 'F1: exact expiry message' 'The grid reservation for this offer has expired.' ([string](($r.Body | ConvertFrom-Json).detail))
  }
  finally {
    # The forced expiry is test-only state. Restore the value captured before the UPDATE,
    # even when setup or an assertion throws, then verify the restore independently below.
    $null = Sql "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; UPDATE GRID_CAPACITY_RESERVATIONS SET EXPIRES_AT = $expRestore WHERE OFFER_ID = $o8;"
  }
  $expRestored = Sql "SELECT CONVERT(varchar(33), EXPIRES_AT, 126) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $o8;"
  if ($expRestored -ne $expOriginal) { throw "F1 fixture restore failed: expected '$expOriginal', got '$expRestored'" }

  # F2: rejection is allowed while unsigned; approval still is not
  $L9 = Create-Land "S6-L9-$stamp" "S6P-L9-$stamp" $grid $laTok $admin
  $o9 = Accept-Approve $L9 $i1Tok
  $r = Call 'POST' '/api/contracts' @{ offerId = $o9 } $admin
  if ($r.Status -ne 201) { throw "F2 setup: contract create failed ($($r.Status))" }
  $c9 = [int](($r.Body | ConvertFrom-Json).id)
  $r = Call 'POST' "/api/contracts/$c9/review" @{ approve = $true } $admin
  Check 'S6-106' 'F2: approve with no signatures -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c9/review" @{ approve = $false } $admin
  Check 'S6-107' 'F2: unsigned reject without reason -> 400' 400 $r.Status
  $r = Call 'POST' "/api/contracts/$c9/review" @{ approve = $false; reason = 'Unsigned rejection' } $admin
  Check 'S6-108' 'F2: unsigned reject with reason -> 200' 200 $r.Status
  Check 'S6-109' 'F2: contract Rejected' 'Rejected' ([string](($r.Body | ConvertFrom-Json).status))
  $relC9 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c9 AND RELEASED_AT IS NOT NULL;"
  Check 'S6-110' 'F2: reservation released on unsigned rejection' 1 $relC9
  $landC9 = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L9;"
  Check 'S6-111' 'F2: land back to Verified(3)' 3 $landC9
  $histC9 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM LAND_STATUS_HISTORY WHERE LAND_ID = $L9 AND STATUS_ID = 3;"
  Check 'S6-112' 'F2: land history row created' 'True' ([string]([int]$histC9 -ge 1))
  $r = Call 'POST' "/api/contracts/$c9/review" @{ approve = $true } $admin
  Check 'S6-113' 'F2: review of a non-PendingSignatures contract -> 409' 409 $r.Status

  # ================= Contract revision workflow =================
  Write-Output ''
  Write-Output '== Contract revision workflow =='

  function Get-Contract-Dto([int]$ContractId) {
    $r = Call 'GET' "/api/contracts/$ContractId" $null $admin
    if ($r.Status -ne 200) { throw "read contract $ContractId failed ($($r.Status))" }
    return ($r.Body | ConvertFrom-Json)
  }
  function Get-Drafts([int]$ContractId) {
    $r = Call 'GET' "/api/contracts/$ContractId/versions" $null $admin
    if ($r.Status -ne 200) { throw "read contract versions $ContractId failed ($($r.Status))" }
    return @(($r.Body | ConvertFrom-Json) | Where-Object { $_.versionKind -eq 'Draft' } | Sort-Object versionNumber)
  }
  function Get-Draft([int]$ContractId, [int]$VersionNumber) {
    $found = @(Get-Drafts $ContractId | Where-Object { [int]$_.versionNumber -eq $VersionNumber })
    if ($found.Count -eq 0) { throw "draft v$VersionNumber for contract $ContractId not found" }
    return $found[0]
  }
  function Get-Revision-List([int]$ContractId) {
    $r = Call 'GET' "/api/contracts/$ContractId/revisions" $null $admin
    if ($r.Status -ne 200) { throw "read contract revisions $ContractId failed ($($r.Status))" }
    return @($r.Body | ConvertFrom-Json)
  }
  function Get-Revision-By-Id([int]$ContractId, [int]$RevisionId) {
    $found = @(Get-Revision-List $ContractId | Where-Object { [int]$_.id -eq $RevisionId })
    if ($found.Count -eq 0) { throw "revision $RevisionId for contract $ContractId not found" }
    return $found[0]
  }
  function New-Revision-Body(
    [int]$DraftId,
    [string]$Reason,
    [decimal]$LandlordSharePct,
    [int]$DurationYears,
    [string]$StartDate,
    [decimal]$InstallationCost,
    [decimal]$SolarCellCapacityKw,
    [decimal]$ExpectedAnnualRevenue,
    [decimal]$EffectiveCostPerKw,
    [decimal]$PaybackPeriodMonths,
    [int]$NoticePeriodDays) {
    return @{
      expectedDraftVersionId = $DraftId
      reason = $Reason
      landlordSharePct = $LandlordSharePct
      durationYears = $DurationYears
      startDate = $StartDate
      installationCost = $InstallationCost
      solarCellCapacityKw = $SolarCellCapacityKw
      expectedAnnualRevenue = $ExpectedAnnualRevenue
      effectiveCostPerKw = $EffectiveCostPerKw
      paybackPeriodMonths = $PaybackPeriodMonths
      noticePeriodDays = $NoticePeriodDays
    }
  }
  function New-Revision-Contract([string]$Tag, [switch]$WithCompetitor) {
    $land = Create-Land "S6-R$Tag-$stamp" "S6P-R$Tag-$stamp" $grid $laTok $admin
    $competitor = 0
    if ($WithCompetitor) {
      $r = Call 'POST' '/api/offers' @{ landId = $land; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 24 } $i2Tok
      if ($r.Status -ne 201) { throw "revision fixture competitor $Tag failed ($($r.Status))" }
      $competitor = [int](($r.Body | ConvertFrom-Json).id)
    }
    $offer = Accept-Approve $land $i1Tok
    $r = Call 'POST' '/api/contracts' @{ offerId = $offer } $admin
    if ($r.Status -ne 201) { throw "revision fixture contract $Tag failed ($($r.Status)) body=$($r.Body)" }
    return [pscustomobject]@{
      Land = [int]$land
      Offer = [int]$offer
      Contract = [int](($r.Body | ConvertFrom-Json).id)
      Competitor = [int]$competitor
    }
  }

  # --- Draft v1 -> v2 with a real signature invalidated by the approved revision ---
  $f10 = New-Revision-Contract '10'
  $c10 = [int]$f10.Contract; $L10 = [int]$f10.Land; $o10 = [int]$f10.Offer
  $v1 = Get-Draft $c10 1
  $dto10 = Get-Contract-Dto $c10
  $oldAcceptedVersion = [int]$dto10.offerVersionId
  $oldNotice = [int]$dto10.noticePeriodDays
  $share1 = [decimal]$v1.terms.landlordSharePct
  $duration1 = [int]$v1.terms.durationYears
  $r = Call 'POST' "/api/contracts/$c10/sign" $null $i1Tok
  Check 'S6-120' 'revision fixture: investor signs v1 before revision' 200 $r.Status
  $sigBefore = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c10 AND EVENT_TYPE = 'Signed';"
  $res10 = Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c10 AND RELEASED_AT IS NULL;"
  $resCount10 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c10;"
  $ovCount10Before = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE OFFER_ID = $o10;"
  $offerHist10Before = [int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $o10 AND ACTION_TYPE = 4;")

  $revBody10 = New-Revision-Body -DraftId ([int]$v1.id) -Reason 'R10: revised v2 terms' `
    -LandlordSharePct ($share1 + 1) -DurationYears ($duration1 + 1) -StartDate '2027-01-01' `
    -InstallationCost 1000 -SolarCellCapacityKw 2 -ExpectedAnnualRevenue 5000 `
    -EffectiveCostPerKw 0.5 -PaybackPeriodMonths 36 -NoticePeriodDays ($oldNotice + 30)

  $r = Call 'GET' "/api/contracts/$c10/revisions"
  Check 'S6-121' 'revisions anonymous -> 401' 401 $r.Status
  $r = Call 'GET' "/api/contracts/$c10/revisions" $null $i2Tok
  Check 'S6-122' 'revisions third-party -> 403' 403 $r.Status
  $r = Call 'GET' "/api/contracts/$c10/revisions" $null $adminTok
  Check 'S6-123' 'revisions regular Admin readable -> 200' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody10
  Check 'S6-124a' 'anonymous revision request -> 401' 401 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody10 $adminTok
  Check 'S6-124' 'regular Admin cannot request revision -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody10 $i2Tok
  Check 'S6-125' 'third-party investor cannot request revision -> 403' 403 $r.Status

  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody10 $laTok
  Check 'S6-126' 'contract party requests revision -> 201' 201 $r.Status
  $rev1 = $r.Body | ConvertFrom-Json
  $rev1Id = [int]$rev1.id
  Check 'S6-127' 'revision starts PendingPartyConsent' 'PendingPartyConsent' ([string]$rev1.status)
  Check 'S6-127a' 'proposal snapshots proposer identity and UTC' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE ID = $rev1Id AND PROPOSED_BY_ID = $laId AND PROPOSED_BY_NAME_SNAPSHOT IS NOT NULL AND PROPOSED_AT IS NOT NULL;")
  Check 'S6-128' 'revision records the exact base draft v1' 1 ([int]$rev1.baseDraftVersionNumber)
  Check 'S6-129' 'proposal does not create a new draft yet' 1 (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c10 AND VERSION_KIND = 'Draft';")
  Check 'S6-130' 'proposal does not create a new offer version yet' $ovCount10Before (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE OFFER_ID = $o10;")
  Check 'S6-131' 'RevisionRequested event targets v1' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c10 AND EVENT_TYPE = 'RevisionRequested' AND TARGET_VERSION_ID = $([int]$v1.id);")
  Check 'S6-132' 'proposal leaves the accepted offer version unchanged' $oldAcceptedVersion (Sql "SELECT CAST(OFFER_VERSION_ID AS varchar(10)) FROM CONTRACTS WHERE ID = $c10;")
  Check 'S6-133' 'proposal leaves the previous signature intact' 'True' ([string]((Get-Contract-Dto $c10).investorSignedAt -ne $null))
  $r = Call 'POST' "/api/contracts/$c10/sign" $null $laTok
  Check 'S6-134' 'signing is blocked while a revision is open -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/review" @{ approve = $true } $adminTok
  Check 'S6-135' 'contract review is blocked while a revision is open -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/admin-decision" @{ expectedDraftVersionId = [int]$v1.id; decision = 'Approve' } $adminTok
  Check 'S6-136' 'Admin cannot decide before both party consents -> 409' 409 $r.Status

  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/consent" @{ expectedDraftVersionId = [int]$v1.id; approved = $true } $adminTok
  Check 'S6-137' 'Admin cannot give party consent -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/consent" @{ expectedDraftVersionId = [int]$v1.id; approved = $true } $i2Tok
  Check 'S6-138' 'third-party investor cannot give consent -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/consent" @{ expectedDraftVersionId = [int]$v1.id; approved = $true } $laTok
  Check 'S6-139' 'proposing landlord explicitly approves -> 200' 200 $r.Status
  $revAfterOne = $r.Body | ConvertFrom-Json
  Check 'S6-140' 'one consent keeps revision pending party consent' 'PendingPartyConsent' ([string]$revAfterOne.status)
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/consent" @{ expectedDraftVersionId = [int]$v1.id; approved = $true } $laTok
  Check 'S6-141' 'same party cannot consent twice -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/consent" @{ expectedDraftVersionId = [int]$v1.id; approved = $true } $i1Tok
  Check 'S6-142' 'second party approves -> 200' 200 $r.Status
  $revAfterTwo = $r.Body | ConvertFrom-Json
  Check 'S6-143' 'both consents advance to PendingAdminReview' 'PendingAdminReview' ([string]$revAfterTwo.status)
  Check 'S6-144' 'two explicit consent rows persisted' 2 ([int]@($revAfterTwo.consents).Count)
  Check 'S6-144a' 'consents persist party, decision and UTC' '2' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISION_CONSENTS WHERE REVISION_ID = $rev1Id AND DECISION = 'Approved' AND CREATED_AT IS NOT NULL;")

  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/admin-decision" @{ expectedDraftVersionId = 2147000000; decision = 'Approve' } $adminTok
  Check 'S6-145' 'stale draft id at Admin decision -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/admin-decision" @{ expectedDraftVersionId = [int]$v1.id; decision = 'Approve' } $i1Tok
  Check 'S6-146' 'party cannot make the Admin decision -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev1Id/admin-decision" @{ expectedDraftVersionId = [int]$v1.id; decision = 'Approve' } $adminTok
  Check 'S6-147' 'regular Admin approves the revision -> 200' 200 $r.Status
  $revApproved = $r.Body | ConvertFrom-Json
  Check 'S6-148' 'revision Approved after Admin decision' 'Approved' ([string]$revApproved.status)
  Check 'S6-149' 'Admin approval creates Draft v2' 2 ([int]$revApproved.newDraftVersionNumber)
  $v2 = Get-Draft $c10 2
  Check 'S6-150' 'Draft v2 points to v1 as its previous draft' ([int]$v1.id) ([int]$v2.previousVersionId)
  Check 'S6-151' 'Draft v2 carries the revision reason' 'R10: revised v2 terms' ([string]$v2.reason)
  Check 'S6-152' 'Draft v2 contains the proposed landlord share' ([decimal]($share1 + 1)).ToString('0.####') ([decimal]$v2.terms.landlordSharePct).ToString('0.####')
  $dto10V2 = Get-Contract-Dto $c10
  Check 'S6-153' 'accepted offer version advances to the new version' ([int]$revApproved.newOfferVersionId) ([int]$dto10V2.offerVersionId)
  Check 'S6-154' 'contract notice period advances with the new terms' ($oldNotice + 30) ([int]$dto10V2.noticePeriodDays)
  Check 'S6-155' 'signature does not carry into Draft v2' 'True' ([string]($dto10V2.investorSignedAt -eq $null))
  Check 'S6-156' 'old signature event remains in history' $sigBefore (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c10 AND EVENT_TYPE = 'Signed';")
  Check 'S6-157' 'Draft v1 terms remain unchanged' ([decimal]$v1.terms.landlordSharePct).ToString('0.####') ([decimal](Get-Draft $c10 1).terms.landlordSharePct).ToString('0.####')
  Check 'S6-158' 'DraftSuperseded event targets v1' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c10 AND EVENT_TYPE = 'DraftSuperseded' AND TARGET_VERSION_ID = $([int]$v1.id);")
  Check 'S6-159' 'Admin revision review row recorded' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c10 AND DECISION = 'RevisionApproved';")
  Check 'S6-159a' 'Admin decision records actor and UTC' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE ID = $rev1Id AND ADMIN_REVIEWED_BY_ID IS NOT NULL AND ADMIN_REVIEWED_AT IS NOT NULL AND ADMIN_DECISION = 'Approved';")
  Check 'S6-159b' 'revision approval keeps contract pending and unreviewed' 'True' ([string](($dto10V2.status -eq 'PendingSignatures') -and ($dto10V2.adminReviewedAt -eq $null)))
  Check 'S6-160' 'new accepted offer version is current' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE ID = $([int]$revApproved.newOfferVersionId) AND IS_CURRENT = 1;")
  Check 'S6-161' 'old accepted offer version is preserved and not current' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE ID = $oldAcceptedVersion AND IS_CURRENT = 0;")
  Check 'S6-162' 'ApprovedByAdmin offer history increments once' ([string]($offerHist10Before + 1)) (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $o10 AND ACTION_TYPE = 4;")
  Check 'S6-163' 'reservation id is unchanged by the revision' $res10 (Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c10 AND RELEASED_AT IS NULL;")
  Check 'S6-164' 'no duplicate reservation after the revision' $resCount10 (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c10;")

  # --- Draft v2 -> v3, then fully-signed protection ---
  $dto10V2 = Get-Contract-Dto $c10
  $share2 = [decimal]$dto10V2.landlordSharePct
  $duration2 = [int]$dto10V2.durationYears
  $notice2 = [int]$dto10V2.noticePeriodDays
  $revBody11 = New-Revision-Body -DraftId ([int]$v2.id) -Reason 'R10: revised v3 terms' `
    -LandlordSharePct ($share2 + 1) -DurationYears ($duration2 + 1) -StartDate '2027-06-01' `
    -InstallationCost 1200 -SolarCellCapacityKw 3 -ExpectedAnnualRevenue 6000 `
    -EffectiveCostPerKw 0.6 -PaybackPeriodMonths 42 -NoticePeriodDays ($notice2 + 15)
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody11 $i1Tok
  Check 'S6-165' 'party requests Draft v3 -> 201' 201 $r.Status
  $rev2 = $r.Body | ConvertFrom-Json
  $rev2Id = [int]$rev2.id
  Check 'S6-166' 'Draft v3 request is based on v2' 2 ([int]$rev2.baseDraftVersionNumber)
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev2Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v2.id; decision = 'Continue'; reason = 'No consent yet' } $admin
  Check 'S6-167' 'SuperAdmin cannot decide a non-escalated revision -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev2Id/consent" @{ expectedDraftVersionId = [int]$v2.id; approved = $true } $i1Tok
  Check 'S6-168' 'investor approves Draft v3 terms' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev2Id/consent" @{ expectedDraftVersionId = [int]$v2.id; approved = $true } $laTok
  Check 'S6-169' 'landlord approves Draft v3 terms' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions/$rev2Id/admin-decision" @{ expectedDraftVersionId = [int]$v2.id; decision = 'Approve' } $adminTok
  Check 'S6-170' 'Admin approves Draft v3 -> 200' 200 $r.Status
  $rev3Approved = $r.Body | ConvertFrom-Json
  Check 'S6-171' 'Admin approval creates Draft v3' 3 ([int]$rev3Approved.newDraftVersionNumber)
  $v3 = Get-Draft $c10 3
  Check 'S6-172' 'Draft v3 points to v2' ([int]$v2.id) ([int]$v3.previousVersionId)
  Check 'S6-173' 'Draft v3 carries its own reason' 'R10: revised v3 terms' ([string]$v3.reason)
  Check 'S6-174' 'Draft v3 contains the second revision share' ([decimal]($share2 + 1)).ToString('0.####') ([decimal]$v3.terms.landlordSharePct).ToString('0.####')
  Check 'S6-175' 'Draft v2 remains unchanged' 'R10: revised v2 terms' ([string](Get-Draft $c10 2).reason)
  $noOpBody = $revBody11.Clone()
  $noOpBody['expectedDraftVersionId'] = [int]$v3.id
  $r = Call 'POST' "/api/contracts/$c10/revisions" $noOpBody $i1Tok
  Check 'S6-176' 'no-op revision terms are refused -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/sign" $null $i1Tok
  Check 'S6-177' 'investor signs Draft v3' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/sign" $null $laTok
  Check 'S6-178' 'landlord signs Draft v3' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody11 $i1Tok
  Check 'S6-179' 'revision after both signatures -> 409' 409 $r.Status
  Check 'S6-180' 'fully-signed revision refusal is explicit' 'Fully signed contracts cannot be revised; use the amendment workflow.' ([string](($r.Body | ConvertFrom-Json).detail))
  $r = Call 'POST' "/api/contracts/$c10/review" @{ approve = $true } $adminTok
  Check 'S6-181' 'final contract approval -> 200' 200 $r.Status
  Check 'S6-182' 'contract becomes Active after final review' 'Active' ([string](($r.Body | ConvertFrom-Json).status))
  $r = Call 'POST' "/api/contracts/$c10/revisions" $revBody11 $i1Tok
  Check 'S6-183' 'revision after finalisation -> 409' 409 $r.Status

  # --- Admin escalation, regular-Admin restriction, SuperAdmin continuation ---
  $f11 = New-Revision-Contract '11' -WithCompetitor
  $c11 = [int]$f11.Contract; $L11 = [int]$f11.Land; $o11 = [int]$f11.Offer
  $comp11Status = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $([int]$f11.Competitor);"
  $v11 = Get-Draft $c11 1
  $dto11 = Get-Contract-Dto $c11
  $revBody12 = New-Revision-Body -DraftId ([int]$v11.id) -Reason 'R11: escalation path' `
    -LandlordSharePct ([decimal]$dto11.landlordSharePct + 2) -DurationYears ([int]$dto11.durationYears + 2) -StartDate '2027-02-01' `
    -InstallationCost 2000 -SolarCellCapacityKw 4 -ExpectedAnnualRevenue 7000 `
    -EffectiveCostPerKw 0.7 -PaybackPeriodMonths 48 -NoticePeriodDays ([int]$dto11.noticePeriodDays + 20)
  $r = Call 'POST' "/api/contracts/$c11/revisions" $revBody12 $i1Tok
  Check 'S6-184' 'escalation fixture revision requested -> 201' 201 $r.Status
  $rev11 = $r.Body | ConvertFrom-Json
  $rev11Id = [int]$rev11.id
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/consent" @{ expectedDraftVersionId = [int]$v11.id; approved = $true } $i1Tok
  Check 'S6-185' 'one consent is not enough for escalation' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/admin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Escalate'; reason = 'Needs SuperAdmin review' } $adminTok
  Check 'S6-186' 'Admin escalation before both consents -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/consent" @{ expectedDraftVersionId = [int]$v11.id; approved = $true } $laTok
  Check 'S6-187' 'second party approves escalation fixture' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/admin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Escalate'; reason = '' } $adminTok
  Check 'S6-187a' 'Admin escalation requires a reason -> 400' 400 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/admin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Escalate'; reason = 'Needs SuperAdmin review' } $adminTok
  Check 'S6-188' 'regular Admin escalates -> 200' 200 $r.Status
  $rev11Escalated = $r.Body | ConvertFrom-Json
  Check 'S6-189' 'revision waits for SuperAdmin' 'PendingSuperAdminReview' ([string]$rev11Escalated.status)
  Check 'S6-190' 'escalation reason is recorded' 'Needs SuperAdmin review' ([string]$rev11Escalated.escalationReason)
  Check 'S6-190a' 'Admin escalation records actor and UTC' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE ID = $rev11Id AND ADMIN_REVIEWED_BY_ID IS NOT NULL AND ADMIN_REVIEWED_AT IS NOT NULL AND ADMIN_DECISION = 'Escalated';")
  Check 'S6-190b' 'competing offer stays frozen during escalation' $comp11Status (Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $([int]$f11.Competitor);")
  Check 'S6-191' 'escalation leaves no new draft' 1 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c11 AND VERSION_KIND = 'Draft';"))
  Check 'S6-192' 'escalation leaves reservation active' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c11 AND RELEASED_AT IS NULL;")
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Continue'; reason = 'Attempt by Admin' } $adminTok
  Check 'S6-193' 'regular Admin cannot use SuperAdmin decision -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Continue'; reason = 'Attempt by party' } $i1Tok
  Check 'S6-194' 'party cannot use SuperAdmin decision -> 403' 403 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Continue'; reason = '' } $admin
  Check 'S6-195' 'SuperAdmin continuation requires a reason -> 400' 400 $r.Status
  $r = Call 'POST' "/api/contracts/$c11/revisions/$rev11Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v11.id; decision = 'Continue'; reason = 'Continue after escalation review' } $admin
  Check 'S6-196' 'SuperAdmin continues the revision -> 200' 200 $r.Status
  $rev11Continued = $r.Body | ConvertFrom-Json
  Check 'S6-197' 'continued revision is Approved' 'Approved' ([string]$rev11Continued.status)
  Check 'S6-198' 'continuation creates Draft v2' 2 ([int]$rev11Continued.newDraftVersionNumber)
  Check 'S6-199' 'continuation keeps the reservation active and unique' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c11 AND RELEASED_AT IS NULL;")
  Check 'S6-200' 'escalation review row recorded' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c11 AND DECISION = 'RevisionEscalated';")
  Check 'S6-201' 'continuation review row recorded' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c11 AND DECISION = 'RevisionContinued';")
  Check 'S6-201a' 'continuation records SuperAdmin actor, decision and UTC' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE ID = $rev11Id AND SUPERADMIN_DECIDED_BY_ID IS NOT NULL AND SUPERADMIN_DECIDED_AT IS NOT NULL AND SUPERADMIN_DECISION = 'Continued' AND SUPERADMIN_REASON = N'Continue after escalation review';")
  Check 'S6-201b' 'both party consents remained the basis of continuation' '2' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISION_CONSENTS WHERE REVISION_ID = $rev11Id AND DECISION = 'Approved';")
  Check 'S6-201c' 'competing offer stays frozen after continuation' $comp11Status (Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $([int]$f11.Competitor);")

  # --- SuperAdmin cancellation uses the established rejection effects and preserves history ---
  $f12 = New-Revision-Contract '12' -WithCompetitor
  $c12 = [int]$f12.Contract; $L12 = [int]$f12.Land; $o12 = [int]$f12.Offer
  $comp12Status = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $([int]$f12.Competitor);"
  $v12 = Get-Draft $c12 1
  $dto12 = Get-Contract-Dto $c12
  $revBody13 = New-Revision-Body -DraftId ([int]$v12.id) -Reason 'R12: cancellation path' `
    -LandlordSharePct ([decimal]$dto12.landlordSharePct + 3) -DurationYears ([int]$dto12.durationYears + 3) -StartDate '2027-03-01' `
    -InstallationCost 3000 -SolarCellCapacityKw 5 -ExpectedAnnualRevenue 8000 `
    -EffectiveCostPerKw 0.8 -PaybackPeriodMonths 54 -NoticePeriodDays ([int]$dto12.noticePeriodDays + 25)
  $r = Call 'POST' "/api/contracts/$c12/revisions" $revBody13 $i1Tok
  Check 'S6-202' 'cancellation fixture revision requested -> 201' 201 $r.Status
  $rev12 = $r.Body | ConvertFrom-Json
  $rev12Id = [int]$rev12.id
  $null = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/consent" @{ expectedDraftVersionId = [int]$v12.id; approved = $true } $i1Tok
  $null = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/consent" @{ expectedDraftVersionId = [int]$v12.id; approved = $true } $laTok
  $r = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/admin-decision" @{ expectedDraftVersionId = [int]$v12.id; decision = 'Escalate'; reason = 'Cancellation review' } $adminTok
  Check 'S6-203' 'cancellation fixture escalates -> 200' 200 $r.Status
  $res12 = Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c12;"
  $ovCount12 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE OFFER_ID = $o12;"
  $draftCount12 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c12 AND VERSION_KIND = 'Draft';"
  $accepted12 = Sql "SELECT CAST(ACCEPTED_VERSION_ID AS varchar(10)) FROM OFFERS WHERE ID = $o12;"
  $r = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v12.id; decision = 'Cancel'; reason = '' } $admin
  Check 'S6-204' 'cancellation requires a reason -> 400' 400 $r.Status
  Check 'S6-205' 'refused cancellation leaves contract pending' 'PendingSignatures' ([string](Get-Contract-Dto $c12).status)
  Check 'S6-206' 'refused cancellation leaves reservation active' '0' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c12 AND RELEASED_AT IS NOT NULL;")
  $r = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v12.id; decision = 'Cancel'; reason = 'SuperAdmin cancelled the pre-signing contract' } $admin
  Check 'S6-207' 'SuperAdmin cancels the revision -> 200' 200 $r.Status
  $rev12Cancelled = $r.Body | ConvertFrom-Json
  Check 'S6-208' 'revision is Cancelled' 'Cancelled' ([string]$rev12Cancelled.status)
  Check 'S6-209' 'contract is Cancelled' 'Cancelled' ([string](Get-Contract-Dto $c12).status)
  Check 'S6-210' 'cancellation creates no new offer version' $ovCount12 (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE OFFER_ID = $o12;")
  Check 'S6-211' 'cancellation creates no new draft' $draftCount12 (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c12 AND VERSION_KIND = 'Draft';")
  Check 'S6-212' 'cancellation releases the same reservation' $res12 (Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c12 AND RELEASED_AT IS NOT NULL;")
  Check 'S6-213' 'cancellation returns land to Verified(3)' '3' (Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L12;")
  Check 'S6-214' 'cancellation keeps the offer closed (9)' '9' (Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $o12;")
  Check 'S6-215' 'cancellation preserves Draft v1' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c12 AND VERSION_NUMBER = 1 AND VERSION_KIND = 'Draft';")
  Check 'S6-216' 'cancellation review row recorded' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVIEWS WHERE CONTRACT_ID = $c12 AND DECISION = 'RevisionCancelled';")
  Check 'S6-217' 'cancellation event targets Draft v1' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_VERSIONS WHERE CONTRACT_ID = $c12 AND EVENT_TYPE = 'RevisionCancelled' AND TARGET_VERSION_ID = $([int]$v12.id);")
  Check 'S6-218' 'cancellation records the SuperAdmin reason' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE ID = $rev12Id AND SUPERADMIN_REASON = N'SuperAdmin cancelled the pre-signing contract' AND SUPERADMIN_DECIDED_AT IS NOT NULL AND CLOSED_AT IS NOT NULL;")
  Check 'S6-218a' 'cancellation preserves the accepted offer version' $accepted12 (Sql "SELECT CAST(ACCEPTED_VERSION_ID AS varchar(10)) FROM OFFERS WHERE ID = $o12;")
  Check 'S6-218b' 'cancellation preserves both consent rows' '2' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISION_CONSENTS WHERE REVISION_ID = $rev12Id AND DECISION = 'Approved';")
  Check 'S6-218c' 'competing offer is not reopened by cancellation' $comp12Status (Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $([int]$f12.Competitor);")
  Check 'S6-218d' 'no competitor Reopened history is fabricated' '0' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $([int]$f12.Competitor) AND ACTION_TYPE = 7;")
  $r = Call 'POST' "/api/contracts/$c12/revisions/$rev12Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v12.id; decision = 'Cancel'; reason = 'Repeat' } $admin
  Check 'S6-219' 'repeated cancellation decision -> 409' 409 $r.Status

  # --- Stale/concurrent protection: one open revision and one consent per party ---
  $f13 = New-Revision-Contract '13'
  $c13 = [int]$f13.Contract; $L13 = [int]$f13.Land; $o13 = [int]$f13.Offer
  $v13 = Get-Draft $c13 1
  $dto13 = Get-Contract-Dto $c13
  $raceBody13 = New-Revision-Body -DraftId ([int]$v13.id) -Reason 'R13: concurrent proposal' `
    -LandlordSharePct ([decimal]$dto13.landlordSharePct + 4) -DurationYears ([int]$dto13.durationYears + 4) -StartDate '2027-04-01' `
    -InstallationCost 4000 -SolarCellCapacityKw 6 -ExpectedAnnualRevenue 9000 `
    -EffectiveCostPerKw 0.9 -PaybackPeriodMonths 60 -NoticePeriodDays ([int]$dto13.noticePeriodDays + 35)
  $raceJson13 = ConvertTo-Json -InputObject $raceBody13 -Depth 6 -Compress
  $propRace = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = "/api/contracts/$c13/revisions"; Json = $raceJson13; BearerToken = $i1Tok },
    @{ Method = 'POST'; Path = "/api/contracts/$c13/revisions"; Json = $raceJson13; BearerToken = $i1Tok }
  ) -TimeoutSeconds 60
  $propStatuses = (@([int]$propRace[0].Status, [int]$propRace[1].Status) | Sort-Object) -join ' '
  Check 'S6-220' 'concurrent proposals: exactly one 201, one 409' '201 409' $propStatuses
  Check 'S6-221' 'exactly one open revision persisted' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISIONS WHERE CONTRACT_ID = $c13 AND CLOSED_AT IS NULL;")
  $rev13 = @(Get-Revision-List $c13)[0]
  $rev13Id = [int]$rev13.id
  $consentJson13 = ConvertTo-Json -InputObject @{ expectedDraftVersionId = [int]$v13.id; approved = $true } -Compress
  $consentRace = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = "/api/contracts/$c13/revisions/$rev13Id/consent"; Json = $consentJson13; BearerToken = $i1Tok },
    @{ Method = 'POST'; Path = "/api/contracts/$c13/revisions/$rev13Id/consent"; Json = $consentJson13; BearerToken = $i1Tok }
  ) -TimeoutSeconds 60
  $consentStatuses = (@([int]$consentRace[0].Status, [int]$consentRace[1].Status) | Sort-Object) -join ' '
  Check 'S6-222' 'concurrent same-party consents: exactly one 200, one 409' '200 409' $consentStatuses
  Check 'S6-223' 'exactly one consent row persisted for the party' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACT_REVISION_CONSENTS WHERE REVISION_ID = $rev13Id AND PARTY_USER_ID = $i1Id;")
  $r = Call 'POST' "/api/contracts/$c13/revisions/$rev13Id/superadmin-decision" @{ expectedDraftVersionId = [int]$v13.id; decision = 'Continue'; reason = 'Attempt without both consents' } $admin
  Check 'S6-223a' 'SuperAdmin cannot override a missing party consent -> 409' 409 $r.Status
  $r = Call 'POST' "/api/contracts/$c13/revisions" $raceBody13 $i1Tok
  Check 'S6-224' 'second revision while one is open -> 409' 409 $r.Status
  Check 'S6-225' 'revision workflow created no duplicate reservation' '1' (Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE CONTRACT_ID = $c13;")

  $pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
  $fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
  Write-Output ''
  Write-Output ("S6 TOTAL PASS=$pass FAIL=$fail")
}
finally {
  Complete-TestApiCleanup -Identity $app
}
if ($fail -gt 0) { exit 1 }
