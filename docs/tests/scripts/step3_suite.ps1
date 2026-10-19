#requires -Version 5.1
# Step 3 non-privileged test suite (temporary artifact; never prints tokens/secrets)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http | Out-Null

$base    = 'http://localhost:5181'
$stamp   = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$sqlcmd  = 'sqlcmd'   # resolved from PATH
$server  = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { "$env:COMPUTERNAME\SQLEXPRESS" }
$db      = 'NEBRASDB_Step1'   # approved target database (addendum)

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
$reg = Call POST '/api/auth/register' $null (RegBody 'Step3 Landlord' $landlordEmail $landlordPhone 1 $pw $null $null $null)
Assert 'register Landlord fixture (200)' 200 $reg | Out-Null
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
$invReg = Call POST '/api/auth/register' $null (RegBody 'Step3 Extra Investor' $invEmail $invPhone 2 $pw 'Step3 Co' 'STEP3-CO-01' $invExtra)
Assert 'register Investor + extra protected fields (200)' 200 $invReg | Out-Null
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
$rcReg = Call POST '/api/auth/register' $null (RegBody 'Step3 RoleChange' $rcEmail $rcPhone 1 $pw $null $null $null)
Assert 'register RoleChange landlord fixture (200)' 200 $rcReg | Out-Null
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

function Invoke-SqlScalar {
  param([string]$Query)
  # Materialise the COMPLETE native output before filtering. Piping sqlcmd into
  # `Select-Object -First` closes its stdout early, and this sqlcmd build then exits -1 even
  # though the query succeeded; so collect all output, capture $LASTEXITCODE immediately
  # after sqlcmd finishes, and only then select the row in memory.
  $raw = @(& $sqlcmd -S $server -d $db -E -b -h -1 -W -Q $Query 2>&1 | ForEach-Object { [string]$_ })
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

$schemaQ = "SET NOCOUNT ON; SELECT ROLE_ID FROM USERS WHERE ID = $rcId;"
$snapshot = Invoke-SqlScalar $schemaQ
$before = $snapshot.Value
if ($snapshot.ExitCode -ne 0) {
  $script:sqlFail++
  Log ("  ERROR: ROLE_ID snapshot query failed (sqlcmd rc={0}): {1}" -f $snapshot.ExitCode, $snapshot.Output)
}
Log ("  snapshot: ROLE_ID before = {0}" -f $before)

$meBefore = Call GET '/api/auth/me' $rcToken $null
Assert 'role-change: /me before mutation (200)' 200 $meBefore | Out-Null

$mutQ = "SET NOCOUNT ON; UPDATE USERS SET ROLE_ID = 2 WHERE ID = $rcId;"
& $sqlcmd -S $server -d $db -E -b -h -1 -W -Q $mutQ | Out-Null
$mutRc = $LASTEXITCODE
if ($mutRc -eq 0) {
  Log "  mutation applied: ROLE_ID 1 -> 2 (Investor)"
} else {
  $script:sqlFail++
  Log ("  ERROR: ROLE_ID mutation failed (sqlcmd rc={0})" -f $mutRc)
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

$restoreQ = "SET NOCOUNT ON; UPDATE USERS SET ROLE_ID = $before WHERE ID = $rcId;"
& $sqlcmd -S $server -d $db -E -b -h -1 -W -Q $restoreQ | Out-Null
$restoreRc = $LASTEXITCODE
if ($restoreRc -ne 0) {
  $script:sqlFail++
  Log ("  ERROR: RESTORE failed (sqlcmd rc={0}) - the disposable row may be left mutated" -f $restoreRc)
}
$restoredSnap = Invoke-SqlScalar $schemaQ
$restored = $restoredSnap.Value
if ($restoredSnap.ExitCode -ne 0) {
  $script:sqlFail++
  Log ("  ERROR: restore verification query failed (sqlcmd rc={0}): {1}" -f $restoredSnap.ExitCode, $restoredSnap.Output)
}
Log ("  restore applied: ROLE_ID -> {0}" -f $restored)

$meRestored = Call GET '/api/auth/me' $rcToken $null
Assert 'role-change: /me after restore (200)' 200 $meRestored | Out-Null
$restoreOk = ($restored -eq $before) -and ($restoreRc -eq 0) -and ($restoredSnap.ExitCode -eq 0)
if ($restoreOk) { $script:pass++ } else { $script:fail++ }
$restoreTag = if ($restoreOk) { 'PASS' } else { 'FAIL' }
Log ("  restore verification: before={0} restored={1} -> {2}" -f $before,$restored,$restoreTag)

Log "==================== SUMMARY ===================="
Log ("PASS={0}  FAIL={1}" -f $script:pass,$script:fail)
Log ("REQUIRED-SQL-FAILURES={0}" -f $script:sqlFail)
$script:lines | Set-Content (Join-Path ([IO.Path]::GetTempPath()) 'step3_suite_results.txt')
$global:suiteFail = $script:fail + $script:sqlFail
# Explicit, Windows PowerShell 5.1-compatible exit: 0 only when every test passed AND every
# required SQL operation (snapshot, mutation, restore, restore verification) succeeded.
if ($script:fail -gt 0 -or $script:sqlFail -gt 0) { exit 1 } else { exit 0 }
