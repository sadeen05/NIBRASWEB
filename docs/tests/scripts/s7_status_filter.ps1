# Step 7 - status-filter regression suite (never prints credentials or tokens).
#
# Covers two defects fixed in Step 7:
#   * GET /api/lands?status=<undefined numeric>   used to return 200 (empty page)
#     and must now be rejected with 400.
#   * GET /api/offers/my?statusFilter=<unknown>   used to return 500 (the validation
#     exception was thrown inside the EF Core query-parameter extractor and wrapped
#     as an InvalidOperationException); it must now be a clean 400.
#
# Starts its own app once on the authorized test database, like s6_contracts.ps1.
$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5181'
$db = 'NEBRASDB_Step1'
$sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { '.\SQLEXPRESS' }
$rows = New-Object System.Collections.ArrayList

function Call([string]$Method, [string]$Path, [string]$Token = $null) {
  $req = @{ Uri = ($base + $Path); Method = $Method; UseBasicParsing = $true; ErrorAction = 'Stop'; Headers = @{} }
  if ($Token) { $req.Headers['Authorization'] = 'Bearer ' + $Token }
  try { $r = Invoke-WebRequest @req; return [int]$r.StatusCode }
  catch { $resp = $_.Exception.Response; if ($resp) { return [int]$resp.StatusCode } else { return -1 } }
}
function Check([string]$Id, [string]$Desc, $Expected, $Actual) {
  $res = if ("$Expected" -eq "$Actual") { 'PASS' } else { 'FAIL' }
  [void]$rows.Add([pscustomobject]@{ Id = $Id; Result = $res })
  Write-Output ("{0,-8} {1,-4} exp={2,-5} act={3,-5} {4}" -f $Id, $res, $Expected, $Actual, $Desc)
}

# --- target verification BEFORE any database access ---
$target = (@(& sqlcmd -S $sqlServer -E -d $db -W -h -1 -Q "SET NOCOUNT ON; SELECT DB_NAME();" 2>&1) | Where-Object { $_ }).Trim()
if ($target -ne $db) { Write-Output "BLOCKED: expected $db but connected to '$target'"; exit 2 }
Check 'SF-00' 'database target is NEBRASDB_Step1' $db $target

. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = Start-S4App -Port 5181 -AuthPerMinute 1000
try {
  $repoRoot = $PSScriptRoot
  while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
  $sec = @{}
  foreach ($line in @(& dotnet user-secrets list --project (Join-Path $repoRoot 'NibrasWeb.csproj') 2>$null)) {
    if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') { $sec[$Matches[1].Trim()] = $Matches[2] }
  }
  $body = @{ email = $sec['Seed:SuperAdminEmail']; password = $sec['Seed:SuperAdminPassword'] } | ConvertTo-Json -Compress
  $login = Invoke-WebRequest -Uri ($base + '/api/auth/login') -Method POST -UseBasicParsing -ContentType 'application/json' -Body $body
  $admin = ($login.Content | ConvertFrom-Json).token
  $sec = $null
  Check 'SF-01' 'seeded SuperAdmin login' 200 ([int]$login.StatusCode)

  Check 'SF-02' 'lands ?status=99 -> 400 (undefined numeric rejected)' 400 (Call 'GET' '/api/lands?status=99' $admin)
  Check 'SF-03' 'lands ?status=draft -> 400 (case-sensitive)' 400 (Call 'GET' '/api/lands?status=draft' $admin)
  Check 'SF-04' 'lands ?status=Draft -> 200 (valid name)' 200 (Call 'GET' '/api/lands?status=Draft' $admin)
  Check 'SF-05' 'offers ?statusFilter=NotAStatus -> 400 (no 500)' 400 (Call 'GET' '/api/offers/my?statusFilter=NotAStatus' $admin)
  Check 'SF-06' 'offers ?statusFilter=99 -> 400 (undefined numeric rejected)' 400 (Call 'GET' '/api/offers/my?statusFilter=99' $admin)
  Check 'SF-07' 'offers ?statusFilter=PendingAdminApproval -> 200' 200 (Call 'GET' '/api/offers/my?statusFilter=PendingAdminApproval' $admin)
  Check 'SF-08' 'offers ?statusFilter=1 OR 1=1 -> 400 (injection rejected)' 400 (Call 'GET' ("/api/offers/my?statusFilter=" + [uri]::EscapeDataString("1' OR '1'='1")) $admin)
}
finally { Stop-S4App $app }

$pass = @($rows | Where-Object { $_.Result -eq 'PASS' }).Count
$fail = @($rows | Where-Object { $_.Result -eq 'FAIL' }).Count
Write-Output ''
Write-Output "S7 STATUSFILTER TOTAL PASS=$pass FAIL=$fail"
if ($fail -gt 0) { exit 1 }
exit 0
