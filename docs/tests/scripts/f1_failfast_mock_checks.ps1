#requires -Version 5.1
# Source-extracted assertion/catch/finally checks. No suite, API, SQL, SMTP or disk writes.
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$path = Join-Path $PSScriptRoot 'f1_email_change_approval_documents.ps1'
$ast = [Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw 'focused suite parse failed' }
$definitions = foreach ($name in @('Log','Stop-F1Assertion','Assert-Exact','Assert-OneOf','Assert-True')) {
  $node = $ast.Find({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
  if ($null -eq $node) { throw "missing function $name" }
  $node.Extent.Text
}
$main = @($ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.TryStatementAst] })
if ($main.Count -ne 1 -or $main[0].CatchClauses.Count -ne 1) { throw 'ambiguous acceptance lifecycle' }
$main = $main[0]
$statements = @($main.Body.Statements)
$gateIndex = -1
for ($i=0; $i -lt $statements.Count; $i++) {
  if ($statements[$i] -is [Management.Automation.Language.IfStatementAst] -and
      $statements[$i].Extent.Text -match 'the FileOutbox phase reported') { $gateIndex=$i; break }
}
if ($gateIndex -lt 0) { throw 'FileOutbox gate missing' }
$gate = New-Object System.Collections.Generic.List[string]
for ($i=$gateIndex; $i -lt $statements.Count; $i++) {
  $gate.Add($statements[$i].Extent.Text)
  if ($statements[$i].Extent.Text -match '^\$null\s*=\s*Initialize-TestApi') { break }
}
if (($gate -join "`n") -notmatch "Start-TestApiInstance -DeliveryMode 'Disabled'") { throw 'Disabled gate missing' }
$catchText = $main.CatchClauses[0].Extent.Text
$finallyText = 'finally ' + $main.Finally.Extent.Text
$mockPassCount = 0; $mockFailCount = 0
foreach ($case in @('exactFailure','oneOfFailure','trueFailure','fatal','assertAndCleanupFailure','cleanupFailureAtBoundary','storageFailureAtBoundary','assertAndStorageFailure','success')) {
  $result = & {
    param($Case,$Definitions,$Gate,$CatchText,$FinallyText)
    foreach ($definition in $Definitions) { . ([scriptblock]::Create($definition)) }
    $script:pass=0; $script:fail=0; $script:skip=0
    $script:lines=New-Object System.Collections.Generic.List[string]
    $script:Harness=[pscustomobject]@{Pid=12345;Port=5199}
    $script:CleanupAttempted=$false; $script:StorageRoot=$null
    $script:StorageReceipt=[pscustomobject]@{RunId='mock-only'}
    $script:StorageApiStopped=$false; $script:StorageCheckAttempted=$false
    $script:StorageChecks=0
    $script:CleanupCalls=0; $script:DisabledStarts=0; $script:Later=0
    $script:IdentityPreserved=$true; $script:Case=$Case
    function Write-Host { param($Object) }
    function Complete-TestApiCleanup {
      param($Identity)
      $script:CleanupCalls++
      $script:IdentityPreserved=$script:IdentityPreserved -and [object]::ReferenceEquals($Identity,$script:Harness)
      if ($script:Case -in @('assertAndCleanupFailure','cleanupFailureAtBoundary')) { throw 'isolated cleanup failure' }
    }
    function Start-TestApiInstance {
      param($DeliveryMode)
      if ($DeliveryMode -ne 'Disabled' -or $script:CleanupCalls -ne 1 -or $null -ne $script:Harness) { throw 'Disabled started before verified FileOutbox cleanup' }
      $script:DisabledStarts++
      return [pscustomobject]@{Pid=12346;Port=5199}
    }
    function Initialize-TestApi { }
    function Confirm-F1StorageRetention {
      param($Receipt)
      if (-not $script:StorageApiStopped -or $null -ne $script:Harness) { throw 'storage inspected before containment' }
      $script:StorageChecks++
      if ($script:Case -in @('storageFailureAtBoundary','assertAndStorageFailure')) { throw 'isolated retained storage failure' }
      return [pscustomobject]@{RealFiles=1;LegacyRecords=1;ReceiptPath='mock-retained-receipt'}
    }
    function Test-Path { throw 'unexpected filesystem access' }
    function Remove-Item { throw 'unexpected deletion' }
    $body = switch ($Case) {
      'exactFailure' { "Assert-Exact 'isolated exact' 201 ([pscustomobject]@{Status=500}) | Out-Null" }
      'oneOfFailure' { "Assert-OneOf 'isolated one-of' @(403,404) ([pscustomobject]@{Status=200}) | Out-Null" }
      'trueFailure' { "Assert-True 'isolated boolean' `$false" }
      'assertAndCleanupFailure' { "Assert-True 'isolated boolean' `$false" }
      'assertAndStorageFailure' { "Assert-True 'isolated boolean' `$false" }
      'fatal' { "throw 'isolated fatal'" }
      default { "Assert-True 'isolated pass' `$true" }
    }
    # The assertions and both lifecycle blocks are the actual source; only the acceptance
    # workload and OS-facing cleanup/start dependencies are replaced.
    $text = "try {`n$body`n`$script:Later++`n" + ($Gate -join "`n") + "`n}`n$CatchText`n$FinallyText"
    . ([scriptblock]::Create($text))
    $expectedFail = if ($Case -eq 'success') {0} elseif ($Case -in @('assertAndCleanupFailure','assertAndStorageFailure')) {2} else {1}
    $expectedPass = if ($Case -in @('success','cleanupFailureAtBoundary','storageFailureAtBoundary')) {1} else {0}
    $expectedCleanup = if ($Case -eq 'success') {2} else {1}
    $expectedLater = if ($Case -in @('success','cleanupFailureAtBoundary','storageFailureAtBoundary')) {1} else {0}
    return ($script:fail -eq $expectedFail -and $script:pass -eq $expectedPass -and
      $script:CleanupCalls -eq $expectedCleanup -and $script:Later -eq $expectedLater -and
      $script:DisabledStarts -eq $(if ($Case -eq 'success') {1} else {0}) -and
      $script:IdentityPreserved -and
      $script:StorageChecks -eq $(if ($Case -eq 'success') {2} elseif ($Case -in @('assertAndCleanupFailure','cleanupFailureAtBoundary')) {0} else {1}) -and
      $(if ($Case -in @('assertAndCleanupFailure','cleanupFailureAtBoundary')) {$null -ne $script:Harness} else {$null -eq $script:Harness}))
  } $case $definitions @($gate) $catchText $finallyText
  if ($result -eq $true) { $mockPassCount++; Write-Output "$case PASS" } else { $mockFailCount++; Write-Output "$case FAIL" }
}
# Exercise the actual exit statements in isolated PowerShell processes, without the suite.
$exitGuard = @($ast.EndBlock.Statements | Where-Object {
  $_ -is [Management.Automation.Language.IfStatementAst] -and $_.Extent.Text -match 'if \(\$script:fail -gt 0\)'
})
$successExit = @($ast.EndBlock.Statements | Where-Object { $_ -is [Management.Automation.Language.ExitStatementAst] })
if ($exitGuard.Count -ne 1 -or $successExit.Count -ne 1) { throw 'ambiguous summary exit' }
foreach ($count in @(0,1,2)) {
  $scriptText = '$script:fail=' + $count + "`n" + $exitGuard[0].Extent.Text + "`n" + $successExit[0].Extent.Text
  $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($scriptText))
  $null = & (Join-Path $PSHOME 'powershell.exe') -NoLogo -NoProfile -NonInteractive -EncodedCommand $encoded
  $actualExit = $LASTEXITCODE
  $expectedExit = if ($count) {1} else {0}
  if ($actualExit -eq $expectedExit) {
    $mockPassCount++; Write-Output "summary failCount=$count exit=$actualExit PASS"
  } else { $mockFailCount++; Write-Output "summary failCount=$count exit=$actualExit FAIL" }
}
Write-Output "FAILFAST_MOCK PASS=$mockPassCount FAIL=$mockFailCount"
if ($mockFailCount) { exit 1 }
exit 0
