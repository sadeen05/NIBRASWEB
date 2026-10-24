#requires -Version 5.1
# Isolated source-extracted cleanup checks: no harness loading, API, DB or disk writes.
$ErrorActionPreference = 'Stop'
function Read-TestFunction([string]$File, [string]$Name) {
  $tokens = $null; $errors = $null
  $ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $File), [ref]$tokens, [ref]$errors)
  if ($errors.Count) { throw 'source parse failed' }
  $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name }, $true)
  if ($null -eq $node) { throw "missing function $Name" }
  return [scriptblock]::Create($node.Extent.Text)
}
$cleanup = Read-TestFunction 'verification_helpers.ps1' 'Complete-TestApiCleanup'
$stop = Read-TestFunction 'test_harness.ps1' 'Stop-TestApi'
$resolver = Read-TestFunction 'verification_helpers.ps1' 'Get-VerificationApiIdentityFile'
$pass = 0; $fail = 0
foreach ($case in @('success','listener','identity','process','foreign','resolution','portProbe','processProbe','missing','invalidPort','invalidPid')) {
  $ok = & {
    param($Case, $Cleanup)
    . $Cleanup
    $script:mockCase = $Case
    $script:stopCalls = 0
    function Stop-TestApiInstance { param($Identity) $script:stopCalls++; if ($script:mockCase -eq 'foreign') { throw 'ownership refused' } }
    function Get-VerificationApiIdentityFile { if ($script:mockCase -eq 'resolution') { throw 'helper unavailable' }; 'mock-identity' }
    function Test-Path { param($LiteralPath) return ($script:mockCase -eq 'identity') }
    function Get-NetTCPConnection { param($LocalPort,$State,$ErrorAction) if ($script:mockCase -eq 'portProbe') { throw 'port probe denied' }; if ($script:mockCase -eq 'listener') { [pscustomobject]@{ OwningProcess=12345 } } }
    function Get-Process { param($Id,$ErrorAction) if ($script:mockCase -eq 'processProbe') { throw 'process probe denied' }; if ($script:mockCase -eq 'process') { [pscustomobject]@{ Id=12345 } } }
    # Only the deadline is real; no sleep or operating-system observation is performed.
    function Start-Sleep { param($Milliseconds) }
    $caught = $null
    $identity = [pscustomobject]@{ Pid=12345; Port=5199 }
    if ($Case -eq 'missing') { $identity = $null }
    if ($Case -eq 'invalidPort') { $identity.Port = 0 }
    if ($Case -eq 'invalidPid') { $identity.Pid = 0 }
    try { $null = Complete-TestApiCleanup -Identity $identity } catch { $caught = $_.Exception.Message }
    $expected = switch ($Case) {
      'listener' { 'listener process' }
      'identity' { 'identity file still exists' }
      'process' { 'still alive' }
      'foreign' { 'ownership refused' }
      'resolution' { 'helper unavailable' }
      'portProbe' { 'port probe denied' }
      'processProbe' { 'process probe denied' }
      'missing' { 'no harness identity' }
      'invalidPort' { 'invalid PID or port' }
      'invalidPid' { 'invalid PID or port' }
    }
    if ($Case -eq 'success') { return ($null -eq $caught -and $script:stopCalls -eq 1) }
    $expectedStops = if ($Case -in @('missing','invalidPort','invalidPid')) { 0 } else { 1 }
    return ($null -ne $caught -and $caught -match [regex]::Escape($expected) -and $script:stopCalls -eq $expectedStops)
  } $case $cleanup
  if ($ok) { $pass++; Write-Output "$case PASS" } else { $fail++; Write-Output "$case FAIL" }
}
# Exercise the real ownership guard; no process or filesystem command may escape these mocks.
$ownership = & {
  param($Stop)
  . $Stop
  $script:killCalls = 0
  function Get-Process { param($Id,$ErrorAction) [pscustomobject]@{ Id=$Id } }
  function Get-NetTCPConnection { param($LocalPort,$State,$ErrorAction) [pscustomobject]@{ OwningProcess=99999 } }
  function Stop-Process { param($Id,$Force,$ErrorAction) $script:killCalls++; throw 'unexpected stop' }
  function Remove-Item { throw 'unexpected removal' }
  try { Stop-TestApi -Identity ([pscustomobject]@{ Pid=12345; Port=5199 }); return $false }
  catch { return ($_.Exception.Message -match 'refusing to stop' -and $script:killCalls -eq 0) }
} $stop
if ($ownership) { $pass++; 'real ownership guard PASS' } else { $fail++; 'real ownership guard FAIL' }
$resolution = & {
  param($Resolver)
  . $Resolver
  function Join-Path { throw 'mock harness resolution unavailable' }
  try { $null = Get-VerificationApiIdentityFile; return $false }
  catch { return ($_.Exception.Message -match 'mock harness resolution unavailable') }
} $resolver
if ($resolution) { $pass++; 'real resolver failure PASS' } else { $fail++; 'real resolver failure FAIL' }
Write-Output "CLEANUP_MOCK PASS=$pass FAIL=$fail"
if ($fail) { exit 1 }