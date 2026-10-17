# Step 5 - 5.9 Parallel admin approval (BR-O6 concurrency)
#
# Purpose: prove that two overlapping ApproveOffer calls on two different 6 MW offers
# connected to the SAME 10 MW grid cannot both succeed and cannot oversubscribe the grid.
# The grid row is pinned with an app-level UPDLOCK,HOLDLOCK inside one Serializable
# transaction (D-08 fix), so the second approval re-reads capacity after the first commits
# and must fail with 409.
#
# How it works:
#   - Uses ONLY normal HTTP authentication (real /api/auth/register + login, real JWT).
#     No JWT forgery, no secrets printed.
#   - Creates a FRESH 10 MW grid, two Verified lands on it, two 6 MW offers, landlord
#     accepts both (-> PendingAdminApproval).
#   - Fires the two admin-approve requests concurrently from two parallel PowerShell jobs
#     (separate processes, genuinely overlapping HTTP; normal Invoke-WebRequest auth).
#   - Asserts exactly one 200 and one 409, then verifies the database: active reserved
#     capacity on the grid = 6 MW (<= 10), exactly one Accepted offer, one winner land
#     UnderContract, loser offer still PendingAdminApproval, loser land still Verified,
#     and no reservation row for the loser.
#   - Does NOT clean up its fixtures (leave them for inspection).
#   - Exits non-zero on setup failure or assertion failure (never silently skips).
#
# Usage:   powershell -File docs/tests/parallel-approval.ps1
# Requires: the app running on http://localhost:5181 against NEBRASDB_Step1.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$base = 'http://localhost:5181'
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
$sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { "$env:COMPUTERNAME\SQLEXPRESS" }
$db = 'NEBRASDB_Step1'
$fails = @()

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
function Pace { Start-Sleep -Seconds 7 }  # stay under the 10/min /api/auth rate limit
function Auth([string]$Method, [string]$Path, $Body) { Pace; return (Call $Method $Path $Body) }
function Assert([string]$Name, $Expected, $Actual) {
  $ok = ("$Expected" -eq "$Actual")
  if (-not $ok) { ${script:fails} += "${Name}: expected $Expected got $Actual" }
  Write-Output ("{0,-6} {1} exp={2} act={3}" -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $Name, $Expected, $Actual)
}
function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}

Write-Output "== parallel approval (stamp $stamp) =="

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

$r = Auth 'POST' '/api/auth/login' @{ email = $adminEmail; password = $adminPass }
if ($r.Status -ne 200) { Write-Output 'BLOCKED: admin login failed'; exit 3 }
$admin = ($r.Body | ConvertFrom-Json).token

$laEmail = "s5p-$stamp-la@example.test"; $i1Email = "s5p-$stamp-i1@example.test"; $i2Email = "s5p-$stamp-i2@example.test"
$r = Auth 'POST' '/api/auth/register' @{ fullName = 'P Landlord'; email = $laEmail; phone = (New-Phone); password = $pw; roleId = 1 }
if ($r.Status -ne 200) { Write-Output 'BLOCKED: landlord register failed'; exit 3 }
$r = Auth 'POST' '/api/auth/login' @{ email = $laEmail; password = $pw }
if ($r.Status -ne 200) { Write-Output 'BLOCKED: landlord login failed'; exit 3 }
$laTok = ($r.Body | ConvertFrom-Json).token
$i1Id = [int]((Auth 'POST' '/api/auth/register' @{ fullName = 'P Investor1'; email = $i1Email; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'P Co'; companyRegistrationNumber = "P1-$stamp" }).Body | ConvertFrom-Json).id
$r = Auth 'POST' '/api/auth/login' @{ email = $i1Email; password = $pw }
if ($r.Status -ne 200) { Write-Output 'BLOCKED: investor1 login failed'; exit 3 }
$i1Tok = ($r.Body | ConvertFrom-Json).token
$i2Id = [int]((Auth 'POST' '/api/auth/register' @{ fullName = 'P Investor2'; email = $i2Email; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'P Co'; companyRegistrationNumber = "P2-$stamp" }).Body | ConvertFrom-Json).id
$r = Auth 'POST' '/api/auth/login' @{ email = $i2Email; password = $pw }
if ($r.Status -ne 200) { Write-Output 'BLOCKED: investor2 login failed'; exit 3 }
$i2Tok = ($r.Body | ConvertFrom-Json).token
foreach ($id in @($i1Id, $i2Id)) {
  $r = Call 'POST' "/api/users/$id/approve" $null $admin
  if ($r.Status -ne 204) { Write-Output "BLOCKED: approve user $id failed"; exit 3 }
}
Write-Output 'users ready (tokens never printed)'

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

# fresh 10-MW grid + two Verified lands on it
$r = Call 'POST' '/api/grids' @{ regionId = $fx.PrimaryRegionId; name = "S5P-G10-$stamp"; capacityMw = 10; status = 'Operational' } $admin
if ($r.Status -ne 201) { Write-Output 'BLOCKED: grid create failed'; exit 3 }
$gridId = [int](($r.Body | ConvertFrom-Json).id)

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
$landIds = @()
foreach ($n in 1..2) {
  $r = Call 'POST' '/api/lands' @{ regionId = $fx.PrimaryRegionId; gridId = $gridId; landNumber = "S5P-L$n-$stamp"; village = 'V-S5P'; basin = 'B-S5P'; parcelNumber = "S5PP-L$n-$stamp"; areaDonum = 25; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100 } $laTok
  if ($r.Status -ne 201) { Write-Output 'BLOCKED: land create failed'; exit 3 }
  $landId = [int](($r.Body | ConvertFrom-Json).id)
  Verify-Land $landId $laTok $admin
  $landIds += $landId
}
Write-Output "grid=$gridId lands=$($landIds -join ',') (all Verified)"

# two 6 MW offers -> landlord accepts -> PendingAdminApproval
$r = Call 'POST' '/api/offers' @{ landId = $landIds[0]; requiredCapacityMw = 6; connectionMechanism = 1; landlordSharePct = 30 } $i1Tok
if ($r.Status -ne 201) { Write-Output 'BLOCKED: offer1 create failed'; exit 3 }
$offer1 = [int](($r.Body | ConvertFrom-Json).id)
$r = Call 'POST' '/api/offers' @{ landId = $landIds[1]; requiredCapacityMw = 6; connectionMechanism = 1; landlordSharePct = 30 } $i2Tok
if ($r.Status -ne 201) { Write-Output 'BLOCKED: offer2 create failed'; exit 3 }
$offer2 = [int](($r.Body | ConvertFrom-Json).id)
foreach ($o in @($offer1, $offer2)) {
  $r = Call 'POST' "/api/offers/$o/accept" $null $laTok
  if ($r.Status -ne 200) { Write-Output "BLOCKED: accept $o failed"; exit 3 }
}
Write-Output "offers $offer1 and $offer2 both PendingAdminApproval (6 MW each)"

# ---- concurrent approvals (two parallel PowerShell jobs = two processes, genuinely
# overlapping HTTP requests; normal Bearer auth via the real admin token) ----
$jobScript = {
  param($baseUrl, $token, $path)
  $h = @{ Authorization = 'Bearer ' + $token }
  try {
    $r = Invoke-WebRequest -Uri ($baseUrl + $path) -Method Post -Headers $h -UseBasicParsing -ErrorAction Stop
    return [int]$r.StatusCode
  } catch {
    $resp = $_.Exception.Response
    if ($null -eq $resp) { throw }
    return [int]$resp.StatusCode
  }
}
$job1 = Start-Job -ScriptBlock $jobScript -ArgumentList $base, $admin, "/api/offers/$offer1/admin-approve"
$job2 = Start-Job -ScriptBlock $jobScript -ArgumentList $base, $admin, "/api/offers/$offer2/admin-approve"
$null = Wait-Job $job1, $job2 -Timeout 90
$s1a = Receive-Job $job1
$s2a = Receive-Job $job2
Remove-Job $job1, $job2 -Force
$s1 = [int]$s1a; $s2 = [int]$s2a
$sorted = @($s1, $s2) | Sort-Object
Assert 'exactly one approval succeeds (200) and one fails (409)' '200 409' ($sorted -join ' ')

# ---- database evidence ----
$reserved = Sql "SELECT CAST(SUM(RESERVED_MW) AS varchar(10)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $gridId AND RELEASED_AT IS NULL AND (EXPIRES_AT IS NULL OR EXPIRES_AT > GETUTCDATE());"
Assert 'active reserved capacity = 6 MW (never > grid capacity)' '6.00' $reserved
$statuses = Sql "SELECT CAST(COUNT(CASE WHEN STATUS_ID = 4 THEN 1 END) AS varchar(2)) + '|' + CAST(COUNT(CASE WHEN STATUS_ID = 3 THEN 1 END) AS varchar(2)) FROM OFFERS WHERE ID IN ($offer1,$offer2);"
Assert 'exactly one Accepted(4), one still PendingAdminApproval(3)' '1|1' $statuses
$landSt = Sql "SELECT CAST(COUNT(CASE WHEN LAND_STATUS_ID = 5 THEN 1 END) AS varchar(2)) + '|' + CAST(COUNT(CASE WHEN LAND_STATUS_ID = 3 THEN 1 END) AS varchar(2)) FROM LANDS WHERE ID IN ($($landIds[0]),$($landIds[1]));"
Assert 'winner land UnderContract(5), loser land Verified(3)' '1|1' $landSt
$loserRes = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE OFFER_ID IN (SELECT ID FROM OFFERS WHERE ID IN ($offer1,$offer2) AND STATUS_ID = 3);"
Assert 'loser offer has no reservation' 0 $loserRes
$expiryOk = Sql "SELECT CAST(COUNT(*) AS varchar(2)) FROM GRID_CAPACITY_RESERVATIONS WHERE GRID_ID = $gridId AND (EXPIRES_AT IS NULL OR EXPIRES_AT <= GETUTCDATE());"
Assert 'winner reservation not expired and has future ExpiresAt' 0 $expiryOk

Write-Output ''
if ($fails.Count -gt 0) {
  Write-Output ('FAILURES: ' + ($fails -join '; '))
  exit 1
}
Write-Output 'PARALLEL APPROVAL PASS (one 200, one 409; grid not oversubscribed)'
Write-Output 'Fixtures intentionally left in the database for inspection (no auto-cleanup).'