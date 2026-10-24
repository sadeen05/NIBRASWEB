#requires -Version 5.1
<#
.SYNOPSIS
    Dedicated C4 acceptance: approved-Investor land visibility.

.DESCRIPTION
    This suite supplies the dedicated C4 positive acceptance that the
    evidence-only `s4_checkpoint_a.ps1` deliberately does not contain. It starts
    its own harness-owned API instance, uses the real public registration,
    FileOutbox verification, login and approval endpoints, and then proves the
    approved Investor's list behaviour.

    The suite is API-only: it performs no SQL and opens no database connection.
    The API itself owns all writes. It creates:
      * one fresh Investor, approved through the real approval endpoint;
      * one fresh Landlord, used only to create a run-owned Draft land.

    It does not create or approve a Verified land. A pre-existing
    investor-visible Verified land is required for a positive C4 result. If no
    such land can be proven, the suite reports BLOCKED and exits 2; it never
    turns an empty list into C4 PASS.

    Tokens and passwords stay in memory. Response bodies are never printed; only
    case IDs, statuses, counts and non-secret IDs are written to the suite output.

.NOTES
    Run only through the protected harness/runner after separate approval.
    Target: SADEEN\SQLEXPRESS / NIBRAS_F1_Verify, API port 5199.
#>

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The helpers own the API base address, the FileOutbox token redemption and the
# identity-verified shutdown. There is no direct database path in this suite.

$harness = $null
$script:C4Pass = 0
$script:C4Fail = 0
$script:C4Blocked = 0
$script:C4CleanupFailure = $null

$stamp = (Get-Date).ToString('yyyyMMddHHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0, 10) }
$adminEmail = $null
$adminPass = $null

function Protect-C4Text([string]$Text) {
  if ($null -eq $Text) { return '' }
  $safe = [string]$Text
  $safe = [regex]::Replace($safe, '(?i)\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '<jwt-redacted>')
  $safe = [regex]::Replace($safe, '(?i)\bBearer[ \t]+[^\s]+', 'Bearer <redacted>')
  $safe = [regex]::Replace($safe, '(?i)\b(password|pwd|token|secret)[ \t]*[=:][ \t]*[^&\s"'']+', '$1=<redacted>')
  return $safe
}

function New-Phone {
  return '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999))
}

function ConvertFrom-C4Json([string]$Text) {
  if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
  try {
    return ($Text | ConvertFrom-Json -ErrorAction Stop)
  }
  catch {
    throw 'the API returned a response that is not valid JSON'
  }
}

function Invoke-C4Api {
  param(
    [Parameter(Mandatory)][string]$Method,
    [Parameter(Mandatory)][string]$Path,
    $Body = $null,
    [string]$Token = $null
  )

  $json = $null
  if ($null -ne $Body) {
    $json = ConvertTo-Json -InputObject $Body -Depth 6 -Compress
  }

  if ($Token) {
    return Invoke-VerificationApi -Method $Method -Path $Path -Json $json -BearerToken $Token
  }
  return Invoke-VerificationApi -Method $Method -Path $Path -Json $json
}

function Add-C4Result([string]$Id, [string]$Result, [string]$Case, [string]$Expected, [string]$Actual, [string]$Note = '') {
  $line = "{0,-7} {1,-7} exp={2,-22} act={3,-22} {4}" -f $Id, $Result, $Expected, $Actual, $Case
  if ($Note) { $line += ' [' + $Note + ']' }
  Write-Output $line
}

function Add-C4Pass([string]$Id, [string]$Case, [string]$Actual, [string]$Note = '') {
  $script:C4Pass++
  Add-C4Result $Id 'PASS' $Case 'observed' $Actual $Note
}

function Add-C4Blocked([string]$Id, [string]$Case, [string]$Expected, [string]$Actual, [string]$Note = '') {
  $script:C4Blocked++
  Add-C4Result $Id 'BLOCKED' $Case $Expected $Actual $Note
}

function Check([string]$Id, [string]$Case, $Expected, $Actual, [string]$Note = '') {
  $exp = [string]$Expected
  $act = [string]$Actual
  if ($exp -eq $act) {
    $script:C4Pass++
    Add-C4Result $Id 'PASS' $Case $exp $act $Note
  }
  else {
    $script:C4Fail++
    Add-C4Result $Id 'FAIL' $Case $exp $act $Note
  }
}

function Get-C4AllLands {
  param(
    [Parameter(Mandatory)][string]$Token,
    [string]$StatusFilter = ''
  )

  $items = New-Object System.Collections.ArrayList
  $page = 1
  $pagesRead = 0
  $totalPages = 1

  while ($true) {
    $path = if ($page -eq 1) {
      if ($StatusFilter) {
        '/api/lands?status=' + [Uri]::EscapeDataString($StatusFilter) + '&pageSize=50'
      }
      else {
        '/api/lands?pageSize=50'
      }
    }
    else {
      if ($StatusFilter) {
        '/api/lands?pageNumber=' + $page + '&pageSize=50&status=' + [Uri]::EscapeDataString($StatusFilter)
      }
      else {
        '/api/lands?pageNumber=' + $page + '&pageSize=50'
      }
    }

    $r = Invoke-C4Api -Method 'GET' -Path $path -Token $Token
    if ([int]$r.Status -ne 200) {
      throw ("land listing page {0} returned {1}, expected 200." -f $page, [int]$r.Status)
    }

    $doc = ConvertFrom-C4Json $r.Body
    if ($null -eq $doc) { throw ("land listing page {0} returned no JSON object." -f $page) }

    foreach ($item in @($doc.items)) {
      if ($null -ne $item) { [void]$items.Add($item) }
    }

    $pagesRead++
    $totalPages = [int]$doc.totalPages
    if ($totalPages -lt 0) { throw 'the land listing returned a negative totalPages value.' }
    if ($totalPages -eq 0 -or $page -ge $totalPages) { break }
    $page++
    if ($page -gt 200) { throw 'the land listing exceeded the C4 pagination safety bound.' }
  }

  return [pscustomobject]@{
    Items      = @($items)
    PagesRead  = $pagesRead
    TotalPages = $totalPages
  }
}

try {
  $harness = Start-TestApiInstance
  $null = Initialize-TestApi

  # Credentials are available in the run environment only after the harness has
  # seeded its disposable SuperAdmin. They are never printed.
  $adminEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
  $adminPass = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
  if (-not $adminEmail -or -not $adminPass) {
    throw 'NIBRAS_TEST_SUPERADMIN_EMAIL / NIBRAS_TEST_SUPERADMIN_PASSWORD are not set.'
  }

  Write-Output '== C4 setup: fresh run-owned identities (secrets are not printed) =='

  # -----------------------------------------------------------------------------------------
  # Fresh Investor: register with company details and verify through the real outbox flow.
  # -----------------------------------------------------------------------------------------
  $investorEmail = "c4-inv-$stamp@example.test"
  $investorBaseline = New-OutboxBaseline
  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/register' -Body @{
    fullName = 'C4 Approved Investor'
    email = $investorEmail
    phone = (New-Phone)
    password = $pw
    roleId = 2
    companyName = 'C4 Solar Company'
    companyRegistrationNumber = "C4-REG-$stamp"
  }
  Check 'C4-01' 'fresh Investor registration' 201 $r.Status
  if ([int]$r.Status -ne 201) { throw ("Investor registration returned {0}." -f [int]$r.Status) }
  $investorDoc = ConvertFrom-C4Json $r.Body
  if ($null -eq $investorDoc -or $null -eq $investorDoc.id) { throw 'Investor registration returned no id.' }
  $investorId = [int]$investorDoc.id

  $v = Confirm-VerifiedAccount -Email $investorEmail -Baseline $investorBaseline
  Check 'C4-02' 'Investor email verification' 204 $v.Status
  if ([int]$v.Status -ne 204) { throw ("Investor verification returned {0}." -f [int]$v.Status) }

  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/login' -Body @{ email = $investorEmail; password = $pw }
  Check 'C4-03' 'unapproved Investor login' 200 $r.Status
  if ([int]$r.Status -ne 200) { throw ("Unapproved Investor login returned {0}." -f [int]$r.Status) }
  $unapprovedToken = (ConvertFrom-C4Json $r.Body).token
  if (-not $unapprovedToken) { throw 'The unapproved Investor login returned no token.' }

  $r = Invoke-C4Api -Method 'GET' -Path '/api/lands' -Token $unapprovedToken
  Check 'C4-04' 'unapproved Investor list is refused' 403 $r.Status
  if ([int]$r.Status -ne 403) { throw ("Unapproved Investor list returned {0}, expected 403." -f [int]$r.Status) }

  # -----------------------------------------------------------------------------------------
  # Fresh Landlord and a run-owned Draft land. The governorate is resolved through the API,
  # not through SQL, and the id is captured from the response.
  # -----------------------------------------------------------------------------------------
  $landlordEmail = "c4-la-$stamp@example.test"
  $landlordBaseline = New-OutboxBaseline
  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/register' -Body @{
    fullName = 'C4 Draft Landlord'
    email = $landlordEmail
    phone = (New-Phone)
    password = $pw
    roleId = 1
  }
  if ([int]$r.Status -ne 201) { throw ("Landlord registration returned {0}." -f [int]$r.Status) }
  $landlordDoc = ConvertFrom-C4Json $r.Body
  if ($null -eq $landlordDoc -or $null -eq $landlordDoc.id) { throw 'Landlord registration returned no id.' }
  $landlordId = [int]$landlordDoc.id

  $v = Confirm-VerifiedAccount -Email $landlordEmail -Baseline $landlordBaseline
  if ([int]$v.Status -ne 204) { throw ("Landlord verification returned {0}." -f [int]$v.Status) }

  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/login' -Body @{ email = $landlordEmail; password = $pw }
  if ([int]$r.Status -ne 200) { throw ("Landlord login returned {0}." -f [int]$r.Status) }
  $landlordToken = (ConvertFrom-C4Json $r.Body).token
  if (-not $landlordToken) { throw 'The Landlord login returned no token.' }

  $r = Invoke-C4Api -Method 'GET' -Path '/api/governorates' -Token $landlordToken
  if ([int]$r.Status -ne 200) { throw ("Governorate lookup returned {0}." -f [int]$r.Status) }
  $governorates = @(ConvertFrom-C4Json $r.Body)
  $amman = @($governorates | Where-Object { $null -ne $_ -and [string]$_.code -eq 'AM' })
  if ($amman.Count -ne 1) { throw 'The run requires exactly one governorate with code AM.' }
  $governorateId = [int]$amman[0].id

  $r = Invoke-C4Api -Method 'POST' -Path '/api/lands' -Token $landlordToken -Body @{
    governorateId = $governorateId
    landNumber = "C4-DRAFT-$stamp"
    village = 'C4 Village'
    basin = 'C4 Basin'
    parcelNumber = "C4-P-$stamp"
    areaDonum = 25
  }
  Check 'C4-14' 'run-owned Draft land creation' 201 $r.Status
  if ([int]$r.Status -ne 201) { throw ("Draft land creation returned {0}." -f [int]$r.Status) }
  $draftDoc = ConvertFrom-C4Json $r.Body
  if ($null -eq $draftDoc -or $null -eq $draftDoc.id) { throw 'Draft land creation returned no id.' }
  $draftId = [int]$draftDoc.id
  $draftState = ('{0}|{1}' -f [string]$draftDoc.status, ([int]$draftDoc.landlordId -eq $landlordId))
  Check 'C4-15' 'Draft fixture is Draft and owned by the run Landlord' 'Draft|True' $draftState
  if ($draftState -ne 'Draft|True') { throw 'The run-owned Draft fixture is not in the required state.' }

  # -----------------------------------------------------------------------------------------
  # Existing disposable SuperAdmin: login, read back the exact new Investor, approve only it.
  # -----------------------------------------------------------------------------------------
  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/login' -Body @{ email = $adminEmail; password = $adminPass }
  Check 'C4-05' 'disposable SuperAdmin login' 200 $r.Status
  if ([int]$r.Status -ne 200) { throw ("SuperAdmin login returned {0}." -f [int]$r.Status) }
  $adminToken = (ConvertFrom-C4Json $r.Body).token
  if (-not $adminToken) { throw 'The SuperAdmin login returned no token.' }

  $r = Invoke-C4Api -Method 'POST' -Path ("/api/users/{0}/approve" -f $investorId) -Token $adminToken
  Check 'C4-06' 'approve the exact newly registered Investor' 204 $r.Status
  if ([int]$r.Status -ne 204) { throw ("Investor approval returned {0}." -f [int]$r.Status) }

  $r = Invoke-C4Api -Method 'GET' -Path ("/api/users/{0}" -f $investorId) -Token $adminToken
  Check 'C4-07' 'read back the approved Investor through the API' 200 $r.Status
  if ([int]$r.Status -ne 200) { throw ("Investor read-back returned {0}." -f [int]$r.Status) }
  $readBack = ConvertFrom-C4Json $r.Body
  if ($null -eq $readBack) { throw 'Investor read-back returned no JSON object.' }
  $readBackState = ('{0}|{1}' -f [bool]$readBack.isApproved, [string]$readBack.role)
  Check 'C4-08' 'read-back confirms approved Investor state' 'True|Investor' $readBackState
  if ($readBackState -ne 'True|Investor') { throw 'Investor read-back does not confirm the approved Investor state.' }

  $r = Invoke-C4Api -Method 'POST' -Path '/api/auth/login' -Body @{ email = $investorEmail; password = $pw }
  Check 'C4-09' 'approved Investor re-login' 200 $r.Status
  if ([int]$r.Status -ne 200) { throw ("Approved Investor login returned {0}." -f [int]$r.Status) }
  $approvedToken = (ConvertFrom-C4Json $r.Body).token
  if (-not $approvedToken) { throw 'The approved Investor login returned no token.' }

  # Discover the pre-existing Verified candidates as SuperAdmin. This list can include hidden
  # or outstanding-evidence lands; membership in the Investor list below is what proves
  # that a candidate is actually eligible and investor-visible.
  $adminVerified = Get-C4AllLands -Token $adminToken -StatusFilter 'Verified'
  $candidateIds = @($adminVerified.Items | ForEach-Object { [int]$_.id } | Sort-Object -Unique)

  # -----------------------------------------------------------------------------------------
  # C4: unfiltered Investor list; every page, every item Verified; known Draft excluded;
  # at least one known eligible Verified land included.
  # -----------------------------------------------------------------------------------------
  $r = Invoke-C4Api -Method 'GET' -Path '/api/lands?pageSize=50' -Token $approvedToken
  Check 'C4-10' 'approved Investor unfiltered land list' 200 $r.Status
  if ([int]$r.Status -ne 200) { throw ("Approved Investor list returned {0}." -f [int]$r.Status) }
  $firstPage = ConvertFrom-C4Json $r.Body
  if ($null -eq $firstPage) { throw 'The approved Investor list returned no JSON object.' }

  $allLands = New-Object System.Collections.ArrayList
  foreach ($item in @($firstPage.items)) {
    if ($null -ne $item) { [void]$allLands.Add($item) }
  }
  $totalPages = [int]$firstPage.totalPages
  if ($totalPages -lt 0) { throw 'The approved Investor list returned a negative totalPages value.' }
  $pagesRead = 1
  $page = 1
  while ($page -lt $totalPages) {
    $page++
    if ($page -gt 200) { throw 'The approved Investor list exceeded the C4 pagination safety bound.' }
    $path = '/api/lands?pageNumber=' + $page + '&pageSize=50'
    $r = Invoke-C4Api -Method 'GET' -Path $path -Token $approvedToken
    if ([int]$r.Status -ne 200) { throw ("Approved Investor list page {0} returned {1}." -f $page, [int]$r.Status) }
    $pageDoc = ConvertFrom-C4Json $r.Body
    if ($null -eq $pageDoc) { throw ("Approved Investor list page {0} returned no JSON object." -f $page) }
    foreach ($item in @($pageDoc.items)) {
      if ($null -ne $item) { [void]$allLands.Add($item) }
    }
    $pagesRead++
  }

  $landItems = @($allLands)
  $nonVerified = @($landItems | Where-Object { [string]$_.status -ne 'Verified' })
  Check 'C4-11' 'every land on every page is Verified' 0 $nonVerified.Count ("pages=$pagesRead; items=$($landItems.Count)")
  if ($nonVerified.Count -gt 0) {
    # The IDs are useful non-secret evidence, but never the full response body.
    $ids = @($nonVerified | ForEach-Object { [int]$_.id } | Sort-Object -Unique) -join ','
    Write-Output ("C4-11 observed non-Verified land id(s): {0}" -f $ids)
  }

  $draftInList = @($landItems | Where-Object { [int]$_.id -eq $draftId }).Count
  Check 'C4-12' 'known run-owned Draft is excluded' 0 $draftInList ("draftId=$draftId")

  $visibleCandidates = @($landItems | Where-Object { $candidateIds -contains [int]$_.id })
  if ($candidateIds.Count -eq 0) {
    Add-C4Blocked 'C4-13' 'at least one known eligible Verified land is visible' '>=1' '0' 'no pre-existing Verified land was discoverable'
  }
  elseif ($visibleCandidates.Count -eq 0) {
    Add-C4Blocked 'C4-13' 'at least one known eligible Verified land is visible' '>=1' '0' ("known candidates=$($candidateIds.Count); none was investor-visible")
  }
  else {
    $firstVisibleId = [int]$visibleCandidates[0].id
    Add-C4Pass 'C4-13' 'at least one known eligible Verified land is visible' ([string]$visibleCandidates.Count) ("known candidates=$($candidateIds.Count); firstVisibleId=$firstVisibleId")
  }
}
catch {
  $message = Protect-C4Text $_.Exception.Message
  Add-C4Blocked 'C4-SETUP' 'the full C4 flow completed' 'complete' 'blocked' $message
}
finally {
  try {
    Complete-TestApiCleanup -Identity $harness
  }
  catch {
    $script:C4CleanupFailure = Protect-C4Text $_.Exception.Message
    Write-Output ("WARNING: cleanup failed: {0}" -f $script:C4CleanupFailure)
  }
}

Write-Output ''
Write-Output ("C4 TOTAL PASS={0} FAIL={1} BLOCKED={2}" -f $script:C4Pass, $script:C4Fail, $script:C4Blocked)

if ($script:C4CleanupFailure) { exit 3 }
if ($script:C4Fail -gt 0) { exit 1 }
if ($script:C4Blocked -gt 0) { exit 2 }
exit 0
