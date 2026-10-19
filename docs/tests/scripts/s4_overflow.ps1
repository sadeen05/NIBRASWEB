$ErrorActionPreference = 'Stop'
$sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { "$env:COMPUTERNAME\SQLEXPRESS" }
$dbName = 'NEBRASDB_Step1'
. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = $null
try {
  $app = Start-S4App -Port 5181
  $base = 'http://localhost:5181'
  function Probe([string]$Method,[string]$Path,$Body=$null,[string]$Token=$null){
    $req = @{ Uri=($base+$Path); Method=$Method; UseBasicParsing=$true; ErrorAction='Stop'; Headers=@{} }
    if ($Token) { $req.Headers['Authorization']='Bearer '+$Token }
    if ($null -ne $Body) { $req.Body=(ConvertTo-Json $Body -Compress); $req.ContentType='application/json' }
    try { $r=Invoke-WebRequest @req; return [pscustomobject]@{S=[int]$r.StatusCode;H=$r.Headers;B=[string]$r.Content} }
    catch { $resp=$_.Exception.Response; $t=''; if($resp){$t=(New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd()}
      return [pscustomobject]@{S=[int]$resp.StatusCode;H=$resp.Headers;B=$t} }
  }
  $stamp=(Get-Date).ToString('HHmmss')+(Get-Random -Minimum 100 -Maximum 999)
  $pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
  # Shared, idempotent test fixtures: resolve the test region/grid IDs by name (never
  # assume numeric IDs). Created only when missing; nothing is deleted or overwritten.
  . (Join-Path $PSScriptRoot 'test_fixtures.ps1')
  $fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $dbName
  Write-Output "fixtures: primaryRegion=$($fx.PrimaryRegionId) primaryGrid=$($fx.PrimaryGridId) secondaryRegion=$($fx.SecondaryRegionId)"
  $r=Probe 'POST' '/api/auth/register' @{fullName='Overflow Probe';email="ovf-$stamp@example.test";phone=('9'+('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)));password=$pw;roleId=1}
  $r=Probe 'POST' '/api/auth/login' @{email="ovf-$stamp@example.test";password=$pw}
  $tok=($r.B|ConvertFrom-Json).token
  function LandBody([decimal]$area,[string]$parcel){ @{governorateId=$fx.GovernorateId;landNumber="OVF-$stamp-$parcel";village='Village-O';basin='Basin-O';parcelNumber=$parcel;areaDonum=$area} }
  Write-Output '== areaDonum = 200,000,000 (passes Range, exceeds decimal(10,2)) =='
  $p=Probe 'POST' '/api/lands' (LandBody 200000000 "PO1-$stamp") $tok
  Write-Output ("status={0}" -f $p.S)
  Write-Output ("body={0}" -f $p.B)
  Write-Output ("CSP={0}" -f ($p.H['Content-Security-Policy']))
  Write-Output ''
  Write-Output '== areaDonum = 99,999,999.99 (decimal(10,2) max) =='
  $p=Probe 'POST' '/api/lands' (LandBody 99999999.99 "PO2-$stamp") $tok
  Write-Output ("status={0}" -f $p.S)
  Write-Output ''
  Write-Output '== areaDonum = 1,000,000,001 (above Range -> should be 400) =='
  $p=Probe 'POST' '/api/lands' (LandBody 1000000001 "PO3-$stamp") $tok
  Write-Output ("status={0}" -f $p.S)
} finally {
  Stop-S4App $app
}
