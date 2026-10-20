#requires -Version 5.1
# Step 3 auth rate-limit boundary test (dedicated; never prints tokens/secrets).
#
# IMPORTANT: run this script against a FRESH app start with a small limit:
#   . .\s4_app.ps1
#   Start-S4App -Port 5181 -AuthPerMinute 5
#   .\step3_ratelimit.ps1
#   Stop-S4App
# The functional suites run with a high limit (-AuthPerMinute 1000) so they are
# never throttled; only this script exercises the 429 boundary. All auth
# requests below use a non-existent account, so failures are expected 401s and
# no user data is created or mutated.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
# The suite owns its instance: the harness starts it on the dedicated port, with the
# explicit disposable connection string, and proves that it owns the port. The small
# limit is set before the start, so the instance really runs with it.
$env:NIBRAS_TEST_AUTH_PER_MINUTE = '5'
$harness = Start-TestApiInstance
$base = Initialize-TestApi
$pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
$stamp = (Get-Date).ToString('HHmmss') + (Get-Random -Minimum 100 -Maximum 999)
$limit = 5   # must match the RateLimit__AuthPerMinute the app was started with

$rows = New-Object System.Collections.ArrayList

function Call([string]$Method, [string]$Path, $Body = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($null -ne $Body) { $req.Body = (ConvertTo-Json -InputObject $Body -Depth 5 -Compress); $req.ContentType = 'application/json' }
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

function Check([string]$Id, [string]$Case, $Expected, $Actual) {
  $res = if ("$Expected" -eq "$Actual") { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Result = $res })
  Write-Output ("{0,-7} {1,-4} exp={2,-6} act={3,-6} {4}" -f $Id, $res, $Expected, $Actual, $Case)
}

Write-Output "== rate-limit boundary (fresh window, AuthPerMinute=$limit) =="
# Phase 1: exactly $limit auth requests are admitted (401 for a non-existent account).
$seq = @()
for ($i = 1; $i -le $limit; $i++) {
  $seq += (Call 'POST' '/api/auth/login' @{ email = "rl-$stamp@example.test"; password = $pw }).Status
}
Check 'RL-01' "first $limit logins admitted (401 each, permit consumed)" ($limit) (@($seq | Where-Object { $_ -eq 401 }).Count)

# Phase 2: the next request in the same window is rejected with 429.
$r = Call 'POST' '/api/auth/login' @{ email = "rl-$stamp@example.test"; password = $pw }
Check 'RL-02' 'next login in the same window -> 429' 429 $r.Status

# Phase 3: the limiter is shared by both auth endpoints (login and register).
$r = Call 'POST' '/api/auth/register' @{ fullName = 'RL Test'; email = "rl-reg-$stamp@example.test"; phone = '900000000'; password = $pw; roleId = 1 }
Check 'RL-03' 'register in the same exhausted window -> 429 (shared partition)' 429 $r.Status

# Phase 4: non-auth endpoints are not rate-limited by the "auth" policy.
$r = Call 'GET' '/api/lands' $null
Check 'RL-04' 'non-auth endpoint still 401 (not 429): auth policy scope' 401 $r.Status

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "RATELIMIT TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
exit 0
