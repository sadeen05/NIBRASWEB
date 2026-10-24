#requires -Version 5.1
# Exercise real request construction against an in-memory client; no sockets or credentials.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$t=$null; $e=$null
$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'verification_helpers.ps1'),[ref]$t,[ref]$e)
if ($e.Count) { throw 'helper source parse failed' }
$node=$ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Invoke-ConcurrentApiRequests'},$true)
if ($null -eq $node) { throw 'concurrent helper not found' }
. ([scriptblock]::Create($node.Extent.Text))
$script:captured=New-Object System.Collections.ArrayList
$script:disposed=$false
$script:fakeClient=[pscustomobject]@{Timeout=[TimeSpan]::Zero}
$script:fakeClient | Add-Member ScriptMethod Dispose { $script:disposed=$true }
$script:fakeClient | Add-Member ScriptMethod SendAsync {
  param($Message)
  $parts=@{}
  $json=$null
  if ($Message.Content -is [System.Net.Http.MultipartFormDataContent]) {
    foreach ($part in $Message.Content) {
      $name=$part.Headers.ContentDisposition.Name.Trim('"')
      $parts[$name]=[pscustomobject]@{
        Bytes=$part.ReadAsByteArrayAsync().Result
        FileName=$part.Headers.ContentDisposition.FileName
        ContentType=$part.Headers.ContentType.MediaType
      }
    }
  } else { $json=$Message.Content.ReadAsStringAsync().Result }
  [void]$script:captured.Add([pscustomobject]@{
    Method=$Message.Method.Method; Uri=$Message.RequestUri.AbsoluteUri
    Token=if($Message.Headers.Authorization){$Message.Headers.Authorization.Parameter}else{$null}
    Parts=$parts; Json=$json
  })
  $response=New-Object System.Net.Http.HttpResponseMessage([System.Net.HttpStatusCode]::Created)
  $response.Content=New-Object System.Net.Http.StringContent('{"mock":true}')
  $completion=New-Object 'System.Threading.Tasks.TaskCompletionSource[System.Net.Http.HttpResponseMessage]'
  $completion.SetResult($response)
  return $completion.Task
}
function New-Object {
  param([Parameter(Position=0)][string]$TypeName,[Parameter(Position=1)][object[]]$ArgumentList)
  if ($TypeName -eq 'System.Net.Http.HttpClient') { return $script:fakeClient }
  if ($PSBoundParameters.ContainsKey('ArgumentList')) { return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName -ArgumentList $ArgumentList }
  return Microsoft.PowerShell.Utility\New-Object -TypeName $TypeName
}
function Get-VerificationApiUri { param($Path) [Uri]('http://127.0.0.1:5199'+$Path) }
$oldLimit=[Net.ServicePointManager]::DefaultConnectionLimit
try {
  $bytes=[Text.Encoding]::ASCII.GetBytes('%PDF-mock-only')
  $results=Invoke-ConcurrentApiRequests -Requests @(
    @{Method='POST';Path='/mock/json';Json='{"value":1}';BearerToken='mock-owner'},
    @{Method='POST';Path='/mock/file';FileBytes=$bytes;FileName='mock.pdf';DocumentTypeId=7;BearerToken='mock-other'}
  )
  $checks=[ordered]@{
    'JSON request contract retained'=($script:captured[0].Json -eq '{"value":1}' -and $script:captured[0].Method -eq 'POST')
    'multipart fields and URI'=($script:captured[1].Parts.Count -eq 2 -and $script:captured[1].Uri -eq 'http://127.0.0.1:5199/mock/file' -and [Text.Encoding]::UTF8.GetString($script:captured[1].Parts.documentTypeId.Bytes) -eq '7')
    'multipart bytes filename and MIME'=([Convert]::ToBase64String($script:captured[1].Parts.file.Bytes) -eq [Convert]::ToBase64String($bytes) -and $script:captured[1].Parts.file.FileName.Trim('"') -eq 'mock.pdf' -and $script:captured[1].Parts.file.ContentType -eq 'application/pdf')
    'authorization remains per request'=($script:captured[0].Token -eq 'mock-owner' -and $script:captured[1].Token -eq 'mock-other')
    'results retain shape order status and body'=($results.Count -eq 2 -and $results[0].Status -eq 201 -and $results[1].Status -eq 201 -and $results[0].Body -eq '{"mock":true}')
    'client disposed'=($script:disposed)
  }
  $pass=0; $fail=0
  foreach ($name in $checks.Keys) { if ($checks[$name]) { $pass++; "$name PASS" } else { $fail++; "$name FAIL" } }
  "REQUEST_MOCK PASS=$pass FAIL=$fail"
  if ($fail) { exit 1 }
} finally { [Net.ServicePointManager]::DefaultConnectionLimit=$oldLimit }