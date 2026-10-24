#requires -Version 5.1
# F1 - focused acceptance suite for the three remaining F1 gaps:
#   * authenticated, confirmed address change (single use, cooldown, daily cap, stale JWT);
#   * durable investor-approval eligibility, actor attribution and append-only history;
#   * real land-document upload, private storage, authenticated download and validation.
#
# The suite drives the REAL API. It does not write verification or approval state directly:
# accounts are verified through the outbox link and the public endpoint, approvals go through
# the public approval endpoint, and documents go through the multipart endpoint. The only
# database access is READ-ONLY: audit counts, token TTLs and the absence of an active address
# are inspected, never changed.
#
# Requirements:
#   * the harness-owned API instance against the disposable NIBRAS_F1_Verify database;
#   * a Development instance (the legacy metadata upload and the FileOutbox are Development
#     only), on the harness port;
#   * migrations through 20261021224950_AddGovernanceHistorySnapshots applied (18 total);
#   * -IncludeGovernance requires separate approval for uniquely marked criteria/Admin fixtures;
#   * NIBRAS_TEST_PASSWORD (optional), otherwise a runtime-generated password is used.
#
# Configuration owned by this suite (set before the harness starts):
#   Email__ResendCooldownSeconds=5, Email__ResendDailyCap=4
#   RateLimit__EmailChangePerMinute=1000
#   Documents__StorageRoot=<private durable run directory outside Git/public roots>
#   Documents__MaxFileSizeBytes=1024
#
# Usage:  powershell -File f1_email_change_approval_documents.ps1 [-SelfTest]
#
# A time-travel expiry test and an in-flight cancellation test are reported as SKIP: neither
# can be made deterministic without a test-only clock or a slow test mailer. The read-only TTL
# check below still proves the one-hour expiry is persisted; the rejection branch is covered by
# code review and by the existing expired/used-token tests for the shared token service.

param([switch]$SelfTest, [switch]$IncludeGovernance)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 returns a JSON array as one Object[] instead of enumerating it. A
# one-item array may also be unrolled to a single object. This helper normalizes both shapes
# before the suite reads an id; it deliberately rejects objects, nested arrays and malformed
# payloads instead of guessing.
function ConvertFrom-JsonArray {
  param([AllowNull()][string]$Json)

  if ([string]::IsNullOrWhiteSpace($Json)) {
    return @()
  }

  $trimmed = $Json.Trim()
  if (-not $trimmed.StartsWith('[')) {
    throw 'expected a JSON array response'
  }

  try {
    $parsed = $trimmed | ConvertFrom-Json -ErrorAction Stop
  }
  catch {
    throw "malformed JSON array response: $($_.Exception.Message)"
  }

  if ($null -eq $parsed) {
    return @()
  }

  $items = @($parsed)
  foreach ($item in $items) {
    if ($null -eq $item -or $item -is [System.Array]) {
      throw 'ambiguous nested JSON array response'
    }
  }

  return $items
}

function Get-RequiredPositiveId {
  param([object]$Item, [string]$Label)

  if ($null -eq $Item) {
    throw "$Label is missing"
  }

  $property = $Item.PSObject.Properties['id']
  if ($null -eq $property) {
    throw "$Label has no id"
  }

  $raw = $property.Value
  if ($null -eq $raw -or $raw -is [System.Array]) {
    throw "$Label id is missing or ambiguous"
  }

  $id = 0
  if (-not [int]::TryParse([string]$raw, [ref]$id) -or $id -le 0) {
    throw "$Label id '$raw' is not a positive integer"
  }

  return $id
}

function Get-AmmanGovernorateId {
  param([string]$Body)

  $items = @(ConvertFrom-JsonArray $Body)
  if ($items.Count -eq 0) {
    throw 'the governorate response was empty'
  }

  $amman = @($items | Where-Object {
    $null -ne $_ -and $null -ne $_.PSObject.Properties['code'] -and [string]$_.PSObject.Properties['code'].Value -eq 'AM'
  })
  if ($amman.Count -ne 1) {
    throw "expected exactly one governorate with code AM; found $($amman.Count)"
  }

  return Get-RequiredPositiveId -Item $amman[0] -Label 'the Amman governorate'
}

function Get-FirstDocumentTypeId {
  param([string]$Body)

  $items = @(ConvertFrom-JsonArray $Body)
  if ($items.Count -eq 0) {
    throw 'the document-type response was empty'
  }

  return Get-RequiredPositiveId -Item $items[0] -Label 'the first document type'
}

function Assert-JsonSelfTest {
  param([string]$Name, [bool]$Condition, [ref]$PassCount, [ref]$FailCount)

  if ($Condition) {
    $PassCount.Value++
  }
  else {
    $FailCount.Value++
  }

  Write-Host ("{0,-72} {1}" -f $Name, $(if ($Condition) { 'PASS' } else { 'FAIL' }))
}

function Invoke-JsonParsingSelfTest {
  $pass = 0
  $fail = 0

  $one = @(ConvertFrom-JsonArray '[{"id":1,"code":"AM"}]')
  Assert-JsonSelfTest 'one-item JSON array normalizes to one object' ($one.Count -eq 1 -and (Get-RequiredPositiveId -Item $one[0] -Label 'one-item') -eq 1) ([ref]$pass) ([ref]$fail)

  $multi = @(ConvertFrom-JsonArray '[{"id":1,"code":"AM"},{"id":2,"code":"IR"}]')
  Assert-JsonSelfTest 'multi-item JSON array normalizes to two objects' ($multi.Count -eq 2 -and (Get-AmmanGovernorateId '[{"id":1,"code":"AM"},{"id":2,"code":"IR"}]') -eq 1) ([ref]$pass) ([ref]$fail)

  $empty = @(ConvertFrom-JsonArray '[]')
  Assert-JsonSelfTest 'empty JSON array normalizes to zero objects' ($empty.Count -eq 0) ([ref]$pass) ([ref]$fail)

  $malformedFailed = $false
  try { $null = ConvertFrom-JsonArray '[{"id":1' } catch { $malformedFailed = $true }
  Assert-JsonSelfTest 'malformed JSON fails closed' $malformedFailed ([ref]$pass) ([ref]$fail)

  $objectFailed = $false
  try { $null = ConvertFrom-JsonArray '{"id":1,"code":"AM"}' } catch { $objectFailed = $true }
  Assert-JsonSelfTest 'a single JSON object is rejected as ambiguous' $objectFailed ([ref]$pass) ([ref]$fail)

  $nestedFailed = $false
  try { $null = ConvertFrom-JsonArray '[[{"id":1,"code":"AM"}]]' } catch { $nestedFailed = $true }
  Assert-JsonSelfTest 'a nested JSON array is rejected as ambiguous' $nestedFailed ([ref]$pass) ([ref]$fail)

  $wrongCodeFailed = $false
  try { $null = Get-AmmanGovernorateId '[{"id":2,"code":"IR"}]' } catch { $wrongCodeFailed = $true }
  Assert-JsonSelfTest 'the governorate code must be AM' $wrongCodeFailed ([ref]$pass) ([ref]$fail)

  $duplicateFailed = $false
  try { $null = Get-AmmanGovernorateId '[{"id":1,"code":"AM"},{"id":2,"code":"AM"}]' } catch { $duplicateFailed = $true }
  Assert-JsonSelfTest 'multiple Amman entries fail closed' $duplicateFailed ([ref]$pass) ([ref]$fail)

  $badIdFailed = $false
  try { $null = Get-AmmanGovernorateId '[{"id":"x","code":"AM"}]' } catch { $badIdFailed = $true }
  Assert-JsonSelfTest 'a non-numeric governorate id fails closed' $badIdFailed ([ref]$pass) ([ref]$fail)

  $emptyGovernorateFailed = $false
  try { $null = Get-AmmanGovernorateId '[]' } catch { $emptyGovernorateFailed = $true }
  Assert-JsonSelfTest 'an empty governorate response fails closed' $emptyGovernorateFailed ([ref]$pass) ([ref]$fail)

  $documentTypeId = Get-FirstDocumentTypeId '[{"id":7,"name":"Deed"},{"id":8,"name":"Map"}]'
  Assert-JsonSelfTest 'the first document type id is a scalar' ($documentTypeId -eq 7) ([ref]$pass) ([ref]$fail)

  $emptyDocumentTypeFailed = $false
  try { $null = Get-FirstDocumentTypeId '[]' } catch { $emptyDocumentTypeFailed = $true }
  Assert-JsonSelfTest 'an empty document-type response fails closed' $emptyDocumentTypeFailed ([ref]$pass) ([ref]$fail)

  $malformedDocumentTypeFailed = $false
  try { $null = Get-FirstDocumentTypeId '[{"id":7' } catch { $malformedDocumentTypeFailed = $true }
  Assert-JsonSelfTest 'a malformed document-type response fails closed' $malformedDocumentTypeFailed ([ref]$pass) ([ref]$fail)

  Write-Host ("JSON_SELF_TEST PASS={0} FAIL={1}" -f $pass, $fail)
  return $fail
}

if ($SelfTest) {
  $selfTestFailures = Invoke-JsonParsingSelfTest
  if ($selfTestFailures -gt 0) {
    exit 1
  }
  exit 0
}

Add-Type -AssemblyName System.Net.Http | Out-Null
. (Join-Path $PSScriptRoot 'test_target.ps1')
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
. (Join-Path $PSScriptRoot 'f1_storage_helpers.ps1')
. (Join-Path $PSScriptRoot 'f1_governance_assertions.ps1')

$testTarget = Get-TestTarget
$script:SqlServer = $testTarget.Server
$script:Db = $testTarget.Database

$script:pass = 0
$script:fail = 0
$script:skip = 0
$script:lines = New-Object System.Collections.Generic.List[string]

function Log([string]$Text) {
  $script:lines.Add($Text)
  Write-Host $Text
}

function Stop-F1Assertion {
  # The assertion already counted its failure. Throw through the suite's catch/finally;
  # never exit here, and never count the same assertion again as a fatal harness error.
  $errorRecord = New-Object System.Management.Automation.ErrorRecord(
    ([InvalidOperationException]::new('An acceptance assertion failed; acceptance stopped.')),
    'F1AssertionFailed', [Management.Automation.ErrorCategory]::InvalidResult, $null)
  throw $errorRecord
}

function Assert-Exact {
  param([string]$Name, [int]$Expected, [object]$Response)
  $ok = ($Response.Status -eq $Expected)
  if ($ok) { $script:pass++ } else { $script:fail++ }
  Log ("{0,-70} exp={1,-4} got={2,-4} {3}" -f $Name, $Expected, $Response.Status, $(if ($ok) { 'PASS' } else { 'FAIL' }))
  if (-not $ok) { Stop-F1Assertion }
  return $ok
}

function Assert-OneOf {
  param([string]$Name, [int[]]$Expected, [object]$Response)
  $ok = ($Expected -contains ([int]$Response.Status))
  if ($ok) { $script:pass++ } else { $script:fail++ }
  Log ("{0,-70} exp={1,-8} got={2,-4} {3}" -f $Name, ($Expected -join '/'), $Response.Status, $(if ($ok) { 'PASS' } else { 'FAIL' }))
  if (-not $ok) { Stop-F1Assertion }
  return $ok
}

function Assert-True {
  param([string]$Name, [bool]$Condition)
  if ($Condition) { $script:pass++ } else { $script:fail++ }
  Log ("{0,-70} {1}" -f $Name, $(if ($Condition) { 'PASS' } else { 'FAIL' }))
  if (-not $Condition) { Stop-F1Assertion }
}

function Skip([string]$Name, [string]$Reason) {
  $script:skip++
  Log ("{0,-70} SKIP ({1})" -f $Name, $Reason)
}

function Sql([string]$Query) {
  $out = & sqlcmd -S $script:SqlServer -E -d $script:Db -b -W -h -1 -Q ("SET NOCOUNT ON; " + $Query) 2>&1
  if ($LASTEXITCODE -ne 0) { throw "read-only SQL failed: $out" }
  return (@($out | Where-Object { $_ }) -join "`n").Trim()
}

function JsonField([string]$Body, [string]$Name) {
  try { return ($Body | ConvertFrom-Json).$Name } catch { return $null }
}

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function New-AccountJson {
  param([string]$FullName, [string]$Email, [int]$RoleId, [string]$CompanyName, [string]$CompanyReg)
  return (@{
    fullName = $FullName
    email = $Email
    phone = (New-Phone)
    password = $script:TestPassword
    roleId = $RoleId
    companyName = $CompanyName
    companyRegistrationNumber = $CompanyReg
  } | ConvertTo-Json -Compress)
}

function Invoke-Login {
  param([string]$Email, [string]$Password)
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{
    email = $Email
    password = $Password
  } | ConvertTo-Json -Compress)
}

function Request-EmailChange {
  param([string]$NewEmail, [string]$Password, [string]$BearerToken)
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/change-email' -BearerToken $BearerToken -Json (@{
    newEmail = $NewEmail
    currentPassword = $Password
  } | ConvertTo-Json -Compress)
}

function Confirm-EmailChange {
  param([string]$Token)
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/confirm-email-change' -Json (@{
    token = $Token
  } | ConvertTo-Json -Compress)
}

function Register-ConfirmedAccount {
  param(
    [string]$Label,
    [string]$Email,
    [int]$RoleId,
    [string]$CompanyName = '',
    [string]$CompanyReg = ''
  )

  $baseline = New-OutboxBaseline
  $register = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' `
    -Json (New-AccountJson -FullName $Label -Email $Email -RoleId $RoleId -CompanyName $CompanyName -CompanyReg $CompanyReg)
  Assert-Exact "$Label registers (201)" 201 $register | Out-Null
  if ($register.Status -ne 201) { throw "registration of '$Label' failed" }

  Assert-True "$Label reports the verification mail as handed over" ((JsonField $register.Body 'verification') -eq 'handed_over')
  $token = Get-VerificationTokenFromOutbox -Email $Email -Baseline $baseline
  $confirm = Complete-EmailVerification -Token $token
  Assert-Exact "$Label verifies the address (204)" 204 $confirm | Out-Null
  if ($confirm.Status -ne 204) { throw "verification of '$Label' failed" }
  return $register
}

function UserEventCount {
  param([int]$UserId, [string]$EventType)
  return [int](Sql ("SELECT COUNT(*) FROM USER_SECURITY_EVENTS WHERE USER_ID = {0} AND EVENT_TYPE = N'{1}';" -f $UserId, $EventType))
}

function Get-StoredFileCount {
  if (-not (Test-Path -LiteralPath $script:StorageRoot)) { return 0 }
  return @(Get-ChildItem -LiteralPath $script:StorageRoot -Recurse -File -ErrorAction SilentlyContinue).Count
}

function Invoke-MultipartApi {
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][int]$DocumentTypeId,
    [Parameter(Mandatory)][string]$FileName,
    [Parameter(Mandatory)][string]$ContentType,
    [Parameter(Mandatory)][byte[]]$Bytes,
    [string]$BearerToken
  )

  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  $multipart = New-Object System.Net.Http.MultipartFormDataContent
  $request = New-Object System.Net.Http.HttpRequestMessage
  try {
    if ($BearerToken) {
      $client.DefaultRequestHeaders.Authorization =
        New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $BearerToken)
    }
    $typeField = New-Object System.Net.Http.StringContent([string]$DocumentTypeId, [System.Text.Encoding]::UTF8, 'text/plain')
    [void]$multipart.Add($typeField, 'documentTypeId')
    $fileContent = New-Object System.Net.Http.ByteArrayContent -ArgumentList (,$Bytes)
    $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
    [void]$multipart.Add($fileContent, 'file', $FileName)

    $request.Method = [System.Net.Http.HttpMethod]::Post
    $request.RequestUri = Get-VerificationApiUri -Path $Path
    $request.Content = $multipart
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
  } finally {
    $request.Dispose()
    $multipart.Dispose()
    $client.Dispose()
  }
}

function Invoke-BlobApi {
  param([Parameter(Mandatory)][string]$Path, [string]$BearerToken)

  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(60)
  $request = New-Object System.Net.Http.HttpRequestMessage
  try {
    if ($BearerToken) {
      $client.DefaultRequestHeaders.Authorization =
        New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $BearerToken)
    }
    $request.Method = [System.Net.Http.HttpMethod]::Get
    $request.RequestUri = Get-VerificationApiUri -Path $Path
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $bytes = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    $contentType = if ($response.Content.Headers.ContentType) { $response.Content.Headers.ContentType.MediaType } else { $null }
    $disposition = if ($response.Content.Headers.ContentDisposition) { $response.Content.Headers.ContentDisposition.ToString() } else { $null }
    return [pscustomobject]@{
      Status = [int]$response.StatusCode
      Bytes = [byte[]]$bytes
      ContentType = $contentType
      Disposition = $disposition
    }
  } finally {
    $request.Dispose()
    $client.Dispose()
  }
}

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$run = [guid]::NewGuid().ToString('N').Substring(0, 8)
$script:TestPassword = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0, 12) }
$script:StorageRoot = $null
$script:StorageReceipt = $null
$script:StorageApiStopped = $true
$script:StorageCheckAttempted = $false

$env:Email__ResendCooldownSeconds = '5'
$env:Email__ResendDailyCap = '4'
$env:RateLimit__EmailChangePerMinute = '1000'
$env:Documents__MaxFileSizeBytes = '1024'

$script:Harness = $null
$script:CleanupAttempted = $false

try {
  $script:StorageReceipt = New-F1RunStorage -RepositoryRoot $script:TestRepoRoot -LandNumber ("F1-$stamp-$run")
  $script:StorageRoot = $script:StorageReceipt.StorageRoot
  $env:Documents__StorageRoot = $script:StorageRoot
  Log ("Retained run storage receipt: {0}" -f (Join-Path $script:StorageReceipt.RunDirectory 'storage-receipt.json'))
  $script:StorageApiStopped = $false
  $script:Harness = Start-TestApiInstance
  $null = Initialize-TestApi
  $outbox = $env:NIBRAS_EMAIL_OUTBOX

  $aEmail0 = "f1.$stamp.$run.change.a0@example.test"
  $aEmail1 = "f1.$stamp.$run.change.a1@example.test"
  $aEmail2 = "f1.$stamp.$run.change.a2@example.test"
  $aEmail3 = "f1.$stamp.$run.change.a3@example.test"
  $aEmail4 = "f1.$stamp.$run.change.a4@example.test"
  $bEmail = "f1.$stamp.$run.change.b@example.test"
  $bNewEmail = "f1.$stamp.$run.change.b.new@example.test"
  $cEmail = "f1.$stamp.$run.change.c@example.test"
  $dEmail = "f1.$stamp.$run.change.d@example.test"

  # -------------------------------------------------------------------------------------------
  Log '--- email change: registration and the unchanged/conflict/password guards ---'
  $aReg = Register-ConfirmedAccount -Label 'the address-change investor' -Email $aEmail0 -RoleId 2 `
    -CompanyName 'Nibras Test Investor' -CompanyReg "REG-$run"
  $aId = [int](JsonField $aReg.Body 'id')
  Assert-True 'the registered investor has an account id' ($aId -gt 0)

  $bReg = Register-ConfirmedAccount -Label 'the conflict investor' -Email $bEmail -RoleId 2 `
    -CompanyName 'Nibras Conflict Investor' -CompanyReg "REG-B-$run"
  $bId = [int](JsonField $bReg.Body 'id')

  $cReg = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' `
    -Json (New-AccountJson -FullName 'the unverified investor' -Email $cEmail -RoleId 2 `
      -CompanyName 'Nibras Unverified Investor' -CompanyReg "REG-C-$run")
  Assert-Exact 'the unverified investor registers (201)' 201 $cReg | Out-Null
  $cId = [int](JsonField $cReg.Body 'id')
  $cVerificationToken = Get-VerificationTokenFromOutbox -Email $cEmail -OutboxPath $outbox

  $dReg = Register-ConfirmedAccount -Label 'the document landlord' -Email $dEmail -RoleId 1
  $dId = [int](JsonField $dReg.Body 'id')

  $aLogin = Invoke-Login -Email $aEmail0 -Password $script:TestPassword
  Assert-Exact 'the investor signs in before the change (200)' 200 $aLogin | Out-Null
  if ($aLogin.Status -ne 200) { throw 'the address-change investor cannot sign in' }
  $aToken = [string](JsonField $aLogin.Body 'token')

  $saEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
  $saPassword = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
  if (-not $saEmail -or -not $saPassword) { throw 'the harness did not provide the disposable SuperAdmin credentials' }
  $saLogin = Invoke-Login -Email $saEmail -Password $saPassword
  Assert-Exact 'the SuperAdmin signs in (200)' 200 $saLogin | Out-Null
  if ($saLogin.Status -ne 200) { throw 'the SuperAdmin cannot sign in' }
  $saBody = $saLogin.Body | ConvertFrom-Json
  $saToken = [string]$saBody.token
  $saId = [int]$saBody.user.id
  $saName = [string]$saBody.user.fullName

  Log '--- D3/D5: administrator-created accounts must prove email ownership ---'
  foreach ($createdRole in @(1,2)) {
    $createdEmail = "f1.$stamp.$run.admin-created.$createdRole@example.test"
    $baseline = New-OutboxBaseline
    $createBody = New-AccountJson -FullName "F1 admin-created role $createdRole" -Email $createdEmail -RoleId $createdRole -CompanyName 'F1 created investor' -CompanyReg "CREATED-$run-$createdRole"
    $created = Invoke-VerificationApi -Method 'POST' -Path '/api/users' -BearerToken $saToken -Json $createBody
    Assert-Exact "admin-created role $createdRole is created" 201 $created | Out-Null
    if ($created.Status -ne 201) { throw 'admin-created verification fixture was not created' }
    $createdId = Get-RequiredPositiveId -Item ($created.Body | ConvertFrom-Json) -Label 'admin-created account'
    $before = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$createdId") -BearerToken $saToken
    Assert-Exact 'admin-created account details are authorized' 200 $before | Out-Null
    Assert-True 'admin-created account starts explicitly unverified' ((JsonField $before.Body 'emailVerified') -eq $false)
    $blockedLogin = Invoke-Login -Email $createdEmail -Password $script:TestPassword
    Assert-Exact 'admin-created unverified account cannot log in' 403 $blockedLogin | Out-Null
    Assert-True 'creation is audited once for the admin-created account' ((UserEventCount -UserId $createdId -EventType 'AdminCreatedUnverified') -eq 1)
    $createdToken = Get-VerificationTokenFromOutbox -Email $createdEmail -Baseline $baseline
    $confirmed = Complete-EmailVerification -Token $createdToken
    Assert-Exact 'admin-created account confirms ownership through the public endpoint' 204 $confirmed | Out-Null
    $after = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$createdId") -BearerToken $saToken
    Assert-Exact 'confirmed admin-created account details remain authorized' 200 $after | Out-Null
    Assert-True 'confirmation verifies the admin-created address' ((JsonField $after.Body 'emailVerified') -eq $true -and (JsonField $after.Body 'email') -eq $createdEmail)
    $createdLogin = Invoke-Login -Email $createdEmail -Password $script:TestPassword
    Assert-Exact 'confirmed admin-created account can log in' 200 $createdLogin | Out-Null
    Assert-True 'confirmation is audited for the admin-created account' ((UserEventCount -UserId $createdId -EventType 'EmailVerified') -eq 1)
  }

  Log '--- D4: an administrator cannot directly overwrite a verified address ---'
  $verifiedDetail = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId") -BearerToken $saToken
  Assert-Exact 'the verified investor details are readable before a direct edit' 200 $verifiedDetail | Out-Null
  if ($verifiedDetail.Status -ne 200) { throw 'verified account details unavailable' }
  $verifiedUser = $verifiedDetail.Body | ConvertFrom-Json
  $directEdit = Invoke-VerificationApi -Method 'PUT' -Path ("/api/users/$aId") -BearerToken $saToken -Json (@{
    fullName=$verifiedUser.fullName; email="f1.$stamp.$run.forbidden-edit@example.test"; phone=$verifiedUser.phone
  } | ConvertTo-Json -Compress)
  Assert-Exact 'direct administrator overwrite of a verified email is refused' 409 $directEdit | Out-Null
  $unchangedUser = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId") -BearerToken $saToken
  Assert-Exact 'the investor remains readable after the refused edit' 200 $unchangedUser | Out-Null
  Assert-True 'the refused edit preserves email and verification epoch' ((JsonField $unchangedUser.Body 'email') -eq $verifiedUser.email -and (JsonField $unchangedUser.Body 'emailVerified') -eq $true -and (JsonField $unchangedUser.Body 'emailVerifiedAt') -eq $verifiedUser.emailVerifiedAt)
  $sessionAfterEdit = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $aToken
  Assert-Exact 'the refused edit leaves the existing session valid' 200 $sessionAfterEdit | Out-Null
  $profileEdit = Invoke-VerificationApi -Method 'PUT' -Path ("/api/users/$aId") -BearerToken $saToken -Json (@{
    fullName=("$($verifiedUser.fullName) profile"); email=$verifiedUser.email; phone=$verifiedUser.phone
  } | ConvertTo-Json -Compress)
  Assert-Exact 'same-email profile update succeeds' 204 $profileEdit | Out-Null
  $profileRead = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId") -BearerToken $saToken
  Assert-Exact 'updated profile is readable' 200 $profileRead | Out-Null
  Assert-True 'same-email edit changes profile without changing verified identity or role' ((JsonField $profileRead.Body 'fullName') -eq ("$($verifiedUser.fullName) profile") -and (JsonField $profileRead.Body 'email') -eq $verifiedUser.email -and (JsonField $profileRead.Body 'emailVerifiedAt') -eq $verifiedUser.emailVerifiedAt -and (JsonField $profileRead.Body 'emailVerified') -eq $true -and (JsonField $profileRead.Body 'role') -eq $verifiedUser.role)
  $profileSession = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $aToken
  Assert-Exact 'same-email profile update preserves the existing session' 200 $profileSession | Out-Null
  # The registration itself created a token. Wait out the suite's five-second cooldown before
  # the first address-change request so the cooldown assertion below measures that request.
  Start-Sleep -Seconds 6

  $wrongPassword = Request-EmailChange -NewEmail $aEmail1 -Password 'Wrong-Password-1!' -BearerToken $aToken
  Assert-Exact 'a wrong current password is refused (400)' 400 $wrongPassword | Out-Null

  $unchanged = Request-EmailChange -NewEmail $aEmail0 -Password $script:TestPassword -BearerToken $aToken
  Assert-Exact 'the active address cannot be requested again (409)' 409 $unchanged | Out-Null

  $conflict = Request-EmailChange -NewEmail $bEmail -Password $script:TestPassword -BearerToken $aToken
  Assert-Exact 'an address held by another account is refused (409)' 409 $conflict | Out-Null

  $invalid = Request-EmailChange -NewEmail 'not-an-address' -Password $script:TestPassword -BearerToken $aToken
  Assert-Exact 'a syntactically invalid address is refused (400)' 400 $invalid | Out-Null

  # -------------------------------------------------------------------------------------------
  Log '--- email change: delivery, single use and stale JWT ---'
  $changeBaseline1 = New-OutboxBaseline -OutboxPath $outbox
  $change1 = Request-EmailChange -NewEmail $aEmail1 -Password $script:TestPassword -BearerToken $aToken
  Assert-Exact 'a valid address change is accepted (202)' 202 $change1 | Out-Null

  # The immediate retry is measured before any slow outbox polling or SQL work.
  $cooldown = Request-EmailChange -NewEmail $aEmail2 -Password $script:TestPassword -BearerToken $aToken
  Assert-Exact 'the per-account cooldown answers the immediate retry (429)' 429 $cooldown | Out-Null

  $changeToken1 = Get-VerificationTokenFromOutbox -Email $aEmail1 -OutboxPath $outbox -Baseline $changeBaseline1
  $ttl = [int](Sql ("SELECT TOP 1 DATEDIFF(SECOND, SYSUTCDATETIME(), EXPIRES_AT) FROM EMAIL_VERIFICATION_TOKENS WHERE USER_ID = {0} AND PURPOSE = 'EmailChange' AND CONSUMED_AT IS NULL ORDER BY ID DESC;" -f $aId))
  Assert-True 'the address-change token is persisted with the one-hour TTL' ($ttl -gt 3000 -and $ttl -le 3720)

  $meBefore = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $aToken
  Assert-Exact 'the active session still works before confirmation (200)' 200 $meBefore | Out-Null
  Assert-True 'the pending address is reported before confirmation' ((JsonField $meBefore.Body 'pendingEmail') -ieq $aEmail1)

  $oldAddressLogin = Invoke-Login -Email $aEmail0 -Password $script:TestPassword
  Assert-Exact 'the old address still signs in before confirmation (200)' 200 $oldAddressLogin | Out-Null

  # The registration endpoint and the address-change endpoint share a token service, but a
  # change token must never be redeemable as a registration token.
  $wrongPurpose = Complete-EmailVerification -Token $changeToken1
  Assert-Exact 'a change token is refused by /verify-email (400)' 400 $wrongPurpose | Out-Null

  $confirm1 = Confirm-EmailChange -Token $changeToken1
  Assert-Exact 'the new address is confirmed (204)' 204 $confirm1 | Out-Null

  $reuse = Confirm-EmailChange -Token $changeToken1
  Assert-Exact 'the address-change token cannot be reused (400)' 400 $reuse | Out-Null

  $meAfter = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $aToken
  Assert-Exact 'the JWT issued before the change is rejected (401)' 401 $meAfter | Out-Null

  $oldLogin = Invoke-Login -Email $aEmail0 -Password $script:TestPassword
  Assert-Exact 'the old address no longer signs in (401)' 401 $oldLogin | Out-Null

  $newLogin = Invoke-Login -Email $aEmail1 -Password $script:TestPassword
  Assert-Exact 'the new address signs in (200)' 200 $newLogin | Out-Null
  if ($newLogin.Status -ne 200) { throw 'the new address cannot sign in' }
  $aNewToken = [string](JsonField $newLogin.Body 'token')
  $meNew = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $aNewToken
  Assert-Exact 'the new session is accepted (200)' 200 $meNew | Out-Null
  Assert-True 'the active address is the confirmed address' ((JsonField $meNew.Body 'email') -ieq $aEmail1)
  Assert-True 'no pending address remains after confirmation' ([string]::IsNullOrWhiteSpace([string](JsonField $meNew.Body 'pendingEmail')))

  # -------------------------------------------------------------------------------------------
  Log '--- email change: supersession and the daily cap ---'
  Start-Sleep -Seconds 6
  $changeBaseline2 = New-OutboxBaseline -OutboxPath $outbox
  $change2 = Request-EmailChange -NewEmail $aEmail2 -Password $script:TestPassword -BearerToken $aNewToken
  Assert-Exact 'a later address change is accepted (202)' 202 $change2 | Out-Null
  $changeToken2 = Get-VerificationTokenFromOutbox -Email $aEmail2 -OutboxPath $outbox -Baseline $changeBaseline2

  Start-Sleep -Seconds 6
  $change3 = Request-EmailChange -NewEmail $aEmail3 -Password $script:TestPassword -BearerToken $aNewToken
  Assert-Exact 'a third address change retires the second one (202)' 202 $change3 | Out-Null

  $superseded = Confirm-EmailChange -Token $changeToken2
  Assert-Exact 'the superseded address-change token is refused (400)' 400 $superseded | Out-Null

  Start-Sleep -Seconds 6
  $capped = Request-EmailChange -NewEmail $aEmail4 -Password $script:TestPassword -BearerToken $aNewToken
  Assert-Exact 'the fourth request is stopped by the daily cap (429)' 429 $capped | Out-Null
  $tokenCount = [int](Sql ("SELECT COUNT(*) FROM EMAIL_VERIFICATION_TOKENS WHERE USER_ID = {0} AND CREATED_AT >= CAST(SYSUTCDATETIME() AS date);" -f $aId))
  Assert-True 'the daily cap counts every token of the account (4)' ($tokenCount -eq 4)

  Assert-True 'the audit trail records three address-change requests' ((UserEventCount -UserId $aId -EventType 'EmailChangeRequested') -eq 3)
  Assert-True 'the audit trail records the one completed address change' ((UserEventCount -UserId $aId -EventType 'EmailChanged') -eq 1)

  # -------------------------------------------------------------------------------------------
  Log '--- investor approval: eligibility, durable actor attribution and re-approval ---'
  $forbiddenApproval = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId/approval") -BearerToken $aNewToken
  Assert-Exact 'an investor cannot read the approval queue (403)' 403 $forbiddenApproval | Out-Null

  $anonymousApprove = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$aId/approve")
  Assert-Exact 'anonymous investor approval is refused' 401 $anonymousApprove | Out-Null
  $selfApprove = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$aId/approve") -BearerToken $aNewToken
  Assert-Exact 'investor self-approval is refused' 403 $selfApprove | Out-Null
  Assert-True 'unauthorized approval attempts append no event' ((UserEventCount -UserId $aId -EventType 'AccountApproved') -eq 0)
  $aApprovalBefore = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId/approval") -BearerToken $saToken
  Assert-Exact 'the SuperAdmin reads the investor approval status (200)' 200 $aApprovalBefore | Out-Null
  $aStatus = $aApprovalBefore.Body | ConvertFrom-Json
  Assert-True 'the verified investor with company details is eligible' ([bool]$aStatus.eligibleForApproval)
  Assert-True 'the approval history is empty before the decision' (@($aStatus.history).Count -eq 0)
  Assert-True 'the investor is not approved before the decision' (-not [bool]$aStatus.isApproved)

  $approveA = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$aId/approve") -BearerToken $saToken
  Assert-Exact 'the SuperAdmin approves the investor (204)' 204 $approveA | Out-Null

  $aApproval1 = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId/approval") -BearerToken $saToken
  Assert-Exact 'the approved status is readable (200)' 200 $aApproval1 | Out-Null
  $aStatus1 = $aApproval1.Body | ConvertFrom-Json
  Assert-True 'the investor is approved' ([bool]$aStatus1.isApproved)
  Assert-True 'an already-approved investor is no longer eligible' (-not [bool]$aStatus1.eligibleForApproval)
  Assert-True 'the first approval is recorded once' (@($aStatus1.history).Count -eq 1)
  Assert-True 'the first approval names the acting SuperAdmin' ([int]$aStatus1.approvedById -eq $saId -and $aStatus1.approvedByName -eq $saName -and $aStatus1.approvedByRole -eq 'SuperAdmin')

  $firstApprovalEvent = @($aStatus1.history)[0]
  Assert-True 'first approval records time and actor name' ($null -ne $aStatus1.approvedAt -and [DateTime]$aStatus1.approvedAt -eq [DateTime]$firstApprovalEvent.approvedAt -and $firstApprovalEvent.actorName -eq $saName)
  $approveAgain = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$aId/approve") -BearerToken $saToken
  Assert-Exact 'the SuperAdmin may approve again (204)' 204 $approveAgain | Out-Null
  $aApproval2 = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId/approval") -BearerToken $saToken
  $aStatus2 = $aApproval2.Body | ConvertFrom-Json
  Assert-True 're-approval appends a second history event' (@($aStatus2.history).Count -eq 2)
  Assert-True 'both history events name the actor' (@($aStatus2.history | Where-Object { [int]$_.actorId -eq $saId -and $_.actorRole -eq 'SuperAdmin' }).Count -eq 2)

  $retainedEvent = @($aStatus2.history | Where-Object { $_.eventId -eq $firstApprovalEvent.eventId })
  Assert-True 're-approval preserves the original event identity time and actor' ($retainedEvent.Count -eq 1 -and $retainedEvent[0].approvedAt -eq $firstApprovalEvent.approvedAt -and $retainedEvent[0].actorId -eq $firstApprovalEvent.actorId -and $retainedEvent[0].actorName -eq $firstApprovalEvent.actorName -and $retainedEvent[0].actorRole -eq $firstApprovalEvent.actorRole)
  Assert-True 're-approval events are distinct and latest timestamp agrees' (@($aStatus2.history | Select-Object -ExpandProperty eventId -Unique).Count -eq 2 -and $null -ne $aStatus2.approvedAt -and @($aStatus2.history | Where-Object { [DateTime]$_.approvedAt -eq [DateTime]$aStatus2.approvedAt }).Count -ge 1)
  $aUser = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$aId") -BearerToken $saToken
  Assert-Exact 'the user detail is readable (200)' 200 $aUser | Out-Null
  Assert-True 'the user detail shows the verified address and the approver' ((JsonField $aUser.Body 'emailVerified') -eq $true -and (JsonField $aUser.Body 'approvedByName') -eq $saName)

  $cApprovalBefore = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$cId/approval") -BearerToken $saToken
  Assert-Exact 'the unverified investor status is readable (200)' 200 $cApprovalBefore | Out-Null
  $cStatusBefore = $cApprovalBefore.Body | ConvertFrom-Json
  Assert-True 'an unverified investor is not eligible' (-not [bool]$cStatusBefore.eligibleForApproval -and $cStatusBefore.emailVerified -eq $false)
  $cApproveBefore = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$cId/approve") -BearerToken $saToken
  Assert-Exact 'approving an unverified investor is refused (409)' 409 $cApproveBefore | Out-Null
  Assert-True 'the refused approval leaves no history' ((UserEventCount -UserId $cId -EventType 'AccountApproved') -eq 0)

  $cConfirm = Complete-EmailVerification -Token $cVerificationToken
  Assert-Exact 'the unverified investor confirms the address (204)' 204 $cConfirm | Out-Null
  $cApprovalReady = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$cId/approval") -BearerToken $saToken
  $cStatusReady = $cApprovalReady.Body | ConvertFrom-Json
  Assert-True 'the investor becomes eligible after verification' ([bool]$cStatusReady.eligibleForApproval)

  $cApprove = Invoke-VerificationApi -Method 'POST' -Path ("/api/users/$cId/approve") -BearerToken $saToken
  Assert-Exact 'the verified investor is approved (204)' 204 $cApprove | Out-Null
  $cApprovalAfter = Invoke-VerificationApi -Method 'GET' -Path ("/api/users/$cId/approval") -BearerToken $saToken
  $cStatusAfter = $cApprovalAfter.Body | ConvertFrom-Json
  Assert-True 'the investor approval history records one actor' (@($cStatusAfter.history).Count -eq 1 -and [int]$cStatusAfter.approvedById -eq $saId)

  # -------------------------------------------------------------------------------------------
  Log '--- real land documents: upload, metadata, download and validation ---'
  $dLogin = Invoke-Login -Email $dEmail -Password $script:TestPassword
  Assert-Exact 'the landlord signs in (200)' 200 $dLogin | Out-Null
  if ($dLogin.Status -ne 200) { throw 'the document landlord cannot sign in' }
  $dToken = [string](JsonField $dLogin.Body 'token')

  $governorates = Invoke-VerificationApi -Method 'GET' -Path '/api/governorates' -BearerToken $dToken
  Assert-Exact 'the governorate lookup is readable (200)' 200 $governorates | Out-Null
  $governorateId = Get-AmmanGovernorateId -Body $governorates.Body

  $documentTypes = Invoke-VerificationApi -Method 'GET' -Path '/api/document-types' -BearerToken $dToken
  Assert-Exact 'the document-type lookup is readable (200)' 200 $documentTypes | Out-Null
  $documentTypeId = Get-FirstDocumentTypeId -Body $documentTypes.Body

  $landBody = @{
    governorateId = $governorateId
    landNumber = "F1-$stamp-$run"
    village = 'Test Village'
    basin = 'Test Basin'
    parcelNumber = "P-$run"
    areaDonum = 25
    description = 'F1 document upload test land'
  } | ConvertTo-Json -Compress
  Register-F1StorageLandAttempt -Receipt $script:StorageReceipt -OwnerId $dId
  $land = Invoke-VerificationApi -Method 'POST' -Path '/api/lands' -BearerToken $dToken -Json $landBody
  Assert-Exact 'the landlord registers a land (201)' 201 $land | Out-Null
  $landId = [int](JsonField $land.Body 'id')
  if ($landId -le 0) { throw 'the registered land has no id' }
  $script:StorageReceipt.LandId = $landId
  Write-F1StorageReceipt $script:StorageReceipt

  $pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n1 0 obj<</Type/Catalog>>endobj`ntrailer<</Root 1 0 R>>`n%%EOF`n")
  $sha = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::Create().ComputeHash($pdfBytes)).Replace('-', '').ToLowerInvariant()

  $validUpload = Invoke-MultipartApi -Path ("/api/lands/$landId/documents") -DocumentTypeId $documentTypeId `
    -FileName '../evil.pdf' -ContentType 'text/plain' -Bytes $pdfBytes -BearerToken $dToken
  Assert-Exact 'a valid PDF upload is created (201)' 201 $validUpload | Out-Null
  $validDocument = $validUpload.Body | ConvertFrom-Json
  Assert-True 'a real upload never returns the private storage key' ($null -eq $validDocument.filePath)
  Assert-True 'a real upload reports that a file is stored' ([bool]$validDocument.hasFile)
  Assert-True 'the original file name is reduced to its base name' ($validDocument.originalFileName -eq 'evil.pdf')
  Assert-True 'the server-detected content type is application/pdf' ($validDocument.contentType -eq 'application/pdf')
  Assert-True 'the stored size is the exact byte count' ([long]$validDocument.fileSizeBytes -eq $pdfBytes.Length)
  Assert-True 'the stored SHA-256 is recorded' ($validDocument.sha256 -eq $sha)
  Assert-True 'one private file exists after the upload' ((Get-StoredFileCount) -eq 1)

  $download = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$([int]$validDocument.id)/download") -BearerToken $dToken
  Assert-Exact 'the owner downloads the document (200)' 200 $download | Out-Null
  Assert-True 'the downloaded bytes are the uploaded bytes' ([Convert]::ToBase64String($download.Bytes) -eq [Convert]::ToBase64String($pdfBytes))
  Assert-True 'the download is served as PDF' ($download.ContentType -eq 'application/pdf')
  Assert-True 'the download is an attachment' ($download.Disposition -match 'attachment')

  $anonymousDownload = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$([int]$validDocument.id)/download")
  Assert-Exact 'an anonymous document download is refused (401)' 401 $anonymousDownload | Out-Null

  $nonOwnerDownload = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$([int]$validDocument.id)/download") -BearerToken $aNewToken
  Assert-OneOf 'a non-owner investor cannot download the document' @(403, 404) $nonOwnerDownload | Out-Null

  $adminDownload = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$([int]$validDocument.id)/download") -BearerToken $saToken
  Assert-Exact 'the SuperAdmin downloads the document (200)' 200 $adminDownload | Out-Null

  $badMagicBytes = [System.Text.Encoding]::ASCII.GetBytes('MZ-not-a-document')
  $badMagic = Invoke-MultipartApi -Path ("/api/lands/$landId/documents") -DocumentTypeId $documentTypeId `
    -FileName 'renamed.pdf' -ContentType 'application/pdf' -Bytes $badMagicBytes -BearerToken $dToken
  Assert-Exact 'a renamed executable is refused by the signature check (400)' 400 $badMagic | Out-Null
  Assert-True 'the refused signature leaves no file' ((Get-StoredFileCount) -eq 1)

  $oversizeBytes = New-Object byte[] 2048
  [Array]::Copy($pdfBytes, $oversizeBytes, $pdfBytes.Length)
  $oversize = Invoke-MultipartApi -Path ("/api/lands/$landId/documents") -DocumentTypeId $documentTypeId `
    -FileName 'oversize.pdf' -ContentType 'application/pdf' -Bytes $oversizeBytes -BearerToken $dToken
  Assert-Exact 'a file above the configured limit is refused (400)' 400 $oversize | Out-Null
  Assert-True 'the refused oversize upload leaves no file' ((Get-StoredFileCount) -eq 1)

  $unknownType = Invoke-MultipartApi -Path ("/api/lands/$landId/documents") -DocumentTypeId 999999 `
    -FileName 'unknown.pdf' -ContentType 'application/pdf' -Bytes $pdfBytes -BearerToken $dToken
  Assert-Exact 'an unknown document type is refused (400)' 400 $unknownType | Out-Null
  Assert-True 'the refused document type leaves no file' ((Get-StoredFileCount) -eq 1)

  $legacy = Invoke-VerificationApi -Method 'POST' -Path ("/api/lands/$landId/documents/metadata") -BearerToken $dToken `
    -Json (@{ documentTypeId = $documentTypeId; filePath = 'legacy/contract.pdf' } | ConvertTo-Json -Compress)
  Assert-Exact 'the Development-only legacy metadata upload still works (201)' 201 $legacy | Out-Null
  $legacyDocument = $legacy.Body | ConvertFrom-Json
  Assert-True 'the legacy row keeps its metadata path' ($legacyDocument.filePath -eq 'legacy/contract.pdf')
  Assert-True 'the legacy row is not a stored file' (-not [bool]$legacyDocument.hasFile)

  $legacyDownload = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$([int]$legacyDocument.id)/download") -BearerToken $dToken
  Assert-Exact 'a legacy metadata row cannot be downloaded (404)' 404 $legacyDownload | Out-Null

  $traversal = Invoke-VerificationApi -Method 'POST' -Path ("/api/lands/$landId/documents/metadata") -BearerToken $dToken `
    -Json (@{ documentTypeId = $documentTypeId; filePath = '../../outside.pdf' } | ConvertTo-Json -Compress)
  Assert-Exact 'a traversing legacy path is refused (400)' 400 $traversal | Out-Null
  Assert-True 'the refused legacy upload leaves the file count unchanged' ((Get-StoredFileCount) -eq 1)

  Log '--- genuine document versions and concurrent uploads (not an FK race) ---'
  $pdf2 = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n% second distinct version`n%%EOF`n")
  $sha2 = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($pdf2)).Replace('-', '').ToLowerInvariant()
  $second = Invoke-MultipartApi -Path ("/api/lands/$landId/documents") -DocumentTypeId $documentTypeId -FileName 'second.pdf' -ContentType 'application/pdf' -Bytes $pdf2 -BearerToken $dToken
  Assert-Exact 'a second genuine PDF upload is created' 201 $second | Out-Null
  if ($second.Status -ne 201) { throw 'the second genuine upload failed' }
  $v2 = $second.Body | ConvertFrom-Json
  Assert-True 'the second real upload has a distinct document id' ([int]$v2.id -ne [int]$validDocument.id)
  # The earlier legacy metadata row is also a version: it must not be ignored.
  Assert-True 'versions increase across real and legacy uploads' ([int]$validDocument.version -eq 1 -and [int]$legacyDocument.version -eq 2 -and [int]$v2.version -eq 3)
  Assert-True 'second real metadata matches the new bytes' ($v2.hasFile -and $null -eq $v2.filePath -and $v2.originalFileName -eq 'second.pdf' -and $v2.contentType -eq 'application/pdf' -and [long]$v2.fileSizeBytes -eq $pdf2.Length -and $v2.sha256 -eq $sha2)
  foreach ($versionCase in @(@{ Document=$validDocument; Bytes=$pdfBytes; Name='evil.pdf' }, @{ Document=$v2; Bytes=$pdf2; Name='second.pdf' })) {
    $id = [int]$versionCase.Document.id
    $blob = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$id/download") -BearerToken $dToken
    Assert-Exact "version $id remains downloadable" 200 $blob | Out-Null
    Assert-True "version $id retains its own bytes and attachment metadata" ([Convert]::ToBase64String($blob.Bytes) -eq [Convert]::ToBase64String($versionCase.Bytes) -and $blob.ContentType -eq 'application/pdf' -and $blob.Disposition -match 'attachment' -and $blob.Disposition -match [regex]::Escape($versionCase.Name))
    $denied = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$id/download") -BearerToken $aNewToken
    Assert-OneOf "non-owner cannot retrieve version $id" @(403,404) $denied | Out-Null
  }
  $anonymousSecond = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$($v2.id)/download")
  Assert-Exact 'anonymous retrieval of the second version is refused' 401 $anonymousSecond | Out-Null
  $beforeConcurrent = Get-StoredFileCount
  $race = Invoke-ConcurrentApiRequests -Requests @(
    @{ Method='POST'; Path="/api/lands/$landId/documents"; BearerToken=$dToken; FileBytes=$pdfBytes; FileName='race-one.pdf'; DocumentTypeId=$documentTypeId },
    @{ Method='POST'; Path="/api/lands/$landId/documents"; BearerToken=$dToken; FileBytes=$pdf2; FileName='race-two.pdf'; DocumentTypeId=$documentTypeId },
    @{ Method='POST'; Path="/api/lands/$landId/documents"; BearerToken=$aNewToken; FileBytes=$pdfBytes; FileName='foreign.pdf'; DocumentTypeId=$documentTypeId }
  )
  Assert-Exact 'concurrent owner upload one is created' 201 $race[0] | Out-Null
  Assert-Exact 'concurrent owner upload two is created' 201 $race[1] | Out-Null
  Assert-OneOf 'concurrent non-owner upload is refused' @(403,404) $race[2] | Out-Null
  if ($race[0].Status -ne 201 -or $race[1].Status -ne 201) { throw 'concurrent owner uploads failed' }
  $r1 = $race[0].Body | ConvertFrom-Json
  $r2 = $race[1].Body | ConvertFrom-Json
  Assert-True 'concurrent allocation gives distinct ids and consecutive versions' ($r1.id -ne $r2.id -and (@([int]$r1.version,[int]$r2.version | Sort-Object) -join ',') -eq '4,5')
  Assert-True 'only the two authorized concurrent files are stored' ((Get-StoredFileCount) -eq $beforeConcurrent + 2)
  $history = Invoke-VerificationApi -Method 'GET' -Path ("/api/lands/$landId/documents") -BearerToken $dToken
  Assert-Exact 'document version history is readable' 200 $history | Out-Null
  $versions = @(ConvertFrom-JsonArray $history.Body | Where-Object { [int]$_.documentTypeId -eq $documentTypeId })
  Assert-True 'history preserves all versions in API order' ($versions.Count -eq 5 -and (@($versions | ForEach-Object { $_.version }) -join ',') -eq '1,2,3,4,5')
  Assert-True 'history retains each distinct document id' (@($versions | Select-Object -ExpandProperty id -Unique).Count -eq 5 -and @($versions | Where-Object { $_.id -eq $validDocument.id }).Count -eq 1 -and @($versions | Where-Object { $_.id -eq $v2.id }).Count -eq 1)
  foreach ($pair in @(@{ Document=$r1; Bytes=$pdfBytes; Sha=$sha; Name='race-one.pdf' }, @{ Document=$r2; Bytes=$pdf2; Sha=$sha2; Name='race-two.pdf' })) {
    $blob = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$($pair.Document.id)/download") -BearerToken $dToken
    Assert-Exact 'concurrent committed document is downloadable' 200 $blob | Out-Null
    Assert-True 'concurrent document bytes and metadata agree' ([Convert]::ToBase64String($blob.Bytes) -eq [Convert]::ToBase64String($pair.Bytes) -and $pair.Document.sha256 -eq $pair.Sha -and [long]$pair.Document.fileSizeBytes -eq $pair.Bytes.Length -and $pair.Document.originalFileName -eq $pair.Name -and $pair.Document.hasFile -and $null -eq $pair.Document.filePath -and $blob.ContentType -eq 'application/pdf')
  }
  $originalAfterRace = Invoke-BlobApi -Path ("/api/lands/$landId/documents/$($validDocument.id)/download") -BearerToken $dToken
  Assert-Exact 'original version remains downloadable after concurrent attempts' 200 $originalAfterRace | Out-Null
  Assert-True 'concurrent attempts cannot overwrite the original bytes' ([Convert]::ToBase64String($originalAfterRace.Bytes) -eq [Convert]::ToBase64String($pdfBytes))
  Assert-True 'stored-file and history metadata counts agree' ((Get-StoredFileCount) -eq @($versions | Where-Object { $_.hasFile }).Count)
  Skip 'genuine document FK race' 'NOT_TESTED in this API suite: the prepared isolated --sql-acceptance mode exercises the genuine FK barrier and restrictive fixture deletion in Phase 2'

  if ($IncludeGovernance) {
    Log '--- newly scoped criteria, pinned-land and approval history acceptance ---'
    Invoke-F1GovernanceAcceptance -SuperToken $saToken -SuperId $saId -InvestorToken $aNewToken -InvestorId $aId -LandlordToken $dToken
  }

  # -------------------------------------------------------------------------------------------
  Log '--- delivery failure: the Disabled mode must not change the active address ---'
  if ($script:fail -ne 0) {
    throw "the FileOutbox phase reported $($script:fail) failure(s); the Disabled phase was not started"
  }

  $script:CleanupAttempted = $true
  Complete-TestApiCleanup -Identity $script:Harness
  $script:Harness = $null
  $script:StorageApiStopped = $true
  $script:StorageCheckAttempted = $true
  $retention = Confirm-F1StorageRetention -Receipt $script:StorageReceipt
  Log ("FileOutbox retained storage verified: real={0} legacy={1}; receipt={2}" -f $retention.RealFiles, $retention.LegacyRecords, $retention.ReceiptPath)
  Log '--- FileOutbox phase cleanup and retained storage verified; starting the Disabled phase ---'

  $env:Email__ResendCooldownSeconds = '0'
  $env:Email__ResendDailyCap = '100'
  $script:CleanupAttempted = $false
  $script:StorageCheckAttempted = $false
  $script:StorageApiStopped = $false
  $script:Harness = Start-TestApiInstance -DeliveryMode 'Disabled'
  $null = Initialize-TestApi

  $bLogin = Invoke-Login -Email $bEmail -Password $script:TestPassword
  Assert-Exact 'the conflict investor signs in on the Disabled instance (200)' 200 $bLogin | Out-Null
  if ($bLogin.Status -ne 200) { throw 'the conflict investor cannot sign in on the Disabled instance' }
  $bToken = [string](JsonField $bLogin.Body 'token')

  $bFailed = Request-EmailChange -NewEmail $bNewEmail -Password $script:TestPassword -BearerToken $bToken
  Assert-Exact 'undeliverable address change is a controlled 503' 503 $bFailed | Out-Null
  $bMe = Invoke-VerificationApi -Method 'GET' -Path '/api/auth/me' -BearerToken $bToken
  Assert-Exact 'the session survives a failed delivery (200)' 200 $bMe | Out-Null
  Assert-True 'the active address is unchanged after a failed delivery' ((JsonField $bMe.Body 'email') -ieq $bEmail)
  Assert-True 'the failed request records the pending address without activating it' ((JsonField $bMe.Body 'pendingEmail') -ieq $bNewEmail)

  $bNewLogin = Invoke-Login -Email $bNewEmail -Password $script:TestPassword
  Assert-Exact 'the undelivered address cannot sign in (401)' 401 $bNewLogin | Out-Null
  Assert-True 'the failed request is audited once' ((UserEventCount -UserId $bId -EventType 'VerificationEmailDeliveryFailed') -eq 1)
  Assert-True 'the failed request never records a completed change' ((UserEventCount -UserId $bId -EventType 'EmailChanged') -eq 0)

  Skip 'an expired address-change token is refused at redemption' 'NOT_TESTED in this API suite: prepared Phase 2 --sql-acceptance uses an isolated clock for the exact expiry boundary without token-row rewrites'
  Skip 'an in-flight address-change request is cancelled without side effects' 'NOT_TESTED in this API suite: prepared Phase 2 --sql-acceptance uses an isolated cancellable mailer and checks token/audit restoration'
}
catch {
  if ($_.FullyQualifiedErrorId -notlike 'F1AssertionFailed*') { $script:fail++ }
  Log ("FATAL: {0}" -f $_.Exception.Message)
}
finally {
  if ($script:Harness -and -not $script:CleanupAttempted) {
    try {
      Complete-TestApiCleanup -Identity $script:Harness
      $script:Harness = $null
      $script:StorageApiStopped = $true
    } catch {
      $script:fail++
      Log ("WARNING: harness cleanup reported an error: {0}" -f $_.Exception.Message)
    }
  }

  if ($script:StorageReceipt -and -not $script:StorageCheckAttempted) {
    if ($script:StorageApiStopped) {
      $script:StorageCheckAttempted = $true
      try {
        $retention = Confirm-F1StorageRetention -Receipt $script:StorageReceipt
        Log ("Storage retention verified: real={0} legacy={1}; receipt={2}" -f $retention.RealFiles, $retention.LegacyRecords, $retention.ReceiptPath)
      } catch {
        $script:fail++
        Log 'WARNING: retained storage integrity or temporary-upload cleanup failed; all uncertain bytes were preserved.'
      }
    } else {
      if ($script:fail -eq 0) { $script:fail++ }
      Log 'WARNING: API containment is unresolved; storage verification was blocked and all bytes were preserved.'
    }
  }
}

Log '==================== summary ===================='
Log ("PASS={0} FAIL={1} SKIP={2}" -f $script:pass, $script:fail, $script:skip)
if ($script:fail -gt 0) { exit 1 }
exit 0
