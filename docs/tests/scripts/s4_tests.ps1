$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')

$harness = $null
$runStartedUtc = [DateTime]::UtcNow
$suiteExitCode = 0
$cleanupProblems = New-Object System.Collections.Generic.List[string]

function Stop-S4Run {
  param([int]$Code, [string]$Message)
  if ($Message) { Write-Output $Message }
  $exception = New-Object System.Exception("S4 run stopped with exit code $Code")
  $exception.Data['S4ExitCode'] = $Code
  throw $exception
}

function Stop-S4Api {
  # Stops only the PID recorded by the harness, and only while that PID still owns the
  # harness port. Foreign listeners are reported, never killed. The return value is a list
  # of cleanup problems; an empty list means port release and identity removal were verified.
  param($Identity)

  $problems = New-Object System.Collections.Generic.List[string]
  if ($null -eq $Identity) {
    [void]$problems.Add('no harness identity was recorded')
    return $problems.ToArray()
  }

  try {
    Stop-TestApiInstance -Identity $Identity
  }
  catch {
    [void]$problems.Add(('Stop-TestApiInstance failed: {0}' -f $_.Exception.Message))
  }

  $port = [int]$Identity.Port
  $identityPath = Join-Path ([IO.Path]::GetTempPath()) 'nibras-f1-api-identity.json'
  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  $identityPresent = $true
  $listeners = @()
  $apiProcess = $null
  $portCheckError = $null

  # Observe only. Stop-TestApiInstance already performed the ownership check; this loop waits
  # for the exact process and its listener to disappear, without ever killing another PID.
  do {
    $identityPresent = Test-Path -LiteralPath $identityPath
    $listeners = @()
    try {
      $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop)
    }
    catch {
      if ($_.Exception.Message -notmatch 'No matching|not found' -and $_.FullyQualifiedErrorId -notmatch 'NotFound|NoMatching') {
        $portCheckError = $_.Exception.Message
      }
    }

    $apiProcess = Get-Process -Id ([int]$Identity.Pid) -ErrorAction SilentlyContinue
    $stillPresent = $identityPresent -or ($listeners.Count -gt 0) -or ($null -ne $apiProcess) -or ($null -ne $portCheckError)
    if (-not $stillPresent) { return $problems.ToArray() }
    if ($null -ne $portCheckError) { break }
    if ([DateTime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Milliseconds 250
  } while ($true)

  if ($identityPresent) {
    [void]$problems.Add('the API identity file still exists after cleanup')
  }
  if ($listeners.Count -gt 0) {
    $owners = @($listeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
    [void]$problems.Add(("port {0} still has listener process(es) {1}" -f $port, ($owners -join ',')))
  }
  if ($null -ne $apiProcess) {
    [void]$problems.Add(("API process {0} is still alive" -f $Identity.Pid))
  }
  if ($null -ne $portCheckError) {
    [void]$problems.Add(('port cleanup check failed: {0}' -f $portCheckError))
  }

  return $problems.ToArray()
}

try {
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
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
$db = (Get-TestTarget).Database
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
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Case = $Case; Expected = $exp; Actual = $act; Result = $res; Note = $Note })
  Write-Output ("{0,-7} {1,-4} exp={2,-4} act={3,-4} {4} {5}" -f $Id, $res, $exp, $act, $Case, $Note)
}

function Sql([string]$Query) {
  $out = & sqlcmd -S $sqlServer -E -d $db -W -h -1 -s '|' -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  return (@($out | Where-Object { $_ -and ($_ -notmatch 'rows? affected') }) -join "`n").Trim()
}

function Login([string]$Email) {
  $r = Call 'POST' '/api/auth/login' @{ email = $Email; password = $pw }
  if ($r.Status -ne 200) { throw "login failed: $($r.Status)" }
  return ($r.Body | ConvertFrom-Json).token
}

Write-Output '== setup (tokens and passwords never printed) =='
$laEmail = "s4-$stamp-la@example.test"; $lbEmail = "s4-$stamp-lb@example.test"; $inEmail = "s4-$stamp-in@example.test"
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4 Landlord A'; email = $laEmail; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $laEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $laEmail failed ($($v.Status))" }
$laId = [int]($r.Body | ConvertFrom-Json).id
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4 Landlord B'; email = $lbEmail; phone = (New-Phone); password = $pw; roleId = 1 }

  $v = Confirm-VerifiedAccount -Email $lbEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $lbEmail failed ($($v.Status))" }
$lbId = [int]($r.Body | ConvertFrom-Json).id
$baseline = New-OutboxBaseline
$r = Call 'POST' '/api/auth/register' @{ fullName = 'S4 Investor'; email = $inEmail; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'S4 Co'; companyRegistrationNumber = "S4-REG-$stamp" }

  $v = Confirm-VerifiedAccount -Email $inEmail -Baseline $baseline
if ($v.Status -ne 204) { throw "the verification of $inEmail failed ($($v.Status))" }
$invId = [int]($r.Body | ConvertFrom-Json).id
$aTok = Login $laEmail
$bTok = Login $lbEmail
$iTok = Login $inEmail
Write-Output "users: landlord A id=$laId, landlord B id=$lbId, investor unapproved; 3 logins 200"

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $db
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

# Phase 1: the landlord supplies only the governorate, the location identifiers and the area.
# Region, grid and the four technical measurements are admin-only and are recorded through
# PUT /api/lands/{id}/assessment.
$good = @{ governorateId = $fx.GovernorateId; landNumber = "L-$stamp"; village = 'Village-A'; basin = 'Basin-A'
  parcelNumber = "P-$stamp"; areaDonum = 25 }

Write-Output ''
Write-Output '== 4.3 create and validation (landlord A) =='
$r = Call 'POST' '/api/lands' $good $aTok
Check 'S4-01' 'landlord A creates a valid land' 201 $r.Status
$landId = [int]($r.Body | ConvertFrom-Json).id

# Region/grid are no longer accepted from a landlord, so region/grid consistency is asserted
# against the admin assessment endpoint below and this slot now covers governorate validation.
$x = $good.Clone(); $x.governorateId = 999999; $x.parcelNumber = "P2-$stamp"
$r = Call 'POST' '/api/lands' $x $aTok
Check 'S4-02' 'unknown governorate id -> 400' 400 $r.Status

# Slope is no longer landlord-supplied (Phase 1); the landowner-enforced criterion is area.
$x = $good.Clone(); $x.areaDonum = 5; $x.parcelNumber = "P3-$stamp"; $x.landNumber = "L3-$stamp"
$r = Call 'POST' '/api/lands' $x $aTok
Check 'S4-03' 'area 5 fails min 20' 400 $r.Status
Check 'S4-03b' 'failure message lists "area"' 'True' ([string]($r.Body -match 'area'))

$x = $good.Clone(); $x.parcelNumber = "P4-$stamp"; $x.landNumber = "L4-$stamp"; $x.landlordId = 999999; $x.status = 'Verified'; $x.dataVerifiedByAdmin = $true; $x.documentStorageLocation = 'C:\evil'
$r = Call 'POST' '/api/lands' $x $aTok
Check 'S4-04' 'body with landlordId/status/dataVerifiedByAdmin is accepted (extra fields ignored)' 201 $r.Status
$ignoredId = [int]($r.Body | ConvertFrom-Json).id
$dbv = Sql "SELECT CAST(LANDLORD_ID AS varchar(10)) + '|' + CAST(LAND_STATUS_ID AS varchar(10)) + '|' + CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) FROM LANDS WHERE ID = $ignoredId;"
Check 'S4-05' 'DB: owner = token user, status Draft(1), dataVerified 0' "$laId|1|0" $dbv

$x = $good.Clone(); $x.village = ('V' * 10000); $x.parcelNumber = "P5-$stamp"
$r = Call 'POST' '/api/lands' $x $aTok
Check 'S4-06' '10,000-character Village' 400 $r.Status

$x = $good.Clone(); $x.village = ('Vil' + [char]1 + 'lage'); $x.parcelNumber = "P6-$stamp"
$r = Call 'POST' '/api/lands' $x $aTok
Check 'S4-07' 'control character in Village' 400 $r.Status

$r = Call 'POST' '/api/lands' $good $aTok
Check 'S4-08' 'duplicate parcel (region, village, basin, parcel)' 409 $r.Status

Write-Output ''
Write-Output '== 4.4 submit, documents, ownership (landlord A and B) =='
$r = Call 'POST' "/api/lands/$landId/submit" $null $aTok
Check 'S4-09' 'submit with no required-type document' 409 $r.Status

$r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = 1; filePath = 'docs/titledeed-001.pdf' } $aTok
Check 'S4-10' 'upload TitleDeed (required type) 201' 201 $r.Status
$docId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = 1; filePath = 'docs/titledeed-002.pdf' } $aTok
Check 'S4-11' 'second TitleDeed upload gets Version 2' 2 (($r.Body | ConvertFrom-Json).version)

$r = Call 'POST' "/api/lands/$landId/submit" $null $aTok
Check 'S4-12' 'submit after a required-type upload' 200 $r.Status

$r = Call 'GET' "/api/lands/$landId" $null $bTok
Check 'S4-13' 'landlord B GETs A''s land' 403 $r.Status
$upd = @{ governorateId = $fx.GovernorateId; landNumber = "L-$stamp"; village = 'Village-A'; basin = 'Basin-A'; parcelNumber = "P-$stamp"; areaDonum = 25 }
$r = Call 'PUT' "/api/lands/$landId" $upd $bTok
Check 'S4-14' 'landlord B PUTs A''s land' 403 $r.Status
$r = Call 'POST' "/api/lands/$landId/submit" $null $bTok
Check 'S4-15' 'landlord B submits A''s land' 403 $r.Status
$r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = 2; filePath = 'docs/x.pdf' } $bTok
Check 'S4-16' 'landlord B uploads to A''s land' 403 $r.Status
$r = Call 'GET' "/api/lands/$landId/documents" $null $bTok
Check 'S4-17' 'landlord B lists A''s documents' 403 $r.Status

Write-Output ''
Write-Output '== 4.6 filePath validation (SEC-13) =='
$bad = @(
  @{ id = 'S4-18'; path = 'http://169.254.169.254/'; name = 'http://169.254.169.254/' },
  @{ id = 'S4-19'; path = 'file:///c:/windows/win.ini'; name = 'file:///c:/windows/win.ini' },
  @{ id = 'S4-20'; path = '\\server\share'; name = '\\server\share' },
  @{ id = 'S4-21'; path = '..\..\x'; name = '..\..\x' },
  @{ id = 'S4-22'; path = ('a' * 501); name = '501-character path' }
)
foreach ($b in $bad) {
  $r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = 2; filePath = $b.path } $aTok
  Check $b.id ("filePath rejected: " + $b.name) 400 $r.Status
}
$r = Call 'POST' "/api/lands/$landId/documents/metadata" @{ documentTypeId = 2; filePath = 'docs/encumbrance-001.pdf' } $aTok
Check 'S4-23' 'upload while PendingVerification is blocked (submitted evidence is frozen)' 409 $r.Status
$cnt = Sql "SELECT COUNT(*) FROM LAND_DOCUMENTS WHERE LAND_ID = $landId;"
Check 'S4-24' 'DB: rejected paths and the blocked upload created no rows (2 Draft uploads only)' 2 $cnt
$r = Call 'POST' "/api/lands/$ignoredId/documents/metadata" @{ documentTypeId = 2; filePath = 'docs/encumbrance-001.pdf' } $aTok
Check 'S4-23b' 'same valid relative path is accepted while the land is Draft' 201 $r.Status

Write-Output ''
Write-Output '== 4.7 verify / review / reject as landlord (must be 403) =='
$r = Call 'POST' "/api/lands/$landId/verify" $null $aTok
Check 'S4-25' 'landlord calls verify' 403 $r.Status
$r = Call 'PUT' "/api/lands/$landId/documents/$docId/review" @{ approved = $true; note = 'ok' } $aTok
Check 'S4-26' 'review by a landlord' 403 $r.Status
$r = Call 'POST' "/api/lands/$landId/reject" @{ reason = 'Landlord attempt' } $aTok
Check 'S4-27' 'landlord calls reject' 403 $r.Status
$r = Call 'POST' "/api/lands/$landId/reject" @{ reason = '' } $aTok
Check 'S4-28' 'reject with empty reason (validation runs before role check; Admin variant = S4-52a)' 400 $r.Status
$dbv = Sql "SELECT FILE_PATH + '|' + CAST(DOCUMENT_TYPE_ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE ID = $docId;"
Check 'S4-29' 'DB: FilePath and DocumentTypeId unchanged after review attempts' 'docs/titledeed-001.pdf|1' $dbv

Write-Output ''
Write-Output '== 4.5 browse, eligibility, history =='
$r = Call 'GET' "/api/lands/$landId/eligibility" $null $aTok
Check 'S4-30' 'eligibility endpoint 200' 200 $r.Status
$elig = $r.Body | ConvertFrom-Json
$elevation = @($elig.results | Where-Object { $_.criterion -eq 'elevation' })[0]
Check 'S4-31' 'MinElevation (elevation) shows NotEnforced' 'NotEnforced' $elevation.result
$area = @($elig.results | Where-Object { $_.criterion -eq 'area' })[0]
Check 'S4-32' 'area 25 vs min 20 shows Passed' 'Passed' $area.result

$r = Call 'GET' "/api/lands/$landId/history" $null $aTok
Check 'S4-33' 'history 200 for owner' 200 $r.Status
$hist = $r.Body | ConvertFrom-Json
Check 'S4-34' 'history rows carry a reason text' 'True' ([string](@($hist | Where-Object { $_.reason }).Count -gt 0))

$r = Call 'GET' "/api/lands/$landId/history" $null $bTok
Check 'S4-35' 'history for a non-owner landlord' 403 $r.Status

$r = Call 'GET' '/api/lands?pageNumber=1&pageSize=20' $null $aTok
$own = $r.Body | ConvertFrom-Json
Check 'S4-36' 'landlord A list 200 with envelope' 200 $r.Status
Check 'S4-37' 'landlord A list shows only own lands' 'True' ([string](@($own.items | Where-Object { $_.landlordId -ne $laId }).Count -eq 0))
$r = Call 'GET' '/api/lands?pageSize=1000' $null $aTok
Check 'S4-38' 'pageSize=1000' 400 $r.Status
$inj = [uri]::EscapeDataString("' OR 1=1-- -")
$r = Call 'GET' ("/api/lands?status=" + $inj) $null $aTok
Check 'S4-39' "?status=' OR 1=1-- -" 400 $r.Status
$r = Call 'GET' '/api/lands?status=PendingVerification' $null $aTok
Check 'S4-40' 'landlord ?status=PendingVerification (own lands)' 200 $r.Status
$r = Call 'GET' '/api/lands' $null $iTok
Check 'S4-41' 'unapproved investor lists lands' 403 $r.Status

Write-Output ''
Write-Output '== 4.8 / 4.9 criteria and document types (non-admin) =='
$r = Call 'GET' '/api/criteria/current' $null $aTok
Check 'S4-42' 'GET /api/criteria/current (any authenticated)' 200 $r.Status
$r = Call 'PUT' '/api/criteria' @{ reason = 'x' } $aTok
Check 'S4-43' 'PUT /api/criteria (no PUT route)' 405 $r.Status
$r = Call 'DELETE' '/api/criteria' $null $aTok
Check 'S4-44' 'DELETE /api/criteria (no DELETE route)' 405 $r.Status
$r = Call 'POST' '/api/criteria' @{ maxSlopePct = 5; reason = 'landlord attempt' } $aTok
Check 'S4-45' 'landlord POST /api/criteria' 403 $r.Status
$r = Call 'GET' '/api/criteria/history' $null $aTok
Check 'S4-46' 'landlord GET /api/criteria/history' 403 $r.Status
$r = Call 'GET' '/api/document-types' $null $aTok
Check 'S4-47' 'GET /api/document-types (any authenticated)' 200 $r.Status
$r = Call 'POST' '/api/document-types' @{ name = 'Landlord'; isRequiredForVerification = $false } $aTok
Check 'S4-48' 'landlord POST /api/document-types' 403 $r.Status
$r = Call 'DELETE' '/api/document-types/1' $null $aTok
Check 'S4-49' 'landlord DELETE /api/document-types/1' 403 $r.Status

Write-Output ''
Write-Output '== A2: admin verification paths via a disposable Admin (SuperAdmin-provisioned) =='
# SuperAdmin credentials: dedicated disposable-environment variables, never printed.
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
if (-not $repoRoot) { Stop-S4Run -Code 2 -Message 'FATAL: NibrasWeb.csproj not found above the script directory' }
$sec = @{}
# The SuperAdmin credentials come from the dedicated disposable-environment variables.
# The user secrets are never enumerated: the suite reads exactly the two values it needs,
# and it never prints them.
$saEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
$saPass = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
if (-not $saEmail -or -not $saPass) { Stop-S4Run -Code 2 -Message 'BLOCKED: NIBRAS_TEST_SUPERADMIN_EMAIL / NIBRAS_TEST_SUPERADMIN_PASSWORD are not set' }

$r = Call 'POST' '/api/auth/login' @{ email = $saEmail; password = $saPass }
Check 'A2-01' 'seeded SuperAdmin logs in (credentials never printed)' 200 $r.Status
if ($r.Status -ne 200) { Stop-S4Run -Code 3 -Message 'cannot continue without SuperAdmin' }
$saTok = ($r.Body | ConvertFrom-Json).token

$admEmail = "s4-adm-$stamp@example.test"
$r = Call 'POST' '/api/users' @{ fullName = 'S4 Disposable Admin'; email = $admEmail; phone = (New-Phone); password = $pw; roleId = 3 } $saTok
Check 'A2-02' 'SuperAdmin creates a disposable Admin (auto-approved)' 201 $r.Status
$admId = [int]($r.Body | ConvertFrom-Json).id
# A new administrator account has no usable password: it is activated with the activation
# link and a password of this suite's choosing.
  Activate-AdminAccount -Email $admEmail -Password $pw -Baseline $baseline | Out-Null
$r = Call 'POST' '/api/auth/login' @{ email = $admEmail; password = $pw }
Check 'A2-03' 'disposable Admin logs in' 200 $r.Status
$admTok = ($r.Body | ConvertFrom-Json).token

# --- land X: submit with pending docs -> S4-50/S4-56 -> S4-51 -> S4-53/S4-60 ---
$x = $good.Clone(); $x.parcelNumber = "PX-$stamp"; $x.landNumber = "LX-$stamp"
$r = Call 'POST' '/api/lands' $x $aTok
Check 'A2-04' 'create land X (Draft)' 201 $r.Status
$xId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$xId/documents/metadata" @{ documentTypeId = 1; filePath = "docs/x-title-$stamp.pdf" } $aTok
Check 'A2-05' 'upload TitleDeed on X' 201 $r.Status
$xDoc1 = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$xId/documents/metadata" @{ documentTypeId = 2; filePath = "docs/x-enc-$stamp.pdf" } $aTok
Check 'A2-06' 'upload EncumbranceStatement on X' 201 $r.Status
$xDoc2 = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$xId/submit" $null $aTok
Check 'A2-07' 'submit X -> PendingVerification' 200 $r.Status

# Phase 1: region/grid/technical measurements are admin-only. Assess X so the only remaining
# blocker for verification is the pending required document.
$r = Call 'PUT' "/api/lands/$xId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $fx.PrimaryGridId; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100; note = 'S4 fixture assessment' } $saTok
Check 'A2-07b' 'SuperAdmin assesses X (region/grid/technicals)' 200 $r.Status

# Land data may not be changed by a regular Admin: the assessment endpoint is SuperAdmin-only.
$r = Call 'PUT' "/api/lands/$xId/assessment" @{ slopePercentage = 6 } $admTok
Check 'A2-07d' 'regular Admin assesses a land -> 403' 403 $r.Status

# A grid that does not belong to the given region is refused by the assessment endpoint.
$r = Call 'PUT' "/api/lands/$xId/assessment" @{ regionId = $fx.SecondaryRegionId; gridId = $fx.PrimaryGridId } $saTok
Check 'A2-07c' 'assessment with a grid from another region -> 400' 400 $r.Status

$r = Call 'POST' "/api/lands/$xId/verify" $null $admTok
Check 'S4-50' 'verify while a required document is Pending -> 409' 409 $r.Status

$dbBefore = Sql "SELECT CAST(ID AS varchar(10)) + '|' + FILE_PATH + '|' + CAST(DOCUMENT_TYPE_ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE LAND_ID = $xId ORDER BY ID;"
$r = Call 'PUT' "/api/lands/$xId/documents/$xDoc1/review" @{ approved = $false } $admTok
Check 'S4-56a' 'reject a document without a note -> 400' 400 $r.Status
$r = Call 'PUT' "/api/lands/$xId/documents/$xDoc1/review" @{ approved = $false; note = 'needs a clearer scan' } $admTok
Check 'S4-56b' 'reject a document with a note -> 200' 200 $r.Status
$r = Call 'PUT' "/api/lands/$xId/documents/$xDoc1/review" @{ approved = $true } $admTok
Check 'S4-56c' 'admin approves the TitleDeed on X -> 200' 200 $r.Status
$r = Call 'PUT' "/api/lands/$xId/documents/$xDoc2/review" @{ approved = $true } $admTok
Check 'S4-56d' 'admin approves the EncumbranceStatement on X -> 200' 200 $r.Status
$dbAfter = Sql "SELECT CAST(ID AS varchar(10)) + '|' + FILE_PATH + '|' + CAST(DOCUMENT_TYPE_ID AS varchar(10)) FROM LAND_DOCUMENTS WHERE LAND_ID = $xId ORDER BY ID;"
Check 'S4-56e' 'review never changes FilePath/DocumentTypeId (DB)' $dbBefore $dbAfter

$curId = [int]((Call 'GET' '/api/criteria/current' $null $admTok).Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$xId/verify" $null $admTok
Check 'S4-51a' 'verify X with every required doc approved -> 200' 200 $r.Status
$dbv = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(3)) + '|' + CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $xId;"
Check 'S4-51b' 'DB: X status=3, dataVerified=1, criterion = current' ("3|1|$curId") $dbv
$v1 = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $xId;"

$r = Call 'PUT' "/api/lands/$xId" $x $aTok
Check 'S4-53' 'owner edit of a Verified land -> 409' 409 $r.Status
$r = Call 'PUT' "/api/lands/$xId/documents/$xDoc1/review" @{ approved = $false; note = 'recheck' } $admTok
Check 'S4-60' 'D1: review a document of a Verified land -> 409' 409 $r.Status

# --- land Y: reject -> revise -> edit -> resubmit -> verify (S4-52 full cycle) ---
$y = $good.Clone(); $y.parcelNumber = "PY-$stamp"; $y.landNumber = "LY-$stamp"
$r = Call 'POST' '/api/lands' $y $aTok
Check 'A2-08' 'create land Y (Draft)' 201 $r.Status
$yId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$yId/documents/metadata" @{ documentTypeId = 1; filePath = "docs/y-title-$stamp.pdf" } $aTok
Check 'A2-09' 'upload TitleDeed on Y' 201 $r.Status
$yDoc1 = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$yId/documents/metadata" @{ documentTypeId = 2; filePath = "docs/y-enc-$stamp.pdf" } $aTok
Check 'A2-10' 'upload EncumbranceStatement on Y' 201 $r.Status
$yDoc2 = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$yId/submit" $null $aTok
Check 'A2-11' 'submit Y -> PendingVerification' 200 $r.Status

$r = Call 'POST' "/api/lands/$yId/reject" @{ reason = '' } $admTok
Check 'S4-52a' 'admin reject with an empty reason -> 400' 400 $r.Status
$r = Call 'POST' "/api/lands/$yId/reject" @{ reason = 'Missing clarity in evidence' } $admTok
Check 'S4-52b' 'admin rejects Y with a reason -> 200' 200 $r.Status
$dbv = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(3)) FROM LANDS WHERE ID = $yId;"
Check 'S4-52c' 'DB: Y status = Rejected(4)' '4' $dbv
$r = Call 'POST' "/api/lands/$yId/revise" $null $aTok
Check 'S4-52d' 'owner revises Y -> 200' 200 $r.Status
$dbv = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(3)) FROM LANDS WHERE ID = $yId;"
Check 'S4-52e' 'DB: Y status = Draft(1) after revise' '1' $dbv
$r = Call 'PUT' "/api/lands/$yId" $y $aTok
Check 'S4-52f' 'owner edits the revised Y -> 204' 204 $r.Status
$r = Call 'POST' "/api/lands/$yId/submit" $null $aTok
Check 'S4-52g' 'owner resubmits Y -> 200' 200 $r.Status
$dbv = Sql "SELECT CAST(LAND_STATUS_ID AS varchar(3)) FROM LANDS WHERE ID = $yId;"
Check 'S4-52h' 'DB: Y status = PendingSuperAdminReview(6), not the admin queue' '6' $dbv

# A resubmission after a rejection is reviewed by a SuperAdmin. The regular Admin must be
# refused (403) for both the document review and the decision, so the resubmission can never
# enter the regular admin queue.
$r = Call 'PUT' "/api/lands/$yId/documents/$yDoc1/review" @{ approved = $true } $admTok
Check 'S4-52h2' 'regular Admin reviews a resubmission document -> 403' 403 $r.Status
$r = Call 'POST' "/api/lands/$yId/verify" $null $admTok
Check 'S4-52h3' 'regular Admin verifies a resubmission -> 403' 403 $r.Status

# The assessment writes region/grid/measurements for the first time, and an assessment that
# changes any of them withdraws existing document approvals so the evidence is reviewed against
# the current data (LandService.InvalidateApprovedDocumentsAsync keeps the review audit and
# sets the version back to Pending). The approvals therefore follow the assessment, exactly as
# in the land-X block above; approving before it would (correctly) leave the evidence Pending
# and the verification below would return 409.
$r = Call 'PUT' "/api/lands/$yId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $fx.PrimaryGridId; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100; note = 'S4 fixture assessment' } $saTok
Check 'A2-13b' 'SuperAdmin assesses Y' 200 $r.Status
$r = Call 'PUT' "/api/lands/$yId/documents/$yDoc1/review" @{ approved = $true } $saTok
Check 'A2-12' 'SuperAdmin approves the TitleDeed on Y' 200 $r.Status
$r = Call 'PUT' "/api/lands/$yId/documents/$yDoc2/review" @{ approved = $true } $saTok
Check 'A2-13' 'SuperAdmin approves the EncumbranceStatement on Y' 200 $r.Status
$r = Call 'POST' "/api/lands/$yId/verify" $null $saTok
Check 'S4-52i' 'SuperAdmin verifies Y on the second cycle -> 200' 200 $r.Status

# --- S4-54: approved investor lists only Verified lands ---
$r = Call 'POST' "/api/users/$invId/approve" $null $admTok
Check 'S4-54a' 'admin approves the investor -> 204' 204 $r.Status
$r = Call 'GET' '/api/lands?pageSize=50' $null $iTok
$items = @(); if ($r.Status -eq 200) { $items = @(($r.Body | ConvertFrom-Json).items) }
$hasItems = $items.Count -ge 1
$onlyVerified = $hasItems -and (@($items | Where-Object { $_.status -notin @('3','Verified') }).Count -eq 0)
Check 'S4-54b' 'approved investor lists lands: only Verified' '200|True|True' ("$($r.Status)|$hasItems|$onlyVerified")

# --- land Z (single doc) + S4-55 admin status filter ---
$z = $good.Clone(); $z.parcelNumber = "PZ-$stamp"; $z.landNumber = "LZ-$stamp"
$r = Call 'POST' '/api/lands' $z $aTok
Check 'A2-14' 'create land Z (Draft)' 201 $r.Status
$zId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'POST' "/api/lands/$zId/documents/metadata" @{ documentTypeId = 1; filePath = "docs/z-title-$stamp.pdf" } $aTok
Check 'A2-15' 'upload TitleDeed on Z (single type)' 201 $r.Status
$r = Call 'POST' "/api/lands/$zId/submit" $null $aTok
Check 'A2-16' 'submit Z -> PendingVerification' 200 $r.Status

$r = Call 'GET' '/api/lands?status=PendingVerification&pageSize=50' $null $admTok
$zSeen = $false
if ($r.Status -eq 200) { $zSeen = @(($r.Body | ConvertFrom-Json).items | Where-Object { [int]$_.id -eq $zId }).Count -eq 1 }
Check 'S4-55' 'admin ?status=PendingVerification contains Z' '200|True' ("$($r.Status)|$zSeen")

# --- S4-57: versioned criteria; S4-57b/57d double as the A1 regression guards ---
$criteriaBefore = Call 'GET' '/api/criteria/current' $null $saTok
if ($criteriaBefore.Status -ne 200) { throw 'Current criteria unavailable before SuperAdmin replacement' }
$criteriaBody = @{ expectedCurrentId = [int](($criteriaBefore.Body | ConvertFrom-Json).id); minAreaDonum = 20; maxSlopePct = 10; maxGridDistanceKm = 15; minSolarIrradiance = 5; minElevationM = $null; reason = 'Step5 A2: seed-equivalent thresholds' }
$r = Call 'POST' '/api/criteria' $criteriaBody $admTok
Check 'S4-57a-admin' 'regular Admin cannot replace criteria -> 403' 403 $r.Status
$r = Call 'POST' '/api/criteria' $criteriaBody $saTok
Check 'S4-57a' 'SuperAdmin creates criteria replacement -> 201' 201 $r.Status
$v2 = [int]($r.Body | ConvertFrom-Json).id
$curNow = (Call 'GET' '/api/criteria/current' $null $admTok).Body | ConvertFrom-Json
Check 'S4-57b' 'A1 guard: new version becomes current immediately' $v2 ([int]$curNow.id)
$r = Call 'GET' '/api/criteria/history' $null $admTok
# PS 5.1 quirk: ConvertFrom-Json emits a top-level JSON array as ONE Object[] item,
# so wrapping it in @() would nest it (count=1). Keep it as the unwrapped array.
$hist = $r.Body | ConvertFrom-Json
if ($hist -isnot [array]) { $hist = @($hist) }
Check 'S4-57c' 'admin lists criteria history -> 200' 200 $r.Status
$histOk = ($hist.Count -ge 2) -and ([int]($hist[0].id) -eq $v2) -and (@($hist | Where-Object { [int]$_.id -ne $v2 }).Count -ge 1)
Check 'S4-57d' 'A1 guard: history[0]=newest, >=2 versions, old id present' 'True' ([string]$histOk)
$first = $hist[0]
$whoWhenReason = ([int]$first.updatedById -gt 0) -and (-not [string]::IsNullOrWhiteSpace([string]$first.updatedByName)) -and ($null -ne $first.updatedAt) -and (-not [string]::IsNullOrWhiteSpace([string]$first.reason))
Check 'S4-57e' 'history row carries who/when/reason' 'True' ([string]$whoWhenReason)
$r = Call 'PUT' '/api/criteria' @{ reason = 'admin PUT attempt' } $admTok
Check 'S4-57f' 'admin PUT /api/criteria -> 405 (no route)' 405 $r.Status
$r = Call 'DELETE' '/api/criteria' $null $admTok
Check 'S4-57g' 'admin DELETE /api/criteria -> 405 (no route)' 405 $r.Status

# --- S4-58: version pinning ---
$dbv = Sql "SELECT CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $xId;"
Check 'S4-58a' 'X keeps its original criterion id after v2' ("$v1") $dbv
# Phase 1: the landlord no longer supplies technical values, so the new land is accepted and its
# enforced technical criteria are reported as NotAssessed until an assessment is recorded.
$w = $good.Clone(); $w.parcelNumber = "PW-$stamp"; $w.landNumber = "LW-$stamp"
$r = Call 'POST' '/api/lands' $w $aTok
Check 'S4-58b' 'new land accepted under the new current criterion' 201 $r.Status
$wId = [int]($r.Body | ConvertFrom-Json).id
$r = Call 'GET' "/api/lands/$wId/eligibility" $null $aTok
Check 'S4-58c' 'new land is evaluated against v2' ("$v2") ([int](($r.Body | ConvertFrom-Json).criterionId))

# --- S4-59: used document type cannot be deleted ---
$r = Call 'DELETE' '/api/document-types/1' $null $admTok
Check 'S4-59' 'admin deletes a used document type -> 409' 409 $r.Status

# --- S4-61/S4-62: required-flag toggling and the no-vacuous-verification guard ---
try {
  $snapBefore = Sql "SELECT CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $xId;"
  $r = Call 'PUT' '/api/document-types/2' @{ name = 'EncumbranceStatement'; isRequiredForVerification = $false } $admTok
  Check 'S4-62a' 'admin clears the required flag on type 2 -> 200' 200 $r.Status
  $snapAfter = Sql "SELECT CAST(DATA_VERIFIED_BY_ADMIN AS varchar(1)) + '|' + CAST(VERIFIED_AGAINST_CRITERION_ID AS varchar(10)) FROM LANDS WHERE ID = $xId;"
  Check 'S4-62b' 'existing Verified snapshot unchanged by the flag change' $snapBefore $snapAfter
  $r = Call 'PUT' '/api/document-types/1' @{ name = 'TitleDeed'; isRequiredForVerification = $false } $admTok
  Check 'S4-61a' 'admin clears the last required flag -> 200' 200 $r.Status
  $r = Call 'PUT' "/api/lands/$zId/assessment" @{ regionId = $fx.PrimaryRegionId; gridId = $fx.PrimaryGridId; slopePercentage = 5; distanceToGridKm = 10; solarIrradiance = 6; elevationM = 100 } $saTok
  Check 'A2-16b' 'SuperAdmin assesses Z' 200 $r.Status
  $r = Call 'POST' "/api/lands/$zId/verify" $null $admTok
  Check 'S4-61b' 'verify with no required document type -> 409 (no vacuous pass)' 409 $r.Status
}
finally {
  Call 'PUT' '/api/document-types/1' @{ name = 'TitleDeed'; isRequiredForVerification = $true } $admTok | Out-Null
  Call 'PUT' '/api/document-types/2' @{ name = 'EncumbranceStatement'; isRequiredForVerification = $true } $admTok | Out-Null
}
$restored = Sql "SELECT CAST(SUM(CASE WHEN IS_REQUIRED_FOR_VERIFICATION=1 THEN 1 ELSE 0 END) AS varchar(3)) FROM DOCUMENT_TYPES;"
Check 'S4-61c' 'required flags restored (2 required types)' '2' $restored

# --- S4-63: deactivate the disposable admin; login must then fail ---
$r = Call 'DELETE' "/api/users/$admId" $null $saTok
Check 'S4-63a' 'SuperAdmin deactivates the disposable Admin -> 204' 204 $r.Status
$r = Call 'POST' '/api/auth/login' @{ email = $admEmail; password = $pw }
Check 'S4-63b' 'login after deactivation fails -> 401' 401 $r.Status

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
$blk = @($rows | Where-Object { $_.Result -eq 'BLOCKED' }).Count
Write-Output ''
Write-Output "TOTAL PASS=$pass FAIL=$fail BLOCKED=$blk"
if ($fail -gt 0 -or $blk -gt 0) { $suiteExitCode = 1 }
}
catch {
  $exception = $_.Exception
  if ($null -ne $exception.Data -and $exception.Data.Contains('S4ExitCode')) {
    $suiteExitCode = [int]$exception.Data['S4ExitCode']
  }
  else {
    $suiteExitCode = 1
    Write-Output ("FATAL: " + $exception.Message)
  }
}
finally {
  $cleanupIdentity = $harness

  # If Start-TestApiInstance threw after launching the API but before returning the identity,
  # recover only an identity file created by this run. A stale identity is reported and left
  # alone; Stop-TestApiInstance still proves the PID owns the recorded port before stopping it.
  if ($null -eq $cleanupIdentity) {
    $identityPath = Join-Path ([IO.Path]::GetTempPath()) 'nibras-f1-api-identity.json'
    if (Test-Path -LiteralPath $identityPath) {
      try {
        $candidate = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
        if (-not $candidate -or [int]$candidate.Pid -le 0 -or [int]$candidate.Port -le 0 -or [string]::IsNullOrWhiteSpace([string]$candidate.StartedAt)) {
          [void]$cleanupProblems.Add('the API identity file is incomplete after the startup failure')
        }
        else {
          $candidateStarted = [DateTime]::Parse(
            [string]$candidate.StartedAt,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind)
          if ($candidateStarted -ge $runStartedUtc) {
            $cleanupIdentity = $candidate
          }
          else {
            [void]$cleanupProblems.Add('the API identity file predates this run; it was left untouched')
          }
        }
      }
      catch {
        [void]$cleanupProblems.Add(('the API identity file could not be recovered after the startup failure: ' + $_.Exception.Message))
      }
    }
  }

  if ($null -ne $cleanupIdentity) {
    try {
      $cleanupProblems = @(Stop-S4Api -Identity $cleanupIdentity)
    }
    catch {
      $cleanupProblems = @('cleanup helper failed: ' + $_.Exception.Message)
    }

    if ($cleanupProblems.Count -gt 0) {
      foreach ($problem in $cleanupProblems) {
        Write-Output ("WARNING: cleanup failed: " + $problem)
      }
      $suiteExitCode = 1
    }
    else {
      Write-Output ("CLEANUP-OK api_pid={0} port={1}" -f $cleanupIdentity.Pid, $cleanupIdentity.Port)
    }
  }
  elseif ($cleanupProblems.Count -gt 0) {
    foreach ($problem in $cleanupProblems) {
      Write-Output ("WARNING: cleanup failed: " + $problem)
    }
    $suiteExitCode = 1
  }
}
exit $suiteExitCode
