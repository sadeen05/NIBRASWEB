# Step 6 - Contracts MVP acceptance suite (never prints credentials or tokens).
# Starts the app once with RateLimit__AuthPerMinute=1000, then drives the contract lifecycle
# through the public API and asserts database state. Target database: NEBRASDB_Step1 only.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$base = 'http://localhost:5181'
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
$sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { '.\SQLEXPRESS' }
$db = 'NEBRASDB_Step1'
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
  $r = Call 'POST' '/api/auth/register' $body
  if ($r.Status -ne 200) { throw "register failed ($($r.Status))" }
  return [int](($r.Body | ConvertFrom-Json).id)
}
function Verify-Land([int]$landId, [string]$laTok, [string]$adminTok) {
  $docIds = @()
  foreach ($dt in 1..2) {
    $r = Call 'POST' "/api/lands/$landId/documents" @{ documentTypeId = $dt; filePath = 'docs/titledeed-001.pdf' } $laTok
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
function New-Land-Body([string]$ln, [string]$parcel, [int]$gid) {
  @{ regionId = $fx.PrimaryRegionId; gridId = $gid; landNumber = $ln; village = 'V-S6'; basin = 'B-S6';
     parcelNumber = $parcel; areaDonum = 100; slopePercentage = 5; distanceToGridKm = 10;
     solarIrradiance = 6; elevationM = 100 }
}
function Create-Land([string]$ln, [string]$parcel, [int]$gid, [string]$laTok, [string]$adminTok) {
  $r = Call 'POST' '/api/lands' (New-Land-Body $ln $parcel $gid) $laTok
  if ($r.Status -ne 201) { throw "land create failed: $($r.Status)" }
  $landId = [int](($r.Body | ConvertFrom-Json).id)
  Verify-Land $landId $laTok $adminTok
  return $landId
}

# --- target verification BEFORE any database access ---
Write-Output "== S6 setup ($stamp) - credentials never printed =="
$target = Sql "SELECT DB_NAME();"
if ($target -ne $db) { Write-Output "BLOCKED: expected $db but connected to '$target'"; exit 2 }
Check 'S6-00' 'database target is NEBRASDB_Step1' $db $target

# --- start the app once for this run ---
. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = Start-S4App -Port 5181 -AuthPerMinute 1000

$pass = 0; $fail = 0
try {
  # --- seed SuperAdmin credentials: read at run time via `dotnet user-secrets list`, never printed ---
  $repoRoot = $PSScriptRoot
  while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
  if (-not $repoRoot) { Write-Output 'BLOCKED: NibrasWeb.csproj not found above the script directory'; exit 2 }
  $sec = @{}
  foreach ($line in @(& dotnet user-secrets list --project (Join-Path $repoRoot 'NibrasWeb.csproj') 2>$null)) {
    if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') { $sec[$Matches[1].Trim()] = $Matches[2] }
  }
  $adminEmail = $sec['Seed:SuperAdminEmail']; $adminPass = $sec['Seed:SuperAdminPassword']
  $sec = $null
  if (-not $adminEmail -or -not $adminPass) { Write-Output 'BLOCKED: Seed:SuperAdmin* not configured in user-secrets'; exit 2 }

  $admin = Login $adminEmail $adminPass
  Check 'S6-01' 'seeded SuperAdmin login' 200 200

  # --- disposable users ---
  $laEmail = "s6-$stamp-la@example.test"; $lbEmail = "s6-$stamp-lb@example.test"
  $i1Email = "s6-$stamp-i1@example.test"; $i2Email = "s6-$stamp-i2@example.test"
  $laId = Reg 'S6 Landlord A' $laEmail 1; $laTok = Login $laEmail $pw
  $lbId = Reg 'S6 Landlord B' $lbEmail 1; $lbTok = Login $lbEmail $pw
  $i1Id = Reg 'S6 Investor 1' $i1Email 2; $r = Call 'POST' "/api/users/$i1Id/approve" $null $admin; if ($r.Status -ne 204) { throw "approve i1 failed" }; $i1Tok = Login $i1Email $pw
  $i2Id = Reg 'S6 Investor 2' $i2Email 2; $r = Call 'POST' "/api/users/$i2Id/approve" $null $admin; if ($r.Status -ne 204) { throw "approve i2 failed" }; $i2Tok = Login $i2Email $pw
  Write-Output "users: la=$laId lb=$lbId i1=$i1Id i2=$i2Id"

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
  $r = Call 'GET' '/api/contracts?pageNumber=1&pageSize=50' $null $admin
  $admHas = ($r.Body -match ('"id":' + $c1 + '(?![0-9])'))
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
  $r = Call 'POST' '/api/criteria' @{ minAreaDonum = 20; maxSlopePct = 8; maxGridDistanceKm = 15; minSolarIrradiance = 5; minElevationM = $null; reason = 'S6 stricter criterion' } $admin
  Check 'S6-80' 'admin adds a stricter criterion -> 201' 201 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/sign" $null $laTok
  Check 'S6-81' 'C3 landlord signs after criterion change' 200 $r.Status
  $r = Call 'POST' "/api/contracts/$c3/review" @{ approve = $true } $admin
  Check 'S6-82' 'review of land verified under older criterion still succeeds -> 200' 200 $r.Status
  Check 'S6-83' 'C3 becomes Active (no re-verification during review)' 'Active' ([string](($r.Body | ConvertFrom-Json).status))

  # ================= M-1: concurrent operations on the same contract =================
  Write-Output ''
  Write-Output '== M-1: concurrent operations on the same contract =='
  $jobPost = {
    param($baseUrl, $token, $path, $body)
    $h = @{ Authorization = 'Bearer ' + $token }
    $p = @{ Uri = ($baseUrl + $path); Method = 'Post'; Headers = $h; UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($body) { $p.Body = $body; $p.ContentType = 'application/json' }
    try { $r = Invoke-WebRequest @p; return [int]$r.StatusCode }
    catch { $resp = $_.Exception.Response; if ($null -eq $resp) { throw }; return [int]$resp.StatusCode }
  }

  # C4: two concurrent investor signatures
  $L5 = Create-Land "S6-L5-$stamp" "S6P-L5-$stamp" $grid $laTok $admin
  $o5 = Accept-Approve $L5 $i1Tok
  $r = Call 'POST' '/api/contracts' @{ offerId = $o5 } $admin
  $c4 = [int](($r.Body | ConvertFrom-Json).id)
  $j1 = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $i1Tok, "/api/contracts/$c4/sign", $null
  $j2 = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $i1Tok, "/api/contracts/$c4/sign", $null
  $null = Wait-Job $j1, $j2 -Timeout 60
  $sg1 = [int](Receive-Job $j1); $sg2 = [int](Receive-Job $j2); Remove-Job $j1, $j2 -Force
  Check 'S6-90' 'concurrent same-party sign: exactly one 200, one 409' '200 409' ((@($sg1, $sg2) | Sort-Object) -join ' ')
  $signCount = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM CONTRACTS WHERE ID = $c4 AND INVESTOR_SIGNED_AT IS NOT NULL;"
  Check 'S6-91' 'exactly one investor signature persisted' 1 $signCount

  # C4: two concurrent reviews (after both parties signed)
  $r = Call 'POST' "/api/contracts/$c4/sign" $null $laTok
  Check 'S6-92' 'C4 landlord signs' 200 $r.Status
  $reviewBody = (ConvertTo-Json -InputObject @{ approve = $true } -Compress)
  $j1 = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/contracts/$c4/review", $reviewBody
  $j2 = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/contracts/$c4/review", $reviewBody
  $null = Wait-Job $j1, $j2 -Timeout 60
  $rv1 = [int](Receive-Job $j1); $rv2 = [int](Receive-Job $j2); Remove-Job $j1, $j2 -Force
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
  $jR = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/contracts/$c6/review", $reviewBody
  $jT = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/contracts/$c6/terminate", $termBody
  $null = Wait-Job $jR, $jT -Timeout 60
  $sR = [int](Receive-Job $jR); $sT = [int](Receive-Job $jT); Remove-Job $jR, $jT -Force
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
  # sqlcmd defaults QUOTED_IDENTIFIER/ANSI_NULLS OFF; this table requires them ON for DML.
  $null = Sql "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; UPDATE GRID_CAPACITY_RESERVATIONS SET EXPIRES_AT = DATEADD(day, -1, GETUTCDATE()) WHERE OFFER_ID = $o8;"
  $expSet = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $o8 AND EXPIRES_AT <= GETUTCDATE();"
  if ($expSet -ne '1') { throw "F1 setup: failed to expire the reservation (count=$expSet)" }
  $r = Call 'POST' '/api/contracts' @{ offerId = $o8 } $admin
  Check 'S6-104' 'F1: expired reservation -> 409' 409 $r.Status
  Check 'S6-105' 'F1: exact expiry message' 'The grid reservation for this offer has expired.' ([string](($r.Body | ConvertFrom-Json).detail))

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

  $pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
  $fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
  Write-Output ''
  Write-Output ("S6 TOTAL PASS=$pass FAIL=$fail")
} finally {
  Stop-S4App -App $app
}
if ($fail -gt 0) { exit 1 }
