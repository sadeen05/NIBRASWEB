$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
. (Join-Path $PSScriptRoot 'test_target.ps1')
# The target is resolved from the effective connection string and is fail-closed:
# only the disposable database is accepted, and a mismatch stops the run.
$sqlServer = (Get-TestTarget).Server
$dbName = (Get-TestTarget).Database
# The suite owns its instance: the harness starts it on the dedicated port, with the
# explicit disposable connection string, and proves that it owns the port.
$harness = Start-TestApiInstance
$base = Initialize-TestApi
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function Call([string]$Method, [string]$Path, $Body = $null, [string]$Token = $null) {
  $args = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $args.Headers['Authorization'] = 'Bearer ' + $Token }
  if ($null -ne $Body) {
    $args.Body = (ConvertTo-Json -InputObject $Body -Depth 5 -Compress)
    $args.ContentType = 'application/json'
  }
  try {
    $r = Invoke-WebRequest @args
    return [pscustomobject]@{ Status = [int]$r.StatusCode; Body = $r.Content }
  } catch {
    $resp = $_.Exception.Response
    $text = ''
    if ($resp) {
      $sr = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $text = $sr.ReadToEnd()
    }
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $text }
  }
}

function Mask([string]$s) {
  return [regex]::Replace($s, 'eyJ[A-Za-z0-9_\-\.]+', '<token-masked>')
}

function Register($Name, $Email, $RoleId, $Company = $null) {
  $b = @{ fullName = $Name; email = $Email; phone = (New-Phone); password = $pw; roleId = $RoleId }
  if ($Company) { $b.companyName = $Company; $b.companyRegistrationNumber = ('REG-' + $stamp + '-' + $RoleId) }
  $baseline = New-OutboxBaseline
  $r = Call 'POST' '/api/auth/register' $b
  if ($r.Status -ne 200) { throw "register failed ($($r.Status))" }
  # The account can only sign in after its address was confirmed.
  $v = Confirm-VerifiedAccount -Email $Email -Baseline $baseline
  if ($v.Status -ne 204) { throw "the verification of $Email failed ($($v.Status))" }
  return $r
}

function Login($Email) {
  $r = Call 'POST' '/api/auth/login' @{ email = $Email; password = $pw }
  if ($r.Status -ne 200) { throw "login failed with $($r.Status)" }
  return ($r.Body | ConvertFrom-Json).token
}

Write-Output '== setup: disposable users (passwords and tokens are never printed) =='
$laEmail = "s4-$stamp-la@example.test"
$lbEmail = "s4-$stamp-lb@example.test"
$inEmail = "s4-$stamp-in@example.test"
$r = Register 'Step4 Landlord A' $laEmail 1
Write-Output ("register landlord A -> {0}" -f $r.Status)
$r = Register 'Step4 Landlord B' $lbEmail 1
Write-Output ("register landlord B -> {0}" -f $r.Status)
$r = Register 'Step4 Investor' $inEmail 2 'Step4 Co'
Write-Output ("register investor (unapproved) -> {0}" -f $r.Status)
$aToken = Login $laEmail
$bToken = Login $lbEmail
$iToken = Login $inEmail
Write-Output 'logins: landlord A, landlord B, investor -> all 200 (tokens held in memory only)'

# Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
# assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
. (Join-Path $PSScriptRoot 'test_fixtures.ps1')
$fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $dbName
Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"

Write-Output ''
Write-Output '== CHECKPOINT A =='
$create = @{
  governorateId = $fx.GovernorateId; landNumber = "CP-$stamp"; village = 'Village-One'; basin = 'Basin-One'
  parcelNumber = "P-$stamp"; areaDonum = 25
}
$r = Call 'POST' '/api/lands' $create $aToken
Write-Output ("C1 landlord A creates a land -> expected 201, actual {0}" -f $r.Status)
Write-Output ("   response: " + (Mask $r.Body))
$landId = ($r.Body | ConvertFrom-Json).id

$r = Call 'POST' '/api/lands' $create $aToken
Write-Output ("C2 duplicate parcel (same region/village/basin/parcel) -> expected 409, actual {0}" -f $r.Status)
Write-Output ("   response: " + (Mask $r.Body))

$r = Call 'GET' "/api/lands/$landId" $null $bToken
Write-Output ("C3 landlord B GETs landlord A's land -> expected 403, actual {0}" -f $r.Status)
Write-Output ("   response: " + (Mask $r.Body))

$r = Call 'GET' '/api/lands' $null $iToken
Write-Output ("C5 unapproved investor lists lands -> expected 403, actual {0}" -f $r.Status)
Write-Output ("   response: " + (Mask $r.Body))

$r = Call 'GET' '/api/lands' $null $aToken
$own = ($r.Body | ConvertFrom-Json)
Write-Output ("extra: landlord A lists lands -> 200 expected, actual {0}; totalCount={1} (own lands only: all LandlordId match = {2})" -f $r.Status, $own.totalCount, (@($own.items | Where-Object { $_.landlordId -ne ($own.items[0].landlordId) }).Count -eq 0))

Write-Output ("C4 approved investor lists lands (only Verified) -> BLOCKED: no approved Investor fixture; approving needs an Admin token, not available")
Write-Output ("landId for DB checks: $landId")
