#requires -Version 5.1
# Step 3 non-privileged test suite (temporary artifact; never prints tokens/secrets)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The email verification helpers: the token is read from the development outbox and
# redeemed through the real endpoints - no bypass, no direct state update.
Add-Type -AssemblyName System.Net.Http | Out-Null

# The suite owns its instance: the harness starts it on the dedicated port, with the
# explicit disposable connection string, and proves that it owns the port.
$harness = Start-TestApiInstance
# The suite owns its instance until the very end: the finally block stops ONLY the process
# the harness started (its PID must still own the recorded port), even when an assertion
# throws or a required SQL operation fails. A cleanup error is reported, never thrown over
# the original test result.
try {
$base = Initialize-TestApi
$stamp   = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$sqlcmd  = 'sqlcmd'   # resolved from PATH
# The target is resolved from the effective connection string and is fail-closed:
# only the disposable database is accepted, and a mismatch stops the run.
. (Join-Path $PSScriptRoot 'test_target.ps1')
$server  = (Get-TestTarget).Server
$db      = (Get-TestTarget).Database
# The running instance is proved to write into the disposable database before anything is written.
Assert-TestInstanceIdentity | Out-Null

$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(60)

$script:pass = 0
$script:fail = 0
$script:sqlFail = 0
$script:lines = New-Object System.Collections.Generic.List[string]

function Log([string]$s) { $script:lines.Add($s); Write-Host $s }

function Call {
  param([string]$Method,[string]$Path,[string]$Token,[string]$Json)
  $req = New-Object System.Net.Http.HttpRequestMessage
  $req.Method = New-Object System.Net.Http.HttpMethod($Method)
  $req.RequestUri = [Uri]($base + $Path)
  if ($Token) { $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer',$Token) }
  if ($Json) { $req.Content = New-Object System.Net.Http.StringContent($Json, [System.Text.Encoding]::UTF8, 'application/json') }
  try {
    $resp = $client.SendAsync($req).Result
    $status = [int]$resp.StatusCode
    $body = $resp.Content.ReadAsStringAsync().Result
    $h = @{}
    foreach ($kv in $resp.Headers) { $h[$kv.Key] = ($kv.Value -join ', ') }
    return [pscustomobject]@{ Status=$status; Header=$h; Body=$body }
  } finally { $req.Dispose() }
}

function Assert {
  param([string]$Name,[int]$Expected,[object]$Resp)
  $ok = ($Resp.Status -eq $Expected)
  if ($ok) { $script:pass++ } else { $script:fail++ }
  $tag = if ($ok) { 'PASS' } else { 'FAIL' }
  Log ("{0,-58} exp={1,-4} got={2,-4} {3}" -f $Name,$Expected,$Resp.Status,$tag)
  return $ok
}

function B64Url([string]$s) {
  $b = [System.Text.Encoding]::UTF8.GetBytes($s)
  return ([Convert]::ToBase64String($b)).TrimEnd('=').Replace('+','-').Replace('/','_')
}

function JsonRole([string]$body) {
  try { return ($body | ConvertFrom-Json).role } catch { return $null }
}
function JsonApproved([string]$body) {
  try { return ($body | ConvertFrom-Json).isApproved } catch { return $null }
}

function RegBody {
  param([string]$FullName,[string]$Email,[string]$Phone,[int]$RoleId,[string]$Password,[string]$CompanyName,[string]$CompanyReg,[string]$Extra)
  $o = [ordered]@{ fullName=$FullName; email=$Email; phone=$Phone; password=$Password; roleId=$RoleId }
  if ($CompanyName) { $o.companyName = $CompanyName }
  if ($CompanyReg)  { $o.companyRegistrationNumber = $CompanyReg }
  if ($Extra) { return ($o | ConvertTo-Json -Compress).TrimEnd('}') + ',' + $Extra + '}' }
  return ($o | ConvertTo-Json -Compress)
}

Log "==================== PHASE A (auth-policy checks; rate-limit ceiling 1000/min) ===================="

# Anonymous / non-auth checks first (no rate-limit permit consumed)
$r = Call GET '/api/lands' $null $null
Assert 'GET /api/lands (no token) -> 401' 401 $r | Out-Null
Log ("  headers: XCTO={0}; XFO={1}; RP={2}; CSP={3}" -f $r.Header['X-Content-Type-Options'], $r.Header['X-Frame-Options'], $r.Header['Referrer-Policy'], $r.Header['Content-Security-Policy'])

$r = Call GET '/api/auth/me' $null $null
Assert 'GET /api/auth/me (no token) -> 401' 401 $r | Out-Null

# --- 10 auth-policy requests ---
Assert 'register roleId=3 (400)' 400 (Call POST '/api/auth/register' $null (RegBody 'Rej Admin' "step3-pub-admin-$stamp@example.test" "079$stamp" 3 $pw $null $null $null)) | Out-Null
Assert 'register roleId=4 (400)' 400 (Call POST '/api/auth/register' $null (RegBody 'Rej SAdmin' "step3-pub-sadmin-$stamp@example.test" "078$stamp" 4 $pw $null $null $null)) | Out-Null
Assert 'register roleId=2 no company (400)' 400 (Call POST '/api/auth/register' $null (RegBody 'NoCo Investor' "step3-noco-$stamp@example.test" "077$stamp" 2 $pw $null $null $null)) | Out-Null
$pw200 = 'A' * 200
Assert 'register password 200 chars (400)' 400 (Call POST '/api/auth/register' $null (RegBody 'Pw200' "step3-pw200-$stamp@example.test" "076$stamp" 1 $pw200 $null $null $null)) | Out-Null
Assert 'register control char fullName (400)' 400 (Call POST '/api/auth/register' $null ("{""fullName"":""Bad\u0001Name"",""email"":""step3-ctl-$stamp@example.test"",""phone"":""075$stamp"",""password"":""$pw"",""roleId"":1}")) | Out-Null
Assert 'register invalid email (400)' 400 (Call POST '/api/auth/register' $null (RegBody 'Bad Email' 'not-an-email' "074$stamp" 1 $pw $null $null $null)) | Out-Null
Assert 'login unknown email (401)' 401 (Call POST '/api/auth/login' $null ("{""email"":""nobody-$stamp@example.test"",""password"":""$pw""}")) | Out-Null

$landlordEmail = "step3-landlord-$stamp@example.test"
$landlordPhone = "073$stamp"
$baseline = New-OutboxBaseline
$reg = Call POST '/api/auth/register' $null (RegBody 'Step3 Landlord' $landlordEmail $landlordPhone 1 $pw $null $null $null)

# The account can only sign in after its address was confirmed.
$v = Confirm-VerifiedAccount -Email $landlordEmail -Baseline $baseline
Assert 'confirm the registered Landlord (204)' 204 $v | Out-Null
Assert 'register Landlord fixture (201)' 201 $reg | Out-Null
$landlordId = ($reg.Body | ConvertFrom-Json).id
Assert 'duplicate register same email/phone (409)' 409 (Call POST '/api/auth/register' $null (RegBody 'Dup Landlord' $landlordEmail $landlordPhone 1 $pw $null $null $null)) | Out-Null
$login = Call POST '/api/auth/login' $null ("{""email"":""$landlordEmail"",""password"":""$pw""}")
Assert 'login Landlord correct password (200)' 200 $login | Out-Null
$landlordToken = ($login.Body | ConvertFrom-Json).token

Log "==================== PHASE B (token checks) ===================="

Assert 'login Landlord wrong password (401)' 401 (Call POST '/api/auth/login' $null ("{""email"":""$landlordEmail"",""password"":""Wrong-Passw0rd-123""}")) | Out-Null

$invEmail = "step3-inv-extra-$stamp@example.test"
$invPhone = "072$stamp"
$invExtra = '"isApproved":true,"isDeleted":true,"approvedById":3,"approvedAt":"2030-01-01T00:00:00Z","passwordHash":"not-a-hash"'
$baseline = New-OutboxBaseline
$invReg = Call POST '/api/auth/register' $null (RegBody 'Step3 Extra Investor' $invEmail $invPhone 2 $pw 'Step3 Co' 'STEP3-CO-01' $invExtra)

# The account can only sign in after its address was confirmed.
$v = Confirm-VerifiedAccount -Email $invEmail -Baseline $baseline
Assert 'confirm the registered Investor (204)' 204 $v | Out-Null
Assert 'register Investor + extra protected fields (201)' 201 $invReg | Out-Null
$invLogin = Call POST '/api/auth/login' $null ("{""email"":""$invEmail"",""password"":""$pw""}")
Assert 'login Investor (proves not soft-deleted) (200)' 200 $invLogin | Out-Null
$invToken = ($invLogin.Body | ConvertFrom-Json).token
$invMe = Call GET '/api/auth/me' $invToken $null
Assert 'Investor /me after extra fields (200)' 200 $invMe | Out-Null
$invApproved = JsonApproved $invMe.Body
$invRole = JsonRole $invMe.Body
$noElev = ($invApproved -eq $false) -and ($invRole -eq 'Investor')
if ($noElev) { $script:pass++ } else { $script:fail++ }
$noElevTag = if ($noElev) { 'PASS' } else { 'FAIL' }
Log ("  no-elevation check: role={0} isApproved={1} -> {2}" -f $invRole,$invApproved,$noElevTag)

$rcEmail = "step3-rolechange-$stamp@example.test"
$rcPhone = "071$stamp"
$baseline = New-OutboxBaseline
$rcReg = Call POST '/api/auth/register' $null (RegBody 'Step3 RoleChange' $rcEmail $rcPhone 1 $pw $null $null $null)

# The account can only sign in after its address was confirmed.
$v = Confirm-VerifiedAccount -Email $rcEmail -Baseline $baseline
Assert 'confirm the registered RoleChange Landlord (204)' 204 $v | Out-Null
Assert 'register RoleChange landlord fixture (201)' 201 $rcReg | Out-Null
$rcLogin = Call POST '/api/auth/login' $null ("{""email"":""$rcEmail"",""password"":""$pw""}")
Assert 'login RoleChange landlord (200)' 200 $rcLogin | Out-Null
$rcToken = ($rcLogin.Body | ConvertFrom-Json).token
$rcId    = ($rcLogin.Body | ConvertFrom-Json).user.id

# --- token checks (non-auth endpoints, no permit) ---
$me = Call GET '/api/auth/me' $landlordToken $null
Assert 'Landlord /me with token (200)' 200 $me | Out-Null
Assert 'Landlord GET /api/users (403)' 403 (Call GET '/api/users' $landlordToken $null) | Out-Null
Assert 'Landlord POST /api/users (403)' 403 (Call POST '/api/users' $landlordToken (RegBody 'Rej' "step3-landlord-post-$stamp@example.test" "070$stamp" 1 $pw $null $null $null)) | Out-Null

$tampered = $landlordToken.Substring(0, $landlordToken.Length-1) + $(if ($landlordToken[-1] -eq 'A') {'B'} else {'A'})
Assert 'tampered JWT (401)' 401 (Call GET '/api/auth/me' $tampered $null) | Out-Null

$payloadSeg = $landlordToken.Split('.')[1]
$noneToken = (B64Url '{"alg":"none","typ":"JWT"}') + '.' + $payloadSeg + '.'
Assert 'alg:none JWT (401)' 401 (Call GET '/api/auth/me' $noneToken $null) | Out-Null

$qsToken = [Uri]::EscapeDataString($landlordToken)
Assert 'query-string access_token (401)' 401 (Call GET ("/api/auth/me?access_token=$qsToken") $null $null) | Out-Null

# NOTE: the rate-limit boundary moved to the dedicated script step3_ratelimit.ps1,
# which runs against a fresh app start with RateLimit__AuthPerMinute=5 (see README.md).

# USERS has a filtered unique index (UX_USERS_PENDING_EMAIL), so its DML requires
# QUOTED_IDENTIFIER ON. sqlcmd defaults it OFF; -I enables it (SQL Server error 1934 otherwise).
function Invoke-SqlScalar {
  param([string]$Query)
  # Materialise the COMPLETE native output before filtering. Piping sqlcmd into
  # `Select-Object -First` closes its stdout early, and this sqlcmd build then exits -1 even
  # though the query succeeded; so collect all output, capture $LASTEXITCODE immediately
  # after sqlcmd finishes, and only then select the row in memory.
  $raw = @(& $sqlcmd -S $server -d $db -E -b -I -h -1 -W -Q $Query 2>&1 | ForEach-Object { [string]$_ })
  $rc = $LASTEXITCODE
  $rows = @($raw | Where-Object { $_ -ne '' })
  return [pscustomobject]@{
    ExitCode = $rc
    Value    = $(if ($rows.Count -gt 0) { [string]$rows[0] } else { '' })
    Output   = ($rows -join ' ')
  }
}

Log "==================== PHASE D (approved DB mutation: role change + restore) ===================="
Log ("  target disposable row ID={0} (is_deleted/role checked via read-only query)" -f $rcId)

if ($rcId -notmatch '^\d+$') {
  $script:sqlFail++
  Log ("  CRITICAL: disposable user ID is indeterminate ('{0}'); refusing mutation." -f $rcId)
  throw 'the disposable user ID is indeterminate; the role-change mutation is not safe to attempt.'
}

$schemaQ = "SET NOCOUNT ON; SELECT ROLE_ID FROM USERS WHERE ID = $rcId;"
$snapshot = Invoke-SqlScalar $schemaQ
$before = ([string]$snapshot.Value).Trim()
if ($snapshot.ExitCode -ne 0 -or $before -notmatch '^\d+$') {
  $script:sqlFail++
  Log ("  CRITICAL: original ROLE_ID is indeterminate (sqlcmd rc={0}, value='{1}'): {2}; refusing mutation." -f $snapshot.ExitCode, $before, $snapshot.Output)
  throw 'the original ROLE_ID is indeterminate; the role-change mutation is not safe to attempt.'
}
Log ("  snapshot: ROLE_ID before = {0}" -f $before)

$meBefore = Call GET '/api/auth/me' $rcToken $null
Assert 'role-change: /me before mutation (200)' 200 $meBefore | Out-Null

# The mutation and every subsequent assertion are inside this inner try. The restore is in
# the inner finally, so an exception or a terminating assertion cannot skip it. The outer
# finally still runs later and stops the harness-owned API.
$mutAttempted = $false
$restoreRc = -1
$restored = $null
$restoredSnap = $null
$restoreOk = $false

try {
  # Mark the attempt BEFORE invoking the UPDATE: even a failed or uncertain sqlcmd result
  # leaves the disposable row eligible for restoration.
  $mutAttempted = $true
  $mutQ = "SET NOCOUNT ON; UPDATE USERS SET ROLE_ID = 2 WHERE ID = $rcId;"
  $mutOut = @(& $sqlcmd -S $server -d $db -E -b -I -h -1 -W -Q $mutQ 2>&1 | ForEach-Object { [string]$_ })
  $mutRc = $LASTEXITCODE
  if ($mutRc -eq 0) {
    Log "  mutation applied: ROLE_ID 1 -> 2 (Investor)"
  } else {
    $script:sqlFail++
    Log ("  ERROR: ROLE_ID mutation failed (sqlcmd rc={0}): {1}" -f $mutRc, (($mutOut | Where-Object { $_ -ne '' }) -join ' '))
  }

  $afterSnap = Invoke-SqlScalar $schemaQ
  $after = $afterSnap.Value
  if ($afterSnap.ExitCode -ne 0) {
    $script:sqlFail++
    Log ("  ERROR: post-mutation verify query failed (sqlcmd rc={0}): {1}" -f $afterSnap.ExitCode, $afterSnap.Output)
  }
  Log ("  verify: ROLE_ID after mutation = {0}" -f $after)

  $meAfter = Call GET '/api/auth/me' $rcToken $null
  Assert 'role-change: old token now 401' 401 $meAfter | Out-Null
}
finally {
  if ($mutAttempted) {
    try {
      $restoreQ = "SET NOCOUNT ON; UPDATE USERS SET ROLE_ID = $before WHERE ID = $rcId;"
      $restoreOut = @(& $sqlcmd -S $server -d $db -E -b -I -h -1 -W -Q $restoreQ 2>&1 | ForEach-Object { [string]$_ })
      $restoreRc = $LASTEXITCODE
      if ($restoreRc -ne 0) {
        $script:sqlFail++
        Log ("  CRITICAL: RESTORE failed (sqlcmd rc={0}): {1} - the disposable row may be left mutated" -f $restoreRc, (($restoreOut | Where-Object { $_ -ne '' }) -join ' '))
      }

      $restoredSnap = Invoke-SqlScalar $schemaQ
      $restored = ([string]$restoredSnap.Value).Trim()
      if ($restoredSnap.ExitCode -ne 0) {
        $script:sqlFail++
        Log ("  CRITICAL: restore verification query failed (sqlcmd rc={0}): {1}" -f $restoredSnap.ExitCode, $restoredSnap.Output)
      }
      $restoreOk = ($restored -eq $before) -and ($restoreRc -eq 0) -and ($restoredSnap.ExitCode -eq 0)
      if ($restoreOk) {
        Log ("  restore applied: ROLE_ID -> {0}" -f $restored)
      } else {
        $script:sqlFail++
        Log ("  CRITICAL: final ROLE_ID verification failed: expected={0} restored={1} restore_rc={2} verify_rc={3}" -f $before, $restored, $restoreRc, $restoredSnap.ExitCode)
      }
    } catch {
      $script:sqlFail++
      $restoreOk = $false
      Log ("  CRITICAL: role restoration threw before it could be verified: {0}" -f $_.Exception.Message)
    }
  }
}

$meRestored = Call GET '/api/auth/me' $rcToken $null
Assert 'role-change: /me after restore (200)' 200 $meRestored | Out-Null
if ($restoreOk) { $script:pass++ } else { $script:fail++ }
$restoreTag = if ($restoreOk) { 'PASS' } else { 'FAIL' }
Log ("  restore verification: before={0} restored={1} -> {2}" -f $before,$restored,$restoreTag)

Log "==================== SUMMARY ===================="
Log ("PASS={0}  FAIL={1}" -f $script:pass,$script:fail)
Log ("REQUIRED-SQL-FAILURES={0}" -f $script:sqlFail)
$script:lines | Set-Content (Join-Path ([IO.Path]::GetTempPath()) 'step3_suite_results.txt')
$global:suiteFail = $script:fail + $script:sqlFail
}
finally {
  # The API is stopped through the harness, so only the process this suite started can be
  # stopped, and only while it still owns the port recorded in the identity file. The
  # protected run credentials are never rotated or removed here.
  try {
    Stop-TestApiInstance -Identity $harness
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and
           @(Get-NetTCPConnection -LocalPort ([int]$harness.Port) -State Listen -ErrorAction SilentlyContinue).Count -gt 0) {
      Start-Sleep -Milliseconds 250
    }
    $left = @(Get-NetTCPConnection -LocalPort ([int]$harness.Port) -State Listen -ErrorAction SilentlyContinue).Count
    Write-Host ("--- cleanup: harness-owned PID {0} stopped; port {1} listeners={2} ---" -f $harness.Pid, $harness.Port, $left)
    if ($left -ne 0) {
      Write-Host ("WARNING: port {0} still has a listener after the cleanup; the run result is kept." -f $harness.Port)
    }
  } catch {
    Write-Host ("WARNING: the harness-owned API cleanup reported an error (the test result is kept): {0}" -f $_.Exception.Message)
  }
}
# Explicit, Windows PowerShell 5.1-compatible exit: 0 only when every test passed AND every
# required SQL operation (snapshot, mutation, restore, restore verification) succeeded.
if ($script:fail -gt 0 -or $script:sqlFail -gt 0) { exit 1 } else { exit 0 }
