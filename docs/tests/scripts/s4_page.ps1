$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 's4_app.ps1')
$app = $null
try {
  $app = Start-S4App -Port 5181
  $base = 'http://localhost:5181'
  function Probe([string]$Path,$Token=$null){
    $req = @{ Uri=($base+$Path); Method='GET'; UseBasicParsing=$true; ErrorAction='Stop'; Headers=@{} }
    if ($Token) { $req.Headers['Authorization']='Bearer '+$Token }
    try { $r=Invoke-WebRequest @req; return [pscustomobject]@{S=[int]$r.StatusCode;B=[string]$r.Content} }
    catch { $resp=$_.Exception.Response; $t=''; if($resp){$t=(New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd()}
      return [pscustomobject]@{S=[int]$resp.StatusCode;B=$t} }
  }
  function Post([string]$Path,$Body,[string]$Token=$null){
    $req = @{ Uri=($base+$Path); Method='POST'; UseBasicParsing=$true; ErrorAction='Stop'; Headers=@{}; Body=(ConvertTo-Json $Body -Compress); ContentType='application/json' }
    if ($Token) { $req.Headers['Authorization']='Bearer '+$Token }
    try { $r=Invoke-WebRequest @req; return [pscustomobject]@{S=[int]$r.StatusCode;B=[string]$r.Content} }
    catch { $resp=$_.Exception.Response; $t=''; if($resp){$t=(New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd()}
      return [pscustomobject]@{S=[int]$resp.StatusCode;B=$t} }
  }
  $stamp=(Get-Date).ToString('HHmmss')+(Get-Random -Minimum 100 -Maximum 999)
  $pw = if ($env:NIBRAS_TEST_PASSWORD) { $env:NIBRAS_TEST_PASSWORD } else { 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,10) } # runtime-generated credential, never a literal
  Post '/api/auth/register' @{fullName='Page Probe';email="pg-$stamp@example.test";phone=('9'+('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)));password=$pw;roleId=1} | Out-Null
  $r=Post '/api/auth/login' @{email="pg-$stamp@example.test";password=$pw}
  $tok=($r.B|ConvertFrom-Json).token
  foreach ($q in @('pageNumber=1&pageSize=20','pageNumber=0','pageNumber=-5','pageNumber=2000000000&pageSize=50','pageNumber=2147483647&pageSize=50','pageNumber=50000000&pageSize=50')) {
    $p = Probe ("/api/lands?" + $q) $tok
    Write-Output ("{0,-45} -> {1}  {2}" -f $q, $p.S, ($p.B.Substring(0,[Math]::Min(110,$p.B.Length))))
  }
} finally { Stop-S4App $app }
