#requires -Version 5.1
# Focused real-filesystem checks for the Windows PowerShell 5.1 receipt replacement.
# Uses an isolated temp base outside the repository. No API, database, or product code is touched.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'f1_storage_helpers.ps1')

$pass = 0
$fail = 0
function Check([string]$Name, [bool]$Condition) {
  if ($Condition) { $script:pass++; Write-Output ("{0} PASS" -f $Name) }
  else { $script:fail++; Write-Output ("{0} FAIL" -f $Name) }
}
function Assert-Throws([string]$Name, [scriptblock]$Action) {
  $threw = $false
  try { & $Action } catch { $threw = $true }
  Check $Name $threw
}
function Get-ReceiptTempNames([string]$RunDirectory) {
  return @(Get-ChildItem -LiteralPath $RunDirectory -Force -ErrorAction Stop |
    Where-Object { $_.Name -match '^receipt-.*\.tmp$' -or $_.Name -match '^receipt-backup-.*\.tmp$' } |
    ForEach-Object { $_.Name })
}
function Read-Receipt([string]$Path) {
  return (Get-Content -LiteralPath $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop)
}

$repo = 'D:\tasks\try\NibrasWeb'
$testBase = Join-Path ([IO.Path]::GetTempPath()) ('nibras-f1-receipt-tests-' + [guid]::NewGuid().ToString('N'))
$script:TestBase = $testBase
function Get-F1StorageBase { return $script:TestBase }
function Get-TestTarget {
  return [pscustomobject]@{
    Server = 'SADEEN\SQLEXPRESS'
    Database = 'NIBRAS_F1_Verify'
    ConnectionString = 'Server=SADEEN\SQLEXPRESS;Database=NIBRAS_F1_Verify;Trusted_Connection=True;TrustServerCertificate=True;'
  }
}

try {
  if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'This check must run under Windows PowerShell 5.1.'
  }
  Check 'Windows PowerShell 5.1' $true

  # Effective snapshot connection string: the 8-second timeout must be present through the
  # supported Windows PowerShell 5.1 keyword form, with the approved target preserved.
  $snapshotTarget = [pscustomobject]@{
    ConnectionString = 'Server=SADEEN\SQLEXPRESS;Database=NIBRAS_F1_Verify;Trusted_Connection=True;TrustServerCertificate=True;'
  }
  $snapshotConnectionString = Get-F1RunDocumentConnectionString $snapshotTarget
  Check 'effective snapshot connection string uses 8-second Connect Timeout' ($snapshotConnectionString -match '(?i)(^|;)\s*Connect Timeout=8\s*(;|$)')
  Check 'effective snapshot connection string keeps the approved target' ($snapshotConnectionString -match '(?i)Data Source=SADEEN\\SQLEXPRESS' -and $snapshotConnectionString -match '(?i)Initial Catalog=NIBRAS_F1_Verify')
  Check 'effective snapshot connection string uses integrated security' ($snapshotConnectionString -match '(?i)Integrated Security=True')
  Check 'effective snapshot connection string has no unsupported ConnectTimeout keyword' (-not ($snapshotConnectionString -match '(?i)(^|;)\s*ConnectTimeout='))
  Assert-Throws 'wrong snapshot target fails closed' { Get-F1RunDocumentConnectionString ([pscustomobject]@{ ConnectionString = 'Server=SADEEN\SQLEXPRESS;Database=NEBRAS;Trusted_Connection=True;TrustServerCertificate=True;' }) }

  # 1. Initial receipt creation.
  $receipt = New-F1RunStorage -RepositoryRoot $repo -LandNumber 'F1-20261022000000-abcdef12'
  $receiptPath = Join-Path $receipt.RunDirectory 'storage-receipt.json'
  Check 'initial receipt creation' (Test-Path -LiteralPath $receiptPath -PathType Leaf)
  $initialAclOk = $false
  try { Assert-F1PrivatePath $receiptPath -RequireProtected; $initialAclOk = $true } catch { }
  Check 'initial receipt ACL protected' $initialAclOk
  Check 'initial receipt content' ((Read-Receipt $receiptPath).Status -eq 'Prepared')
  Check 'initial receipt has no staging/backup leftovers' ((Get-ReceiptTempNames $receipt.RunDirectory).Count -eq 0)

  # 2. Successful update of an existing receipt.
  $receipt.Status = 'Updated'
  $receipt.RemovedTemporaryFiles = 1
  Write-F1StorageReceipt $receipt
  $updated = Read-Receipt $receiptPath
  Check 'successful update of existing receipt' ($updated.Status -eq 'Updated' -and $updated.RemovedTemporaryFiles -eq 1)
  $updatedAclOk = $false
  try { Assert-F1PrivatePath $receiptPath -RequireProtected; $updatedAclOk = $true } catch { }
  Check 'updated receipt ACL protected' $updatedAclOk
  Check 'no staging/backup leftovers after success' ((Get-ReceiptTempNames $receipt.RunDirectory).Count -eq 0)

  # 3. Repeated updates.
  $repeatedOk = $true
  for ($i = 0; $i -lt 5; $i++) {
    $receipt.RemovedTemporaryFiles = $i
    Write-F1StorageReceipt $receipt
    if ((Read-Receipt $receiptPath).RemovedTemporaryFiles -ne $i) { $repeatedOk = $false }
  }
  Check 'repeated updates' $repeatedOk
  Check 'no staging/backup leftovers after repeated updates' ((Get-ReceiptTempNames $receipt.RunDirectory).Count -eq 0)

  # 4. Invalid source/destination path: an unexpected run directory must fail closed.
  $invalid = $receipt.PSObject.Copy()
  $invalid.RunDirectory = Join-Path $receipt.RunDirectory '..\escape'
  Assert-Throws 'invalid destination path fails closed' { Write-F1StorageReceipt $invalid }
  Check 'invalid path leaves valid receipt untouched' ((Read-Receipt $receiptPath).Status -eq 'Updated')

  # 5. Missing destination: an update must fail closed and must not create a new receipt.
  $missingRun = New-F1RunStorage -RepositoryRoot $repo -LandNumber 'F1-20261022000000-abcdef13'
  $missingPath = Join-Path $missingRun.RunDirectory 'storage-receipt.json'
  Remove-Item -LiteralPath $missingPath -Force -ErrorAction Stop
  Assert-Throws 'missing destination fails closed' { Write-F1StorageReceipt $missingRun }
  Check 'missing destination leaves no new receipt' (-not (Test-Path -LiteralPath $missingPath))

  # 6. Failed replacement and old-receipt recovery: a missing source must not destroy the destination.
  $before = (Get-Content -LiteralPath $receiptPath -Raw)
  $failedReplace = $false
  try {
    [IO.File]::Replace((Join-Path $receipt.RunDirectory 'missing.tmp'), $receiptPath, (Join-Path $receipt.RunDirectory 'receipt-backup-failure.tmp'))
  } catch { $failedReplace = $true }
  $after = (Get-Content -LiteralPath $receiptPath -Raw)
  Check 'failed replacement fails closed' $failedReplace
  Check 'failed replacement preserves the previous valid receipt' ($before -ceq $after)

  # 7. Backup/staging cleanup after verified success.
  $receipt.Status = 'Verified'
  Write-F1StorageReceipt $receipt
  Check 'verified success cleans backup and staging' ((Get-ReceiptTempNames $receipt.RunDirectory).Count -eq 0)

  # 8. No deletion of referenced document bytes.
  $docDir = Join-Path $receipt.StorageRoot 'land-42'
  New-Item -ItemType Directory -Path $docDir -Force | Out-Null
  $docPath = Join-Path $docDir (('a' * 32) + '.pdf')
  $docBytes = [Text.Encoding]::ASCII.GetBytes('%PDF receipt-check committed bytes')
  [IO.File]::WriteAllBytes($docPath, $docBytes)
  $receipt.Status = 'VerifiedAgain'
  Write-F1StorageReceipt $receipt
  Check 'committed document bytes survive receipt update' ((Test-Path -LiteralPath $docPath) -and ([IO.File]::ReadAllBytes($docPath).Length -eq $docBytes.Length))

  # 9. Correct nonzero failure reporting.
  $threw = $false
  try { Write-F1StorageReceipt $invalid } catch { $threw = $true }
  Check 'failure is reported as an exception' $threw

  Write-Output ("STORAGE_RECEIPT_CHECKS PASS={0} FAIL={1}" -f $pass, $fail)
  if ($fail -gt 0) { throw ("{0} receipt check(s) failed" -f $fail) }
  # Clean up only after every check passed.
  Remove-Item -LiteralPath $testBase -Recurse -Force -ErrorAction Stop
  exit 0
}
catch {
  Write-Output ("STORAGE_RECEIPT_CHECKS_FAIL PASS={0} FAIL={1} :: {2}" -f $pass, $fail, $_.Exception.Message)
  Write-Output ("PRESERVED_TEST_BASE={0}" -f $testBase)
  exit 1
}
