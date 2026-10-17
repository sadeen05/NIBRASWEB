# Step 7 - grid-capacity reservation surface (never prints credentials or tokens).
#
# Covers the approved reservation scope decisions:
#   * POST /api/GridCapacityReservations/{id}/release exists, is Admin/SuperAdmin only,
#     stamps ReleasedAt/ReleaseReason, is idempotent, and 404s for an unknown id.
#   * The direct reservation POST/PUT/DELETE routes are gone (405) and reservations are
#     created only by workflow code (offer approval).
#   * Releasing a reservation returns its capacity to the grid (accounting stays intact).
#   * Concurrency (F1/F2): overlapping releases cannot overwrite the first release's audit
#     fields, and a release racing a contract creation can never leave the hold both
#     released and linked to a live contract.
# Expiry behaviour (ExpiresAt/HoldDays) is intentionally NOT exercised here: its removal
# is deferred to a separate, separately-approved increment.
#
# Starts its own app once on the authorized test database, like s6_contracts.ps1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$base = 'http://localhost:5181'
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
$sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { "$env:COMPUTERNAME\SQLEXPRESS" }
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
  Write-Output ("{0,-8} {1,-4} exp={2,-20} act={3,-20} {4}" -f $Id, $res, $Expected, $Actual, $Desc)
}
function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -b -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  if ($LASTEXITCODE -ne 0) { throw ("sqlcmd failed (exit {0}): {1}" -f $LASTEXITCODE, ((@($out) -join ' ').Trim())) }
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}
function Login([string]$Email, [string]$Pass) {
  $r = Call 'POST' '/api/auth/login' @{ email = $Email; password = $Pass }
  if ($r.Status -ne 200) { throw "login failed ($($r.Status))" }
  return ($r.Body | ConvertFrom-Json).token
}
function Reg([string]$FullName, [string]$Email, [int]$RoleId) {
  $body = @{ fullName = $FullName; email = $Email; phone = (New-Phone); password = $pw; roleId = $RoleId }
  if ($RoleId -eq 2) { $body.companyName = 'S7R Co'; $body.companyRegistrationNumber = "S7R-REG-$stamp" }
  $r = Call 'POST' '/api/auth/register' $body
  if ($r.Status -ne 200) { throw "register failed ($($r.Status))" }
  return [int](($r.Body | ConvertFrom-Json).id)
}
function Verify-Land([int]$landId, [string]$laTok, [string]$adminTok) {
  $docIds = @()
  foreach ($dt in 1..2) {
    $r = Call 'POST' "/api/lands/$landId/documents" @{ documentTypeId = $dt; filePath = 'docs/s7r-deed.pdf' } $laTok
    if ($r.Status -ne 201) { throw "land $landId document upload ($dt) failed: $($r.Status)" }
    $docIds += [int](($r.Body | ConvertFrom-Json).id)
  }
  if ((Call 'POST' "/api/lands/$landId/submit" $null $laTok).Status -ne 200) { throw "land $landId submit failed" }
  foreach ($docId in $docIds) {
    $s = (Call 'PUT' "/api/lands/$landId/documents/$docId/review" @{ approved = $true } $adminTok).Status
    if ($s -ne 204 -and $s -ne 200) { throw "land $landId document review failed: $s" }
  }
  if ((Call 'POST' "/api/lands/$landId/verify" $null $adminTok).Status -ne 200) { throw "land $landId verify failed" }
}

# Builds an isolated verified land on a fresh grid, an accepted offer and the workflow-created
# reservation for it. Used by the Decision C cases. Returns the ids for assertions.
function New-ApprovedHold([string]$tag, [int]$regionId, [string]$laTok, [string]$ivTok, [string]$adminTok) {
  $g = Call 'POST' '/api/grids' @{ regionId = $regionId; name = "S7R-G-$tag-$stamp"; capacityMw = 20; status = 'Operational' } $adminTok
  if ($g.Status -ne 201) { throw "grid ($tag) create failed: $($g.Status)" }
  $grid = [int](($g.Body | ConvertFrom-Json).id)

  $l = Call 'POST' '/api/lands' @{ regionId = $regionId; gridId = $grid; landNumber = "S7R-L-$tag-$stamp"; village = "V-$tag"; basin = "B-$tag"; parcelNumber = "S7RP-$tag-$stamp"; areaDonum = 100; slopePercentage = 3; distanceToGridKm = 5; solarIrradiance = 6; elevationM = 100 } $laTok
  if ($l.Status -ne 201) { throw "land ($tag) create failed: $($l.Status)" }
  $landId = [int](($l.Body | ConvertFrom-Json).id)
  Verify-Land $landId $laTok $adminTok

  $o = Call 'POST' '/api/offers' @{ landId = $landId; requiredCapacityMw = 2; connectionMechanism = 1; landlordSharePct = 30 } $ivTok
  if ($o.Status -ne 201) { throw "offer ($tag) create failed: $($o.Status)" }
  $offerId = [int](($o.Body | ConvertFrom-Json).id)
  if ((Call 'POST' "/api/offers/$offerId/accept" $null $laTok).Status -ne 200) { throw "offer ($tag) accept failed" }
  if ((Call 'POST' "/api/offers/$offerId/admin-approve" $null $adminTok).Status -ne 200) { throw "offer ($tag) admin-approve failed" }

  $resId = [int](Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $offerId;")
  return [pscustomobject]@{ Grid = $grid; Land = $landId; Offer = $offerId; Reservation = $resId }
}

# --- target verification BEFORE any database access ---
Write-Output "== S7R setup ($stamp) - credentials never printed =="
$target = Sql "SELECT DB_NAME();"
if ($target -ne $db) { Write-Output "BLOCKED: expected $db but connected to '$target'"; exit 2 }
Check 'S7R-00' 'database target is NEBRASDB_Step1' $db $target

. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = Start-S4App -Port 5181 -AuthPerMinute 1000

try {
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
  Check 'S7R-01' 'seeded SuperAdmin login' 200 200

  # ---------- removed direct CRUD routes (405) ----------
  Write-Output ''
  Write-Output '== removed reservation CRUD routes =='
  Check 'S7R-10' 'POST /api/GridCapacityReservations -> 405' 405 (Call 'POST' '/api/GridCapacityReservations' @{ gridId = 1; offerId = 1; reservedMw = 1 } $admin).Status
  Check 'S7R-11' 'PUT /api/GridCapacityReservations/{id} -> 405' 405 (Call 'PUT' '/api/GridCapacityReservations/1' @{ reservedMw = 1 } $admin).Status
  Check 'S7R-12' 'DELETE /api/GridCapacityReservations/{id} -> 405' 405 (Call 'DELETE' '/api/GridCapacityReservations/1' $null $admin).Status
  Check 'S7R-13' 'GET /api/GridCapacityReservations still readable -> 200' 200 (Call 'GET' '/api/GridCapacityReservations' $null $admin).Status

  # ---------- release authorization ----------
  Write-Output ''
  Write-Output '== release authorization =='
  $laEmail = "s7r-$stamp-la@example.test"
  $laId = Reg 'S7R Landlord' $laEmail 1
  $laTok = Login $laEmail $pw
  Check 'S7R-20' 'anonymous release -> 401' 401 (Call 'POST' '/api/GridCapacityReservations/1/release' $null $null).Status
  Check 'S7R-21' 'landlord release -> 403' 403 (Call 'POST' '/api/GridCapacityReservations/1/release' $null $laTok).Status
  Check 'S7R-22' 'landlord GET reservations -> 403' 403 (Call 'GET' '/api/GridCapacityReservations' $null $laTok).Status
  Check 'S7R-23' 'admin release unknown id -> 404' 404 (Call 'POST' '/api/GridCapacityReservations/2147480000/release' $null $admin).Status

  # ---------- workflow creates the reservation ----------
  Write-Output ''
  Write-Output '== workflow-created reservation + release =='
  . (Join-Path $PSScriptRoot 'test_fixtures.ps1')
  $fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db

  $r = Call 'POST' '/api/grids' @{ regionId = $fx.PrimaryRegionId; name = "S7R-G-$stamp"; capacityMw = 20; status = 'Operational' } $admin
  if ($r.Status -ne 201) { throw "grid create failed: $($r.Status)" }
  $grid = [int](($r.Body | ConvertFrom-Json).id)

  $land = Call 'POST' '/api/lands' @{ regionId = $fx.PrimaryRegionId; gridId = $grid; landNumber = "S7R-L-$stamp"; village = 'V-S7R'; basin = 'B-S7R'; parcelNumber = "S7RP-$stamp"; areaDonum = 100; slopePercentage = 3; distanceToGridKm = 5; solarIrradiance = 6; elevationM = 100 } $laTok
  if ($land.Status -ne 201) { throw "land create failed: $($land.Status)" }
  $landId = [int](($land.Body | ConvertFrom-Json).id)
  Verify-Land $landId $laTok $admin

  $ivEmail = "s7r-$stamp-iv@example.test"
  $ivId = Reg 'S7R Investor' $ivEmail 2
  if ((Call 'POST' "/api/users/$ivId/approve" $null $admin).Status -ne 204) { throw 'approve investor failed' }
  $ivTok = Login $ivEmail $pw

  $offer = Call 'POST' '/api/offers' @{ landId = $landId; requiredCapacityMw = 4; connectionMechanism = 1; landlordSharePct = 30 } $ivTok
  if ($offer.Status -ne 201) { throw "offer create failed: $($offer.Status)" }
  $offerId = [int](($offer.Body | ConvertFrom-Json).id)
  if ((Call 'POST' "/api/offers/$offerId/accept" $null $laTok).Status -ne 200) { throw 'offer accept failed' }
  if ((Call 'POST' "/api/offers/$offerId/admin-approve" $null $admin).Status -ne 200) { throw 'offer admin-approve failed' }

  $resId = [int](Sql "SELECT CAST(ID AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $offerId;")
  $activeBefore = [int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $offerId AND RELEASED_AT IS NULL;")
  Check 'S7R-30' 'workflow approval created exactly one active reservation' 1 $activeBefore

  $reservedBefore = [decimal](Sql "SELECT CAST(SUM(RESERVED_MW) AS varchar(20)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $grid AND RELEASED_AT IS NULL;")

  $rel = Call 'POST' "/api/GridCapacityReservations/$resId/release" @{ reason = 'S7R admin release' } $admin
  Check 'S7R-31' 'admin release -> 200' 200 $rel.Status
  $relBody = $rel.Body | ConvertFrom-Json
  Check 'S7R-32' 'release stamps ReleasedAt' 'True' ([string]($null -ne $relBody.releasedAt))
  Check 'S7R-33' 'release does not delete the row' 1 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $resId;"))
  Check 'S7R-34' 'release reason recorded' 'S7R admin release' (Sql "SELECT RELEASE_REASON FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $resId;")

  # idempotency: a repeated release must not move ReleasedAt or change the reason
  $firstReleasedAt = Sql "SELECT CONVERT(varchar(30), RELEASED_AT, 126) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $resId;"
  $rel2 = Call 'POST' "/api/GridCapacityReservations/$resId/release" @{ reason = 'second attempt' } $admin
  Check 'S7R-35' 'repeated release -> 200 (idempotent)' 200 $rel2.Status
  Check 'S7R-36' 'repeated release leaves ReleasedAt unchanged' $firstReleasedAt (Sql "SELECT CONVERT(varchar(30), RELEASED_AT, 126) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $resId;")
  Check 'S7R-37' 'repeated release leaves ReleaseReason unchanged' 'S7R admin release' (Sql "SELECT RELEASE_REASON FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $resId;")

  $reservedAfter = [decimal](Sql "SELECT ISNULL(CAST(SUM(RESERVED_MW) AS varchar(20)), '0') FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $grid AND RELEASED_AT IS NULL;")
  Check 'S7R-38' 'released reservation no longer counts toward grid capacity' 'True' ([string]($reservedAfter -lt $reservedBefore))

  # a released reservation must not be re-released into a second reservation
  Check 'S7R-39' 'still exactly one reservation row for the offer' 1 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID = $offerId;"))

  # ---------- Decision C: a live contract owns its reservation ----------
  Write-Output ''
  Write-Output '== Decision C: manual release vs live contracts =='
  $h = New-ApprovedHold 'LC' $fx.PrimaryRegionId $laTok $ivTok $admin

  $c = Call 'POST' '/api/contracts' @{ offerId = $h.Offer } $admin
  Check 'S7R-40' 'contract created from the accepted offer -> 201' 201 $c.Status
  $cId = [int](($c.Body | ConvertFrom-Json).id)

  # PendingSignatures: blocked, and the reservation audit fields must be untouched
  $r = Call 'POST' "/api/GridCapacityReservations/$($h.Reservation)/release" @{ reason = 'blocked attempt' } $admin
  Check 'S7R-41' 'release while contract PendingSignatures -> 409' 409 $r.Status
  Check 'S7R-42' 'blocked release message directs to the contract workflow' 'This reservation belongs to a live contract. Reject or terminate the contract through the normal workflow to release it.' ([string](($r.Body | ConvertFrom-Json).detail))
  Check 'S7R-43' 'blocked release leaves ReleasedAt null' 1 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($h.Reservation) AND RELEASED_AT IS NULL;"))
  Check 'S7R-44' 'blocked release leaves ReleaseReason null' 0 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($h.Reservation) AND RELEASE_REASON IS NOT NULL;"))

  # both parties sign, admin approves -> Active; still blocked
  $null = Call 'POST' "/api/contracts/$cId/sign" $null $ivTok
  $null = Call 'POST' "/api/contracts/$cId/sign" $null $laTok
  $r = Call 'POST' "/api/contracts/$cId/review" @{ approve = $true } $admin
  Check 'S7R-45' 'contract approved -> Active' 200 $r.Status
  $r = Call 'POST' "/api/GridCapacityReservations/$($h.Reservation)/release" @{ reason = 'blocked attempt 2' } $admin
  Check 'S7R-46' 'release while contract Active -> 409' 409 $r.Status
  Check 'S7R-47' 'blocked Active release leaves ReleasedAt null' 1 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($h.Reservation) AND RELEASED_AT IS NULL;"))

  # the contract workflow releases it on termination; a later manual release is a safe no-op
  $r = Call 'POST' "/api/contracts/$cId/terminate" @{ reason = 'S7R termination' } $admin
  Check 'S7R-48' 'terminate the active contract -> 200' 200 $r.Status
  $r = Call 'POST' "/api/GridCapacityReservations/$($h.Reservation)/release" @{ reason = 'after termination' } $admin
  Check 'S7R-49' 'release after the contract ended -> 200 (idempotent)' 200 $r.Status
  Check 'S7R-50' 'ended-contract release keeps the workflow reason' 'S7R termination' (Sql "SELECT RELEASE_REASON FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($h.Reservation);")

  # Allowed path for an ENDED contract: construct the (otherwise unreachable) state where the
  # contract has ended but its hold was not released by the workflow, then release manually.
  # Direct SQL is used only to build this test-only state, mirroring the s6 suite's forced-expiry setup.
  $null = Sql "SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; UPDATE GRID_CAPACITY_RESERVATIONS SET RELEASED_AT = NULL, RELEASE_REASON = NULL WHERE ID = $($h.Reservation);"
  Check 'S7R-51' 'ended contract + unreleased hold is releasable' 200 (Call 'POST' "/api/GridCapacityReservations/$($h.Reservation)/release" @{ reason = 'manual after ended contract' } $admin).Status
  Check 'S7R-52' 'allowed release records the manual reason' 'manual after ended contract' (Sql "SELECT RELEASE_REASON FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($h.Reservation);")

  # ---------- F1/F2: concurrency hardening ----------
  Write-Output ''
  Write-Output '== concurrency: release vs release, release vs contract creation =='

  # Real overlapping HTTP from separate PowerShell processes (same approach as the s6 M-1 block
  # and parallel-approval.ps1). Every job waits for ONE shared start instant so the calls hit the
  # API together instead of relying on process-startup jitter.
  $jobPost = {
    param($baseUrl, $token, $path, $body, $startMs)
    if ($startMs) { while ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() -lt [long]$startMs) { Start-Sleep -Milliseconds 5 } }
    $h = @{ Authorization = 'Bearer ' + $token }
    $p = @{ Uri = ($baseUrl + $path); Method = 'Post'; Headers = $h; UseBasicParsing = $true; ErrorAction = 'Stop' }
    if ($body) { $p.Body = $body; $p.ContentType = 'application/json' }
    try { $r = Invoke-WebRequest @p; return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = [string]$r.Content } }
    catch {
      $resp = $_.Exception.Response
      if ($null -eq $resp) { throw }
      $t = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd()
      return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $t }
    }
  }
  function Receive-Race($jobs) {
    $out = @()
    foreach ($j in $jobs) {
      $o = @(Receive-Job $j); Remove-Job $j -Force
      if ($o.Count -eq 0) { throw 'race job produced no result' }
      $out += $o[-1]
    }
    return $out
  }
  # The API may serialise an in-memory Utc value with a trailing 'Z' and the same instant read
  # back from the database without one, so normalise before comparing instants.
  function Release-Instant([string]$Body) {
    $v = [string](($Body | ConvertFrom-Json).releasedAt)
    return (($v -replace 'Z$', '') -replace '\+00:00$', '')
  }

  # F2: overlapping releases of the SAME hold. Exactly one call writes ReleasedAt/ReleaseReason;
  # every other call must observe the already-released row and change nothing.
  $hRR = New-ApprovedHold 'RR' $fx.PrimaryRegionId $laTok $ivTok $admin
  $rrReasons = @('race release A', 'race release B', 'race release C')
  $raceMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + 4000
  $rrJobs = @()
  foreach ($rsn in $rrReasons) {
    $rrJobs += Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/GridCapacityReservations/$($hRR.Reservation)/release", (ConvertTo-Json -InputObject @{ reason = $rsn } -Compress), $raceMs
  }
  $null = Wait-Job $rrJobs -Timeout 60
  $rrRes = Receive-Race $rrJobs
  Check 'S7R-60' 'overlapping releases all return 200 (one writes, rest idempotent)' '200 200 200' (@($rrRes | ForEach-Object { [int]$_.Status }) -join ' ')

  $rrTimes = @($rrRes | Where-Object { [int]$_.Status -eq 200 } | ForEach-Object { Release-Instant $_.Body })
  Check 'S7R-61' 'overlapping releases report ONE release event (no overwrite)' 1 (@($rrTimes | Sort-Object -Unique).Count)

  # Bind the API-reported instant to the persisted row at full datetime2(7) precision, without
  # depending on how the serializer formats a Database-read vs in-memory DateTime.
  $rrMatch = 0
  if ($rrTimes.Count -gt 0) {
    $rrMatch = [int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($hRR.Reservation) AND RELEASED_AT IS NOT NULL AND TODATETIMEOFFSET(RELEASED_AT, 0) = CAST('$($rrTimes[0])' AS datetimeoffset);")
  }
  Check 'S7R-62' 'the persisted ReleasedAt is the value the API reported' 1 $rrMatch
  $rrReason = Sql "SELECT RELEASE_REASON FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($hRR.Reservation);"
  Check 'S7R-63' 'the persisted reason is exactly one submitted reason' 'True' ([string]($rrReasons -contains $rrReason))

  # F1: a manual release and a contract creation racing on the same hold. Both paths pin the
  # reservation row, so exactly one wins and the hold can never end up released while it is
  # linked to a live contract.
  $hRC = New-ApprovedHold 'RC' $fx.PrimaryRegionId $laTok $ivTok $admin
  $raceMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + 4000
  $jRel = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, "/api/GridCapacityReservations/$($hRC.Reservation)/release", (ConvertTo-Json -InputObject @{ reason = 'race release vs create' } -Compress), $raceMs
  $jCre = Start-Job -ScriptBlock $jobPost -ArgumentList $base, $admin, '/api/contracts', (ConvertTo-Json -InputObject @{ offerId = $hRC.Offer } -Compress), $raceMs
  $null = Wait-Job $jRel, $jCre -Timeout 60
  $rcRes = Receive-Race @($jRel, $jCre)
  $relSt = [int]$rcRes[0].Status
  $creSt = [int]$rcRes[1].Status
  Check 'S7R-70' 'release vs create: exactly one side wins (200 xor 201)' 'True' ([string]((($relSt -eq 200) -and ($creSt -eq 409)) -or (($relSt -eq 409) -and ($creSt -eq 201))))
  Check 'S7R-71' 'release vs create: the loser fails cleanly, never with 5xx' 'True' ([string](($relSt -lt 500) -and ($creSt -lt 500)))
  Check 'S7R-72' 'no hold is both released and linked to a live contract' 0 ([int](Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS r JOIN CONTRACTS c ON c.ID = r.CONTRACT_ID WHERE r.ID = $($hRC.Reservation) AND r.RELEASED_AT IS NOT NULL AND c.STATUS_ID IN (1, 2);"))
  $expectHold = if ($relSt -eq 200) { 'released=1,linked=0' } else { 'released=0,linked=1' }
  $holdRel = Sql "SELECT CAST(CASE WHEN RELEASED_AT IS NULL THEN 0 ELSE 1 END AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($hRC.Reservation);"
  $holdCon = Sql "SELECT CAST(CASE WHEN CONTRACT_ID IS NULL THEN 0 ELSE 1 END AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE ID = $($hRC.Reservation);"
  Check 'S7R-73' 'the persisted hold matches the winning call' $expectHold ("released=$holdRel,linked=$holdCon")
}
finally { Stop-S4App $app }

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "S7R RESERVATIONS TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
exit 0
