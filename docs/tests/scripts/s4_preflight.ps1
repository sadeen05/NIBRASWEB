$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$sqlServer = 'SADEEN\SQLEXPRESS'
$db = 'NEBRASDB_Step1'
$cs = "Server=$sqlServer;Database=$db;Integrated Security=True;TrustServerCertificate=True"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()

function Get-Tables([string]$sql) {
  $da = New-Object System.Data.SqlClient.SqlDataAdapter($sql, $conn)
  $dt = New-Object System.Data.DataTable
  [void]$da.Fill($dt)
  return ,$dt
}
function Scalar([string]$sql) {
  $cmd = $conn.CreateCommand(); $cmd.CommandText = $sql
  $o = $cmd.ExecuteScalar()
  if ($o -is [System.DBNull] -or $null -eq $o) { return $null }
  return $o
}

Write-Output '--- 1) port check ---'
$l = Get-NetTCPConnection -LocalPort 5181 -State Listen -ErrorAction SilentlyContinue
if ($l) { Write-Output 'FAIL: an app is already listening on 5181; abort'; exit 1 }
Write-Output 'port 5181 free'

Write-Output '--- 2) user-secrets: Seed email validation + collision check (value never printed) ---'
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
if (-not $repoRoot) { Write-Output 'FAIL: NibrasWeb.csproj not found above the script directory'; exit 1 }
$sec = @{}
foreach ($line in @(& dotnet user-secrets list --project (Join-Path $repoRoot 'NibrasWeb.csproj') 2>$null)) {
  if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') { $sec[$Matches[1].Trim()] = $Matches[2] }
}
$adminEmail = $sec['Seed:SuperAdminEmail']; $adminPass = $sec['Seed:SuperAdminPassword']
$sec = $null
if (-not $adminEmail -or -not $adminPass) { Write-Output 'FAIL: Seed:SuperAdmin* not configured in user-secrets'; exit 1 }
if ($adminEmail -notmatch '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$') { Write-Output 'FAIL: configured Seed email is not a valid address'; exit 1 }
$cmd = $conn.CreateCommand()
$cmd.CommandText = 'SELECT COUNT(*) FROM USERS WHERE EMAIL = @e AND ID <> -1'
$cmd.Parameters.Add((New-Object System.Data.SqlClient.SqlParameter('@e',$adminEmail))) | Out-Null
$collision = [int]$cmd.ExecuteScalar()
Write-Output ("Seed email collision with existing users (excl id -1): {0}" -f $collision)
if ($collision -gt 0) { Write-Output 'FAIL: configured Seed email already exists; abort (no writes)'; exit 1 }

Write-Output '--- 3) seeder phone 9999999999 collision ---'
Write-Output ("phone 9999999999 rows: {0}" -f (Scalar "SELECT COUNT(*) FROM USERS WHERE PHONE='9999999999'"))
if ((Scalar "SELECT COUNT(*) FROM USERS WHERE PHONE='9999999999'") -gt 0) { Write-Output 'FAIL: phone collision; abort'; exit 1 }

Write-Output '--- 4) interim FK target (smallest live positive user) ---'
$tempId = Scalar "SELECT MIN(ID) FROM USERS WHERE ID > 0 AND IS_DELETED = 0"
Write-Output ("interim criterion UPDATED_BY_ID target: {0}" -f $tempId)
if (-not $tempId) { Write-Output 'FAIL: no live user to re-point the criterion to'; exit 1 }

Write-Output '--- 5) snapshot for rollback ---'
$u = Get-Tables 'SELECT * FROM USERS WHERE ID = -1'
$c = Get-Tables 'SELECT * FROM LAND_CRITERIA WHERE ID = 1'
if ($u.Rows.Count -ne 1 -or $c.Rows.Count -ne 1) { Write-Output 'FAIL: expected exactly 1 placeholder user and 1 criterion row'; exit 1 }

function SqlVal($v) {
  if ($null -eq $v -or $v -is [System.DBNull]) { return 'NULL' }
  if ($v -is [DateTime]) { return "CONVERT(datetime2(0),'" + $v.ToString('yyyy-MM-ddTHH:mm:ss') + "')" }
  if ($v -is [bool]) { return $(if ($v) { '1' } else { '0' }) }
  if ($v -is [decimal]) { return $v.ToString([System.Globalization.CultureInfo]::InvariantCulture) }
  if ($v -is [int] -or $v -is [long] -or $v -is [double] -or $v -is [byte]) { return $v.ToString() }
  return "N'" + ([string]$v).Replace("'", "''") + "'"
}
function Row-Insert($dt, [string]$table) {
  $cols = @(); $vals = @()
  foreach ($col in $dt.Columns) { $cols += ("[" + $col.ColumnName + "]") }
  foreach ($row in $dt.Rows) { $vals += (($dt.Columns | ForEach-Object { SqlVal $row[$_.ColumnName] }) -join ', ') }
  return ("INSERT INTO {0} ({1}) VALUES ({2});" -f $table, ($cols -join ', '), $vals)
}

$out = Join-Path ([IO.Path]::GetTempPath()) 'admin_recovery_snapshot.sql'
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('-- NIBRAS Step 4 admin recovery (C1) rollback snapshot')
[void]$sb.AppendLine(('-- taken: ' + (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')))
[void]$sb.AppendLine('-- Apply in this order: 1) restore USERS id=-1 (criterion FK satisfied afterward),')
[void]$sb.AppendLine('--                   2) restore LAND_CRITERIA.UPDATED_BY_ID if it still differs.')
[void]$sb.AppendLine('')
$insUser = Row-Insert $u 'USERS'
[void]$sb.AppendLine($insUser)
[void]$sb.AppendLine("UPDATE LAND_CRITERIA SET UPDATED_BY_ID = -1 WHERE ID = 1;")
[void]$sb.AppendLine("-- optionally soft-delete the seeder-created SuperAdmin: UPDATE USERS SET IS_DELETED=1 WHERE ROLE_ID=4 AND PASSWORD_HASH <> 'N/A';")
[System.IO.File]::WriteAllText($out, $sb.ToString())
Write-Output ("snapshot rollback script written: {0} ({1} bytes)" -f $out, (Get-Item $out).Length)

$conn.Close()
Write-Output 'PREFLIGHT OK -- ready to apply the two approved writes'