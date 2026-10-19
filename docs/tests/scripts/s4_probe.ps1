$ErrorActionPreference = 'Stop'
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
  $r=Probe 'POST' '/api/auth/register' @{fullName='Hdr Probe';email="hdr-$stamp@example.test";phone=('9'+('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999)));password=$pw;roleId=1}
  $r=Probe 'POST' '/api/auth/login' @{email="hdr-$stamp@example.test";password=$pw}
  $tok=($r.B|ConvertFrom-Json).token

  Write-Output '== 401 unauth GET /api/lands =='
  $p=Probe 'GET' '/api/lands'
  Write-Output ("status={0} redirect={1}" -f $p.S, ($p.H['Location']))
  Write-Output ("ACAO={0}" -f ($p.H['Access-Control-Allow-Origin']))
  Write-Output ("HSTS={0}" -f ($p.H['Strict-Transport-Security']))
  Write-Output ("CSP={0}" -f ($p.H['Content-Security-Policy']))
  Write-Output ("nosniff={0} frame={1} referrer={2}" -f $p.H['X-Content-Type-Options'],$p.H['X-Frame-Options'],$p.H['Referrer-Policy'])

  Write-Output ''
  Write-Output '== 404 authed GET /api/lands/999999 (body + traceId) =='
  $p=Probe 'GET' '/api/lands/999999' $null $tok
  Write-Output ("status={0}" -f $p.S)
  Write-Output ("CSP={0}" -f ($p.H['Content-Security-Policy']))
  Write-Output ("body={0}" -f $p.B)

  Write-Output ''
  Write-Output '== HTTP->HTTPS redirect check: 2xx/4xx (not 307) means no redirect in Dev =='
  Write-Output '== Swagger (Dev only) =='
  $p=Probe 'GET' '/swagger/index.html'
  Write-Output ("swagger status={0} CSP={1}" -f $p.S, ($p.H['Content-Security-Policy']))
  $p=Probe 'GET' '/swagger/v1/swagger.json'
  Write-Output ("swagger.json status={0}" -f $p.S)
} finally {
  Stop-S4App $app
}
