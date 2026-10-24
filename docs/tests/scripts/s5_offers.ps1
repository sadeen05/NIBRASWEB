# Step 5 - offers acceptance suite (never prints credentials or tokens).
#
# The concurrency blocks use in-process concurrent HttpClient requests: no session JWT is
# passed to a PowerShell job or a child process.
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
$blocked = 0
$global:AuthCalls = 0

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
function Pace([int]$Seconds = 7) {
  # /api/auth is rate limited to 10 calls per minute per IP (RateLimit:AuthPerMinute).
  # Space auth calls so any 60-second window holds at most ~9 of them.
  Start-Sleep -Seconds $Seconds
}
function Auth([string]$Method, [string]$Path, $Body) {
  $global:AuthCalls++
  Pace
  return (Call $Method $Path $Body)
}
function Check([string]$Id, [string]$Desc, $Expected, $Actual) {
  $res = if ("$Expected" -eq "$Actual") { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Desc = $Desc; Expected = "$Expected"; Actual = "$Actual"; Result = $res })
  Write-Output ("{0,-8} {1,-4} exp={2,-16} act={3,-16} {4}" -f $Id, $res, $Expected, $Actual, $Desc)
}
function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}
function Login([string]$Email, [string]$Pass) {
  $r = Auth 'POST' '/api/auth/login' @{ email = $Email; password = $Pass }
  if ($r.Status -ne 200) { throw "login failed ($($r.Status)): $($r.Body)" }
  return ($r.Body | ConvertFrom-Json).token
}
function Reg([string]$FullName, [string]$Email, [int]$RoleId) {
  $body = @{ fullName = $FullName; email = $Email; phone = (New-Phone); password = $pw; roleId = $RoleId }
  if ($RoleId -eq 2) { $body.companyName = 'S5 Co'; $body.companyRegistrationNumber = "S5-REG-$stamp" }
  $baseline = New-OutboxBaseline
  $r = Auth 'POST' '/api/auth/register' $body
  if ($r.Status -ne 201) { throw "register failed ($($r.Status)): $($r.Body)" }
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
    if ($r.Status -ne 204 -and $r.Status -ne 200) { throw "land $landId document review failed: $($r.Status)" }
  }
  $r = Call 'POST' "/api/lands/$landId/verify" $null $adminTok
  if ($r.Status -ne 200) { throw "land $landId verify failed: $($r.Status) body=$($r.Body)" }
}
# Phase 1: a landlord registers only the governorate, the location identifiers and the area.
# The region, the grid and the four technical measurements are admin-only and are recorded
# through PUT /api/lands/{id}/assessment. $gid is kept in New-Land-Body's signature so the
# existing call sites stay valid; it is applied by Assess-Land instead of the create body.
function New-Land-Body([string]$ln, [string]$parcel, [int]$gid) {
  @{ governorateId = $fx.GovernorateId; landNumber = $ln; village = 'Village-S5'; basin = 'Basin-S5';
     parcelNumber = $parcel; areaDonum = 25 }
}
function Assess-Land([int]$landId, [int]$gid, [string]$adminTok) {
  $r = Call 'PUT' "/api/lands/$landId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $gid;
     slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100;
     note = 'S5 fixture assessment' } $adminTok
  if ($r.Status -ne 200) { throw "land $landId assessment failed: $($r.Status) body=$($r.Body)" }
}
function Create-Land([string]$ln, [string]$parcel, [int]$gid, [string]$laTok, [string]$adminTok) {
  $r = Call 'POST' '/api/lands' (New-Land-Body $ln $parcel $gid) $laTok
  if ($r.Status -ne 201) { throw "land create failed: $($r.Status) body=$($r.Body)" }
  $landId = [int](($r.Body | ConvertFrom-Json).id)
  Assess-Land $landId $gid $adminTok
  Verify-Land $landId $laTok $adminTok
  return $landId
}

Write-Output "== S5 setup ($stamp) - credentials never printed =="

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
Check 'S5-00' 'seeded SuperAdmin login' 200 200
if (-not $admin) { Write-Output 'BLOCKED: admin login returned no token'; exit 3 }

# Resolve the authenticated admin's own user id from GET /api/auth/me (its id comes from the
# token's ClaimTypes.NameIdentifier, i.e. USERS.ID). S5-47b must prove the UnderContract
# history row was written by THIS admin, not by "a user holding the SuperAdmin role" - the
# database legitimately holds more than one such user.
$r = Call 'GET' '/api/auth/me' $null $admin
if ($r.Status -ne 200) { Write-Output "BLOCKED: admin /api/auth/me failed ($($r.Status))"; exit 3 }
$adminId = [int](($r.Body | ConvertFrom-Json).id)

$laEmail = "s5-$stamp-la@example.test"; $lbEmail = "s5-$stamp-lb@example.test"
$i1Email = "s5-$stamp-i1@example.test"; $i2Email = "s5-$stamp-i2@example.test"
$i3Email = "s5-$stamp-i3@example.test"; $uEmail = "s5-$stamp-u@example.test"
$laId = Reg 'S5 Landlord A' $laEmail 1; $laTok = Login $laEmail $pw
$lbId = Reg 'S5 Landlord B' $lbEmail 1; $lbTok = Login $lbEmail $pw
$i1Id = Reg 'S5 Investor 1' $i1Email 2; $i1Tok = Login $i1Email $pw
$i2Id = Reg 'S5 Investor 2' $i2Email 2; $i2Tok = Login $i2Email $pw
$i3Id = Reg 'S5 Investor 3' $i3Email 2; $i3Tok = Login $i3Email $pw
$uId  = Reg 'S5 Investor U' $uEmail 2;  $uTok  = Login $uEmail $pw
Write-Output "users: la=$laId lb=$lbId i1=$i1Id i2=$i2Id i3=$i3Id u=$uId"

foreach ($id in @($i1Id, $i2Id, $i3Id)) {
  $r = Call 'POST' "/api/users/$id/approve" $null $admin
  if ($r.Status -ne 204) { throw "approve user $id failed: $($r.Status)" }
}
Write-Output 'investors i1,i2,i3 approved (204)'

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

# --- fixtures: 10-MW grid (G10) + Verified lands ---
$r = Call 'POST' '/api/grids' @{ regionId = $fx.PrimaryRegionId; name = "S5-G10-$stamp"; capacityMw = 10; status = 'Operational' } $admin
Check 'S5-01' 'admin creates 10-MW grid' 201 $r.Status
$g10 = [int](($r.Body | ConvertFrom-Json).id)

$L1 = Create-Land "S5-L1-$stamp" "S5P-L1-$stamp" $g10 $laTok $admin        # S3 land
$L1A = Create-Land "S5-L1A-$stamp" "S5P-L1A-$stamp" $g10 $laTok $admin      # S4 land A
$L1B = Create-Land "S5-L1B-$stamp" "S5P-L1B-$stamp" $g10 $laTok $admin      # S4 land B
$L2 = Create-Land "S5-L2-$stamp" "S5P-L2-$stamp" $fx.PrimaryGridId $laTok $admin            # withdraw suite land
$r = Call 'POST' '/api/lands' (New-Land-Body "S5-LRAW-$stamp" "S5P-LRAW-$stamp" $fx.PrimaryGridId) $laTok
Check 'S5-02' 'draft land created (for not-verified 409)' 201 $r.Status
$LRAW = [int](($r.Body | ConvertFrom-Json).id)
Write-Output "lands: L1=$L1 L1A=$L1A L1B=$L1B L2=$L2 LRAW(draft)=$LRAW; grid10=$g10"

# ================= BR-O1 / create gates =================
Write-Output ''
Write-Output '== create gates (BR-O1) =='
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $uTok
Check 'S5-10' 'unapproved investor creates offer' 403 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $laTok
Check 'S5-11' 'owner offers on own land' 409 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $LRAW; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-12' 'offer on non-Verified land' 409 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = 2147480000; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-13' 'offer on missing land' 404 $r.Status
$r = Call 'GET' '/api/offers' $null $null
Check 'S5-14' 'anonymous list offers' 401 $r.Status

# ================= S3: multi-investor + approval =================
Write-Output ''
Write-Output '== S3: two+ investors on L1, admin approves one, competitors preserved =='
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-20' 'i1 creates offer on L1' 201 $r.Status
$oA = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 25 } $i2Tok
Check 'S5-21' 'i2 creates offer on L1' 201 $r.Status
$oB = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 20 } $i3Tok
Check 'S5-22' 'i3 creates offer on L1 (multi-investor allowed while Verified)' 201 $r.Status
$oC = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 10 } $i1Tok
Check 'S5-23' 'same investor second active offer on same land' 409 $r.Status
$r = Call 'POST' "/api/offers/$oA/counter-offer" @{ newAmount = 35; message = 'x' } $i1Tok
Check 'S5-24' 'investor counter at PendingLandlordResponse (landlord turn)' 403 $r.Status
$r = Call 'POST' "/api/offers/$oA/counter-offer" @{ newAmount = 35; message = 'landlord counter v2' } $laTok
Check 'S5-25' 'landlord counter -> PendingInvestorResponse' 200 $r.Status
$r = Call 'POST' "/api/offers/$oA/counter-offer" @{ newAmount = 40; message = 'investor counter v3' } $i1Tok
Check 'S5-26' 'investor counter -> PendingLandlordResponse' 200 $r.Status
$r = Call 'POST' "/api/offers/$oA/accept" $null $i1Tok
Check 'S5-27' 'investor accept at PendingLandlordResponse (landlord turn)' 403 $r.Status
$r = Call 'POST' "/api/offers/$oA/accept" $null $laTok
Check 'S5-28' 'landlord accepts -> PendingAdminApproval' 200 $r.Status
$v = Sql "SELECT CAST(MAX(VERSION_NUMBER) AS varchar(4)) + '|' + CAST(SUM(CASE WHEN IS_CURRENT=1 THEN 1 ELSE 0 END) AS varchar(2)) + '|' + CAST(COUNT(*) AS varchar(2)) FROM OFFER_VERSIONS WHERE OFFER_ID = $oA;"
Check 'S5-29' '3 versions, exactly one current (filtered unique index)' '3|1|3' $v
$acc = Sql "SELECT CAST(ACCEPTED_VERSION_ID AS varchar(10)) FROM OFFERS WHERE ID = $oA;"
$cur = Sql "SELECT CAST(MAX(ID) AS varchar(10)) FROM OFFER_VERSIONS WHERE OFFER_ID = $oA;"
Check 'S5-29b' 'AcceptedVersionId set to current version (BR-O5)' $cur $acc

Write-Output ''
Write-Output '== read authorization (BR-O9 / 7) =='
$r = Call 'GET' '/api/offers/pending-admin-approval' $null $admin
$pendingHas = ($r.Body -match ('"id":' + $oA + '(?![0-9])'))
Check 'S5-30' 'admin lists pending approvals (contains oA)' 'True' $pendingHas
$r = Call 'GET' '/api/offers/pending-admin-approval' $null $i1Tok
Check 'S5-31' 'investor lists pending approvals' 403 $r.Status
$r = Call 'GET' "/api/offers/$oA" $null $i1Tok
Check 'S5-32' 'investor party reads own offer' 200 $r.Status
$r = Call 'GET' "/api/offers/$oA" $null $laTok
Check 'S5-32b' 'landlord party reads offer' 200 $r.Status
$r = Call 'GET' "/api/offers/$oA" $null $i2Tok
Check 'S5-33' 'third-party investor reads offer' 403 $r.Status
$r = Call 'GET' "/api/offers/$oA" $null $lbTok
Check 'S5-34' 'unrelated landlord reads offer' 403 $r.Status
$r = Call 'GET' '/api/offers' $null $null
Check 'S5-35' 'anonymous read offers' 401 $r.Status
$r = Call 'GET' "/api/offers/land/$L1" $null $laTok
$landHas = ($r.Body -match ('"id":' + $oB + '(?![0-9])'))
Check 'S5-36' 'land owner lists land offers' 'True' $landHas
$r = Call 'GET' "/api/offers/land/$L1" $null $lbTok
Check 'S5-37' 'other landlord lists land offers' 403 $r.Status
$r = Call 'GET' "/api/offers/land/$L1" $null $admin
Check 'S5-38' 'admin lists land offers' 200 $r.Status
$r = Call 'GET' '/api/offers/my' $null $i1Tok
$myHas = ($r.Body -match ('"id":' + $oA + '(?![0-9])'))
$myNot = ($r.Body -notmatch ('"id":' + $oB + '(?![0-9])'))
Check 'S5-39' 'GET my returns only own offers' 'True' ($myHas -and $myNot)
$r = Call 'GET' "/api/offers/$oA/negotiation-history" $null $i1Tok
Check 'S5-40' 'party reads negotiation history' 200 $r.Status
$r = Call 'GET' "/api/offers/$oA/negotiation-history" $null $i2Tok
Check 'S5-41' 'third party reads negotiation history' 403 $r.Status
$r = Call 'GET' "/api/offers/$oA/negotiation-history" $null $admin
Check 'S5-42' 'admin reads negotiation history' 200 $r.Status

Write-Output ''
Write-Output '== S3 approval (BR-O6): approve oA; competitors preserved + frozen; land UnderContract =='
# Landlord also accepts oC before approval so oC is a competing offer already in
# PendingAdminApproval; this lets the freeze be exercised for administrative approve/reject.
$r = Call 'POST' "/api/offers/$oC/accept" $null $laTok
Check 'S5-44' 'landlord also accepts oC -> PendingAdminApproval (competitor)' 200 $r.Status
$r = Call 'POST' "/api/offers/$oA/admin-approve" $null $admin
Check 'S5-45' 'admin approves oA' 200 $r.Status
$st = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oA;"
Check 'S5-46' 'DB oA Accepted(4)' 4 $st
$stB = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oB;"
$stC = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oC;"
Check 'S5-46b' 'DB competitors preserved (oB pending-landlord 1, oC pending-admin 3)' '1|3' ($stB + '|' + $stC)
$accCount = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFERS WHERE LAND_ID = $L1 AND STATUS_ID = 4;"
Check 'S5-53j' 'at most one Accepted offer per land (STATUS_ID=4)' 1 $accCount
$landSt = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L1;"
Check 'S5-47' 'DB L1 UnderContract(5)' 5 $landSt
$hist = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM LAND_STATUS_HISTORY WHERE LAND_ID = $L1 AND STATUS_ID = 5 AND CHANGED_BY_ID = $adminId;"
Check 'S5-47b' 'DB L1 has UnderContract history row, actor=admin' 1 $hist
$res = Sql "SELECT RESERVATION_TYPE + '|' + CAST(RESERVED_MW AS varchar(10)) + '|' + CAST(CASE WHEN RELEASED_AT IS NULL THEN 1 ELSE 0 END AS varchar(1)) + '|' + CAST(CASE WHEN EXPIRES_AT IS NULL THEN 0 ELSE 1 END AS varchar(1)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $oA;"
Check 'S5-48' 'DB reservation Offer/2/active/with expiry' 'Offer|2.00|1|1' $res
$days = Sql "SELECT CAST(DATEDIFF(day, GETUTCDATE(), EXPIRES_AT) AS varchar(5)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $oA;"
Check 'S5-48b' 'DB reservation ExpiresAt ~ now+90d' 90 $days
$ap = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oA AND ACTION_TYPE = 4;"
Check 'S5-49' 'DB ApprovedByAdmin history (action 4)' 1 $ap
$cc = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID IN ($oB,$oC) AND ACTION_TYPE = 9;"
Check 'S5-49b' 'DB no CompetitorClosed history (action 9) at approval' 0 $cc

Write-Output ''
Write-Output '== S3 freeze: competing-offer state-changing ops blocked without mutation =='
$hB0 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oB;"
$hC0 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oC;"
$r = Call 'POST' "/api/offers/$oB/counter-offer" @{ newAmount = 27; message = 'frozen' } $laTok
Check 'S5-53a' 'competing landlord counter blocked (frozen)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oB/accept" $null $laTok
Check 'S5-53b' 'competing landlord accept blocked (frozen)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oB/reject" @{ reason = 'frozen' } $laTok
Check 'S5-53c' 'competing landlord reject blocked (frozen)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oB/withdraw" $null $i2Tok
Check 'S5-53d' 'competing investor withdraw blocked (frozen)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oC/admin-reject" @{ reason = 'frozen' } $admin
Check 'S5-53e' 'competing admin reject blocked (frozen)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oC/admin-approve" $null $admin
Check 'S5-53f' 'competing admin approve blocked (frozen)' 409 $r.Status
$stB2 = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oB;"
$stC2 = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oC;"
Check 'S5-53g' 'DB competitor statuses unchanged by blocked ops' '1|3' ($stB2 + '|' + $stC2)
$hB1 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oB;"
$hC1 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oC;"
Check 'S5-53h' 'DB no history written for blocked competitors' ($hB0 + '|' + $hC0) ($hB1 + '|' + $hC1)
$r = Call 'GET' "/api/offers/$oA" $null $i1Tok
Check 'S5-53i' 'selected offer still readable after approval (not frozen)' 200 $r.Status
$r = Call 'POST' "/api/offers/$oA/admin-approve" $null $admin
Check 'S5-50' 'approve again' 409 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L1; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 15 } $i3Tok
Check 'S5-51' 'new offer on UnderContract land (replaced Closed-block rule)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oB/admin-approve" $null $admin
Check 'S5-52' 'approve a competing offer while land UnderContract' 409 $r.Status

# ================= F-3: competing-offer action raced against admin approval =================
Write-Output ''
Write-Output '== F-3: freeze race (admin approval vs competing withdraw) =='
$r = Call 'POST' '/api/grids' @{ regionId = $fx.PrimaryRegionId; name = "S5-F3G-$stamp"; capacityMw = 50; status = 'Operational' } $admin
Check 'S5-54-0' 'admin creates a dedicated grid for the F-3/F-2 fixtures' 201 $r.Status
$gF3 = [int](($r.Body | ConvertFrom-Json).id)
$L3 = Create-Land "S5-L3-$stamp" "S5P-L3-$stamp" $gF3 $laTok $admin
$r = Call 'POST' '/api/offers' @{ landId = $L3; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-54a' 'i1 creates offer A on L3' 201 $r.Status
$oR1 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $L3; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 25 } $i2Tok
Check 'S5-54b' 'i2 creates offer B on L3 (competitor stays PendingLandlordResponse)' 201 $r.Status
$oR2 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oR1/accept" $null $laTok
Check 'S5-54c' 'landlord accepts A -> PendingAdminApproval' 200 $r.Status
$race = Invoke-ConcurrentApiRequests -Requests @(
  @{ Method = 'POST'; Path = "/api/offers/$oR1/admin-approve"; BearerToken = $admin },
  @{ Method = 'POST'; Path = "/api/offers/$oR2/withdraw"; BearerToken = $i2Tok }
) -TimeoutSeconds 90
$sA = [int]$race[0].Status
$sB = [int]$race[1].Status
Check 'S5-54d' 'admin approval of A succeeds (no 500)' 200 $sA
Check 'S5-54e' 'competing withdraw is 200 (ran before approval) or 409 (frozen), never 500' 'True' ([string]($sB -eq 200 -or $sB -eq 409))
$stR2 = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oR2;"
$expR2 = if ($sB -eq 200) { '8' } else { '1' }
Check 'S5-54f' 'competitor state consistent (withdrawn=8 if it won, else unchanged=1)' $expR2 $stR2
$hR2 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFER_NEGOTIATION_HISTORY WHERE OFFER_ID = $oR2;"
$expHR2 = if ($sB -eq 200) { '2' } else { '1' }
Check 'S5-54g' 'competitor history consistent (no append when frozen)' $expHR2 $hR2
$landR3 = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L3;"
Check 'S5-54h' 'L3 UnderContract(5) after approval' 5 $landR3
if ($stR2 -eq '1') {
  # Deterministic follow-up: after the approval won, the land is UnderContract, so a
  # competing action must be frozen (409) and must not mutate the competitor.
  $r = Call 'POST' "/api/offers/$oR2/withdraw" $null $i2Tok
  Check 'S5-54i' 'post-race deterministic: competing withdraw frozen (409)' 409 $r.Status
  $stR2b = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oR2;"
  Check 'S5-54j' 'post-race deterministic: competitor unchanged (no mutation)' '1' $stR2b
}

# ================= F-2: one active offer per investor per land (DB-enforced) =================
Write-Output ''
Write-Output '== F-2: concurrent duplicate active offer (needs UX_OFFERS_ACTIVE_INVESTOR_LAND) =='
$hasIdx = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM sys.indexes WHERE name = 'UX_OFFERS_ACTIVE_INVESTOR_LAND';"
if ($hasIdx -eq '1') {
  $L4 = Create-Land "S5-L4-$stamp" "S5P-L4-$stamp" $gF3 $laTok $admin
  $body = (ConvertTo-Json -InputObject @{ landId = $L4; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } -Compress)
  $dup = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method = 'POST'; Path = '/api/offers'; Json = $body; BearerToken = $i1Tok },
    @{ Method = 'POST'; Path = '/api/offers'; Json = $body; BearerToken = $i1Tok }
  ) -TimeoutSeconds 90
  $c1 = [int]$dup[0].Status
  $c2 = [int]$dup[1].Status
  $codes = ((@($c1, $c2) | Sort-Object) -join ' ')
  Check 'S5-55a' 'concurrent duplicate create: exactly one 201 and one 409 (no 500)' '201 409' $codes
  $cnt = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFERS WHERE LAND_ID = $L4 AND INVESTOR_ID = $i1Id AND STATUS_ID IN (1,2,3,4);"
  Check 'S5-55b' 'DB exactly one active offer for (land, investor)' 1 $cnt
  $tot = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFERS WHERE LAND_ID = $L4 AND INVESTOR_ID = $i1Id;"
  Check 'S5-55c' 'no orphan/partial offer rows after the failed request' 1 $tot
} else {
  $blocked++
  Write-Output 'BLOCKED S5-55: UX_OFFERS_ACTIVE_INVESTOR_LAND absent (migration Step5_OffersActiveInvestorLandUnique not applied)'
}

# ================= M-1: create vs approval race =================
Write-Output ''
Write-Output '== M-1: a create cannot slip onto a land that approval moved to UnderContract =='
# Deterministic: approve an offer, then a competing create on the now-UnderContract land.
$L5 = Create-Land "S5-L5-$stamp" "S5P-L5-$stamp" $gF3 $laTok $admin
$r = Call 'POST' '/api/offers' @{ landId = $L5; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-56a' 'i1 creates offer A on L5 (Verified land)' 201 $r.Status
$oM1 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oM1/accept" $null $laTok
Check 'S5-56b' 'landlord accepts A on L5' 200 $r.Status
$r = Call 'POST' "/api/offers/$oM1/admin-approve" $null $admin
Check 'S5-56c' 'admin approves A -> L5 UnderContract' 200 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L5; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 25 } $i2Tok
Check 'S5-56d' 'competing create on the UnderContract land -> 409' 409 $r.Status
$cntM1 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFERS WHERE LAND_ID = $L5 AND INVESTOR_ID = $i2Id;"
Check 'S5-56e' 'rejected create left no offer row (and thus no version/history)' 0 $cntM1
# Concurrency: race the approval against a competing create on a fresh land.
$L6 = Create-Land "S5-L6-$stamp" "S5P-L6-$stamp" $gF3 $laTok $admin
$r = Call 'POST' '/api/offers' @{ landId = $L6; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-56f' 'i1 creates offer A on L6' 201 $r.Status
$oM2 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oM2/accept" $null $laTok
Check 'S5-56g' 'landlord accepts A on L6' 200 $r.Status
$bodyM = (ConvertTo-Json -InputObject @{ landId = $L6; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 20 } -Compress)
$raceM = Invoke-ConcurrentApiRequests -Requests @(
  @{ Method = 'POST'; Path = "/api/offers/$oM2/admin-approve"; BearerToken = $admin },
  @{ Method = 'POST'; Path = '/api/offers'; Json = $bodyM; BearerToken = $i2Tok }
) -TimeoutSeconds 90
$sApp = [int]$raceM[0].Status
$sCre = [int]$raceM[1].Status
Check 'S5-56h' 'race: approval succeeds (no 500)' 200 $sApp
Check 'S5-56i' 'race: competing create is 201 (before approval) or 409 (after), never 500' 'True' ([string]($sCre -eq 201 -or $sCre -eq 409))
$landM2 = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L6;"
Check 'S5-56j' 'race: L6 UnderContract(5)' 5 $landM2
$cntM2 = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM OFFERS WHERE LAND_ID = $L6 AND INVESTOR_ID = $i2Id;"
$expM2 = if ($sCre -eq 201) { '1' } else { '0' }
Check 'S5-56k' 'race: offer row matches the create result (1 if 201, 0 if 409)' $expM2 $cntM2

# ================= S4: capacity =================
Write-Output ''
Write-Output '== S4: 10-MW grid, two 6-MW offers, second approval 409 =='
$r = Call 'POST' '/api/offers' @{ landId = $L1A; requiredCapacityMw = 6; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-60' 'i1 creates 6-MW offer on L1A' 201 $r.Status
$oD = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $L1B; requiredCapacityMw = 6; connectionMechanism = 1; landlordSharePct = 30 } $i2Tok
Check 'S5-61' 'i2 creates 6-MW offer on L1B (no create-time capacity gate)' 201 $r.Status
$oE = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oD/accept" $null $laTok
Check 'S5-62' 'landlord accepts oD' 200 $r.Status
$r = Call 'POST' "/api/offers/$oE/accept" $null $laTok
Check 'S5-62b' 'landlord accepts oE' 200 $r.Status
$r = Call 'POST' "/api/offers/$oD/admin-approve" $null $admin
Check 'S5-63' 'admin approves oD (2+6=8 <=10)' 200 $r.Status
$r = Call 'POST' "/api/offers/$oE/admin-approve" $null $admin
Check 'S5-64' 'admin approves oE (8+6=14 >10)' 409 $r.Status
$sum = Sql "SELECT CAST(SUM(RESERVED_MW) AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $g10 AND RELEASED_AT IS NULL;"
Check 'S5-65' 'DB active reserved on G10 = 8' '8.00' $sum
$stE = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oE;"
$stLB = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L1B;"
$resE = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $oE;"
Check 'S5-66' 'no partial state: oE pending(3), L1B verified(3), no reservation' '3|3|0' ($stE + '|' + $stLB + '|' + $resE)
$stLA = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(2)) FROM LANDS WHERE ID = $L1A;"
Check 'S5-67' 'DB L1A UnderContract(5)' 5 $stLA

# ================= withdraw suite (BR-O3 allow-list) =================
Write-Output ''
Write-Output '== withdraw transitions (BR-O3) =='
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30 } $i3Tok
Check 'S5-70' 'i3 creates offer on L2' 201 $r.Status
$oW1 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW1/withdraw" $null $i3Tok
Check 'S5-71' 'withdraw at PendingLandlordResponse' 200 $r.Status
$w1 = Sql "SELECT CAST(STATUS_ID AS varchar(2)) + '|' + CAST(COUNT(*) AS varchar(2)) FROM OFFERS o JOIN OFFER_NEGOTIATION_HISTORY h ON h.OFFER_ID = o.ID WHERE o.ID = $oW1 GROUP BY o.ID, o.STATUS_ID;"
Check 'S5-71b' 'DB Withdrawn(8) + Created+Withdrawn history rows' '8|2' $w1
$r = Call 'POST' "/api/offers/$oW1/withdraw" $null $i3Tok
Check 'S5-72' 'withdraw again (WithdrawnByInvestor)' 409 $r.Status
$w1b = Sql "SELECT CAST(STATUS_ID AS varchar(2)) + '|' + CAST(COUNT(*) AS varchar(2)) FROM OFFERS o JOIN OFFER_NEGOTIATION_HISTORY h ON h.OFFER_ID = o.ID WHERE o.ID = $oW1 GROUP BY o.ID, o.STATUS_ID;"
Check 'S5-72b' 'DB unchanged after 409 (terminal + no new history)' '8|2' $w1b
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 25 } $i3Tok
Check 'S5-73' 'new offer after withdraw (terminal does not block, D-14)' 201 $r.Status
$oW2 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW2/counter-offer" @{ newAmount = 26 } $laTok
Check 'S5-74' 'landlord counter on oW2' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW2/reject" @{ reason = '   ' } $i3Tok
Check 'S5-74b' 'investor reject with blank reason' 400 $r.Status
$r = Call 'POST' "/api/offers/$oW2/counter-offer" @{ newAmount = -5 } $i3Tok
Check 'S5-94' 'counter with negative amount' 400 $r.Status
$r = Call 'POST' "/api/offers/$oW2/counter-offer" @{ newAmount = 0 } $i3Tok
Check 'S5-95' 'counter with zero amount' 400 $r.Status
$r = Call 'POST' "/api/offers/$oW2/withdraw" $null $i3Tok
Check 'S5-96' 'withdraw at PendingInvestorResponse' 200 $r.Status

$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 20 } $i1Tok
Check 'S5-75' 'i1 creates offer on L2' 201 $r.Status
$oW3 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW3/counter-offer" @{ newAmount = 21 } $laTok
Check 'S5-76' 'landlord counter on oW3' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW3/withdraw" $null $i1Tok
Check 'S5-77' 'withdraw at PendingInvestorResponse (W2)' 200 $r.Status

$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 22 } $i2Tok
Check 'S5-78' 'i2 creates offer on L2' 201 $r.Status
$oW4 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW4/counter-offer" @{ newAmount = 23 } $laTok
Check 'S5-78b' 'landlord counter on oW4' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW4/reject" @{ reason = 'investor walks away' } $i2Tok
Check 'S5-79' 'investor rejects -> RejectedByInvestor' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW4/withdraw" $null $i2Tok
Check 'S5-80' 'withdraw at RejectedByInvestor (terminal)' 409 $r.Status

$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 24 } $i1Tok
Check 'S5-81' 'i1 creates offer on L2 (again)' 201 $r.Status
$oW5 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW5/reject" @{ reason = 'landlord not interested' } $laTok
Check 'S5-81b' 'landlord rejects -> RejectedByLandlord' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW5/withdraw" $null $i1Tok
Check 'S5-82' 'withdraw at RejectedByLandlord (terminal)' 409 $r.Status

$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 24 } $i2Tok
Check 'S5-83' 'i2 creates offer on L2 (again)' 201 $r.Status
$oW6 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' "/api/offers/$oW6/accept" $null $laTok
Check 'S5-83b' 'landlord accepts -> PendingAdminApproval' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW6/withdraw" $null $i2Tok
Check 'S5-84' 'withdraw at PendingAdminApproval' 409 $r.Status
$w6 = Sql "SELECT CAST(STATUS_ID AS varchar(2)) FROM OFFERS WHERE ID = $oW6;"
Check 'S5-84b' 'DB oW6 still PendingAdminApproval(3)' 3 $w6
$r = Call 'POST' "/api/offers/$oW6/admin-reject" @{ reason = 'not viable' } $admin
Check 'S5-85' 'admin rejects with reason' 200 $r.Status
$r = Call 'POST' "/api/offers/$oW6/withdraw" $null $i2Tok
Check 'S5-86' 'withdraw at RejectedByAdmin (terminal)' 409 $r.Status
$r = Call 'POST' "/api/offers/$oW1/withdraw" $null $laTok
Check 'S5-87' 'landlord withdraws (wrong role)' 403 $r.Status

# ================= validation (5.8) =================
Write-Output ''
Write-Output '== validation (D-16 / 5.8) =='
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 0; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
Check 'S5-90' 'capacity = 0' 400 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 0 } $i1Tok
Check 'S5-91' 'amount (share) = 0' 400 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 99; landlordSharePct = 30 } $i1Tok
Check 'S5-92' 'undefined ConnectionMechanism' 400 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 30; message = ('M' * 501) } $i1Tok
Check 'S5-93' '501-char message' 400 $r.Status
$r = Call 'POST' '/api/offers' @{ landId = $L2; requiredCapacityMw = 1; connectionMechanism = 1; landlordSharePct = 1000 } $i1Tok
Check 'S5-93b' 'share 1000 (out of 0.01-999.99 range)' 400 $r.Status
$r = Call 'POST' "/api/offers/$oW5/admin-reject" @{ reason = '' } $admin
Check 'S5-88' 'admin reject with empty reason' 400 $r.Status
$r = Call 'POST' "/api/offers/$oW5/admin-reject" @{ reason = ('R' * 501) } $admin
Check 'S5-88b' 'admin reject reason 501 chars' 400 $r.Status
$r = Call 'POST' "/api/offers/$oA/close" $null $admin
Check 'S5-97' 'public close endpoint removed' 404 $r.Status
$r = Call 'GET' '/api/offers/my' $null $admin
Check 'S5-98' 'admin GET my (all offers)' 200 $r.Status

Write-Output ''
$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ("S5 TOTAL PASS=$pass FAIL=$fail BLOCKED=$blocked (auth calls used: $global:AuthCalls)")
if ($fail -gt 0) { exit 1 }
if ($blocked -gt 0) { exit 2 }
}
finally {
  Complete-TestApiCleanup -Identity $harness
}
