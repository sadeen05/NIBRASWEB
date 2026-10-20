#requires -Version 5.1
# F1 - email verification acceptance suite (never prints credentials, tokens or addresses).
#
# The suite drives the REAL API: it registers an account, reads the verification link out of
# the development outbox and redeems it through POST /api/auth/verify-email. There is NO
# bypass: nothing here writes to the database to verify an account, and no verification state
# is set directly. The only database access is READ-ONLY (the audit trail and the stored token
# rows are inspected, never changed).
#
# Requirements:
#   * the harness-owned API instance (a dedicated port) against the disposable database;
#     database (see docs/tests/DISPOSABLE_DB_PREFLIGHT.md);
#   * the instance in the FileOutbox delivery mode, with NIBRAS_EMAIL_OUTBOX pointing at the
#     same per-run outbox directory the instance writes to (outside any Git working tree);
#   * ConnectionStrings__DefaultConnection pointing at the disposable database - the suite
#     resolves its target from it and refuses to run against anything else;
#   * NIBRAS_TEST_PASSWORD (optional) - otherwise a runtime-generated password is used;
#   * for the privileged cases (the SuperAdmin flows), the dedicated test credentials in the
#     environment variables NIBRAS_TEST_SUPERADMIN_EMAIL and NIBRAS_TEST_SUPERADMIN_PASSWORD.
#     The suite reports them as SKIP when they are not set; it never reads the user secrets.
#
# The parts that need a different instance configuration are reported as SKIP with the
# requirement, never silently passed.
#
# Usage:  powershell -File f1_email_verification.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http | Out-Null
. (Join-Path $PSScriptRoot 'test_target.ps1')
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')

# The target is resolved from the effective connection string and is fail-closed: only the
# disposable database on the approved instance is accepted, and a mismatch stops the run.
$testTarget = Get-TestTarget
$db = $testTarget.Database
$sqlServer = $testTarget.Server

# The suite owns its instance: the harness starts it on the dedicated port, with the explicit
# disposable connection string, and proves that it owns the port. The gate below re-proves it
# before anything is written.
$harness = Start-TestApiInstance
$base = Initialize-TestApi

$pw        = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) }
$stamp     = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$outbox    = $env:NIBRAS_EMAIL_OUTBOX

$script:pass = 0
$script:fail = 0
$script:skip = 0
$script:lines = New-Object System.Collections.Generic.List[string]

function Log([string]$s) { $script:lines.Add($s); Write-Host $s }

function Assert {
  param([string]$Name, [int]$Expected, [object]$Resp)
  $ok = ($Resp.Status -eq $Expected)
  if ($ok) { $script:pass++ } else { $script:fail++ }
  $tag = if ($ok) { 'PASS' } else { 'FAIL' }
  Log ("{0,-62} exp={1,-4} got={2,-4} {3}" -f $Name, $Expected, $Resp.Status, $tag)
  return $ok
}

function Skip([string]$Name, [string]$Reason) {
  $script:skip++
  Log ("{0,-62} SKIP ({1})" -f $Name, $Reason)
}

function Sql([string]$q) {
  $out = & sqlcmd -S $sqlServer -E -d $db -b -W -h -1 -Q ("SET NOCOUNT ON; " + $q) 2>&1
  if ($LASTEXITCODE -ne 0) { throw "read-only SQL failed: $out" }
  return (@($out | Where-Object { $_ }) -join "`n").Trim()
}

function JsonField([string]$body, [string]$name) {
  try { return ($body | ConvertFrom-Json).$name } catch { return $null }
}

function OutboxCount([string]$address) {
  if (-not $outbox -or -not (Test-Path -LiteralPath $outbox)) { return -1 }
  $count = 0
  foreach ($file in Get-ChildItem -LiteralPath $outbox -Filter 'mail-*.json' -File -ErrorAction SilentlyContinue) {
    try { $m = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json } catch { continue }
    if ($m -and $m.to -and ($m.to -ieq $address)) { $count++ }
  }
  return $count
}

function New-Phone { '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)) }

function Assert-NotRateLimited([object]$Resp, [string]$What) {
  if ($Resp.Status -eq 429) {
    throw ("the verification rate limit answered while {0}. The run needs " +
      "RateLimit__VerificationPerMinute (and RateLimit__VerificationResendPerMinute) raised - " +
      "the production defaults are 5 per minute and would mask the result.")
  }
}

function Invoke-ConcurrentVerifications {
  <#
    .SYNOPSIS
      Redeems the same token twice at the same time, in this very process.
    .DESCRIPTION
      Two independent request objects are sent without waiting for the first one, so the two
      redemptions really overlap. Nothing is serialised: no job, no temporary file, and the
      plaintext token never leaves the memory of this process.
  #>
  param([Parameter(Mandatory)][string]$Token, [int]$Count = 2)

  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds(30)
  try {
    $payload = @{ token = $Token } | ConvertTo-Json -Compress
    $tasks = @()
    foreach ($i in 1..$Count) {
      $req = New-Object System.Net.Http.HttpRequestMessage
      $req.Method = [System.Net.Http.HttpMethod]::Post
      $req.RequestUri = [Uri](Get-VerificationBaseUrl + '/api/auth/verify-email')
      $req.Content = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, 'application/json')
      $tasks += $client.SendAsync($req)
    }
    $statuses = @()
    foreach ($t in $tasks) { $statuses += [int]$t.Result.StatusCode }
    return $statuses
  } finally { $client.Dispose() }
}

Log '==================== F1 email verification acceptance ===================='
Log ("  target: {0} / {1}" -f $sqlServer, $db)

if (-not $outbox) {
  Log 'FATAL: NIBRAS_EMAIL_OUTBOX is not set. The suite needs the FileOutbox delivery mode.'
  Log '       (the instance must run with Email__DeliveryMode=FileOutbox and a per-run outbox path outside a Git tree)'
  exit 2
}

# ---------------------------------------------------------------------------------------------
Log '--- A. registration -> verification -> sign-in ---'
$laEmail = "f1-$stamp-la@example.test"
$body = @{ fullName = 'F1 Landlord'; email = $laEmail; phone = (New-Phone); password = $pw; roleId = 1 } | ConvertTo-Json -Compress

$baseline = New-OutboxBaseline
$reg = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' -Json $body
Assert 'register a Landlord (201)' 201 $reg | Out-Null
$status = JsonField $reg.Body 'verification'
if ($status -eq 'handed_over') { $script:pass++; Log ("{0,-62} PASS" -f 'the registration answer reports a handed-over mail') }
else { $script:fail++; Log ("{0,-62} FAIL (got '{1}')" -f 'the registration answer reports a handed-over mail', $status) }

$loginUnverified = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{ email = $laEmail; password = $pw } | ConvertTo-Json -Compress)
Assert 'the sign-in of an unverified account is refused (403)' 403 $loginUnverified | Out-Null
$code = JsonField $loginUnverified.Body 'errorCode'
if ($code -eq 'EMAIL_NOT_VERIFIED') { $script:pass++; Log ("{0,-62} PASS" -f 'the refusal carries the code EMAIL_NOT_VERIFIED') }
else { $script:fail++; Log ("{0,-62} FAIL (got '{1}')" -f 'the refusal carries the code EMAIL_NOT_VERIFIED', $code) }

$wrongPassword = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{ email = $laEmail; password = 'wrong-password-value' } | ConvertTo-Json -Compress)
Assert 'the sign-in with a wrong password stays generic (401)' 401 $wrongPassword | Out-Null

$token = Get-VerificationTokenFromOutbox -Email $laEmail -Baseline $baseline
$confirm = Complete-EmailVerification -Token $token
Assert-NotRateLimited $confirm 'the address was confirmed'
Assert 'the address is confirmed (204)' 204 $confirm | Out-Null

$reuse = Complete-EmailVerification -Token $token
Assert-NotRateLimited $reuse 'a token was reused'
Assert 'the same token cannot be used twice (400)' 400 $reuse | Out-Null

$loginVerified = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{ email = $laEmail; password = $pw } | ConvertTo-Json -Compress)
Assert 'the sign-in works after the confirmation (200)' 200 $loginVerified | Out-Null

# ---------------------------------------------------------------------------------------------
Log '--- B. the token rules ---'
$invEmail = "f1-$stamp-inv@example.test"
$body = @{ fullName = 'F1 Investor'; email = $invEmail; phone = (New-Phone); password = $pw; roleId = 2; companyName = 'F1 Co'; companyRegistrationNumber = "F1-REG-$stamp" } | ConvertTo-Json -Compress
$baseline = New-OutboxBaseline
$reg = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' -Json $body
Assert 'register an Investor (201)' 201 $reg | Out-Null
$token = Get-VerificationTokenFromOutbox -Email $invEmail -Baseline $baseline

$statuses = Invoke-ConcurrentVerifications -Token $token
$ok204 = @($statuses | Where-Object { $_ -eq 204 }).Count
$ok400 = @($statuses | Where-Object { $_ -eq 400 }).Count
if ($ok204 -eq 1 -and $ok400 -eq 1) { $script:pass++; Log ("{0,-62} PASS" -f 'two concurrent redemptions: exactly one 204 and one 400') }
else { $script:fail++; Log ("{0,-62} FAIL (statuses: {1})" -f 'two concurrent redemptions: exactly one 204 and one 400', ($statuses -join ',')) }

$wrongPurpose = Complete-AdminActivation -Token $token -Password $pw
Assert-NotRateLimited $wrongPurpose 'a registration token was redeemed as an activation'
Assert 'a registration token cannot be redeemed as an activation (400)' 400 $wrongPurpose | Out-Null

# ---------------------------------------------------------------------------------------------
Log '--- C. the resend: the generic answer, the cooldown ---'
$before = OutboxCount $invEmail
$resend = Request-VerificationResend -Email $invEmail
Assert 'the resend of a known address answers 202' 202 $resend | Out-Null
$resendUnknown = Request-VerificationResend -Email "f1-$stamp-unknown@example.test"
Assert 'the resend of an unknown address answers 202 as well' 202 $resendUnknown | Out-Null
if ($resend.Body -eq $resendUnknown.Body) { $script:pass++; Log ("{0,-62} PASS" -f 'the resend answer is the same for a known and an unknown address') }
else { $script:fail++; Log ("{0,-62} FAIL" -f 'the resend answer is the same for a known and an unknown address') }

Start-Sleep -Seconds 2
$after = OutboxCount $invEmail
if ($before -ge 0 -and $after -eq $before) { $script:pass++; Log ("{0,-62} PASS" -f 'the resend cooldown sends no second mail (60 s)') }
else { $script:fail++; Log ("{0,-62} FAIL (before {1}, after {2})" -f 'the resend cooldown sends no second mail (60 s)', $before, $after) }
Skip 'the resend daily cap (10 per account and day)' 'needs Email__ResendCooldownSeconds=0 on a dedicated instance'

# ---------------------------------------------------------------------------------------------
Log '--- D. the registration failures ---'
$dupe = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' -Json $body
Assert 'a duplicate registration is a conflict (409)' 409 $dupe | Out-Null
Skip 'the delivery failure (verification: "failed")' 'needs a dedicated instance with Email__DeliveryMode=Disabled'
Skip 'the token expiry (an expired token is refused)' 'needs a dedicated instance with Email__RegistrationTokenTtlHours=1 and a wait'
Skip 'a cancelled registration leaves no live token' 'needs a read-only token snapshot taken while the request is in flight'

# ---------------------------------------------------------------------------------------------
Log '--- E. the administrator activation (privileged: the disposable test credentials) ---'
$saEmail = $env:NIBRAS_TEST_SUPERADMIN_EMAIL
$saPass = $env:NIBRAS_TEST_SUPERADMIN_PASSWORD
if ($saEmail -and $saPass) {
  $saLogin = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{ email = $saEmail; password = $saPass } | ConvertTo-Json -Compress)
  Assert 'the seeded SuperAdmin signs in (a legacy NULL account keeps its access)' 200 $saLogin | Out-Null

  $admEmail = "f1-$stamp-adm@example.test"
  $baseline = New-OutboxBaseline
  $create = Invoke-VerificationApi -Method 'POST' -Path '/api/users' -Json (@{ fullName = 'F1 Disposable Admin'; email = $admEmail; phone = (New-Phone); password = $pw; roleId = 3 } | ConvertTo-Json -Compress)
  Assert 'a SuperAdmin creates an Admin (201)' 201 $create | Out-Null

  $admToken = Get-VerificationTokenFromOutbox -Email $admEmail -Baseline $baseline -SubjectLike '*activation*'
  $activation = Complete-AdminActivation -Token $admToken -Password $pw
  Assert 'the Admin is activated with its own password (204)' 204 $activation | Out-Null

  $admLogin = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/login' -Json (@{ email = $admEmail; password = $pw } | ConvertTo-Json -Compress)
  Assert 'the activated Admin signs in with that password (200)' 200 $admLogin | Out-Null

  $reuse = Complete-AdminActivation -Token $admToken -Password $pw
  Assert 'the activation token cannot be used twice (400)' 400 $reuse | Out-Null
} else {
  Skip 'the administrator activation flow' 'the disposable test credentials (NIBRAS_TEST_SUPERADMIN_EMAIL / NIBRAS_TEST_SUPERADMIN_PASSWORD) are not set'
}

# ---------------------------------------------------------------------------------------------
Log '--- F. the audit trail (read-only) ---'
$audit = Sql "SELECT COUNT(*) FROM USER_SECURITY_EVENTS WHERE EVENT_TYPE = 'RegistrationCreated';"
if ([int]$audit -ge 1) { $script:pass++; Log ("{0,-62} PASS" -f 'the registration is recorded in the audit trail') }
else { $script:fail++; Log ("{0,-62} FAIL" -f 'the registration is recorded in the audit trail') }

$failed = Sql "SELECT COUNT(*) FROM USER_SECURITY_EVENTS WHERE EVENT_TYPE = 'LoginBlockedUnverified';"
if ([int]$failed -ge 1) { $script:pass++; Log ("{0,-62} PASS" -f 'the refused sign-in is recorded in the audit trail') }
else { $script:fail++; Log ("{0,-62} FAIL" -f 'the refused sign-in is recorded in the audit trail') }

$nulls = Sql "SELECT COUNT(*) FROM USERS WHERE EMAIL_VERIFIED IS NULL;"
Log ("  (the legacy accounts with a NULL verification flag: {0})" -f $nulls)

# ---------------------------------------------------------------------------------------------
Log '==================== summary ===================='
Log ("PASS={0} FAIL={1} SKIP={2}" -f $script:pass, $script:fail, $script:skip)
if ($script:fail -gt 0) { exit 1 }
exit 0
