#requires -Version 5.1
# Virtual filesystem/SQL snapshot mocks only. No files, services, connections or ACLs are changed.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'f1_storage_helpers.ps1')
$passCount=0;$failCount=0
$cases=@('retain','shutdown','multipleCommitted','secondMissing','legacy','missing','size','hash','changing','temporaryCleanupFailure',
  'temporaryStillPresent','referencedTemporary','orphan','foreignAcl','reparse','receiptMismatch',
  'snapshotFailure','receiptWriteFailure','unprotectedAcl','listener','identity','escapingKey','wrongLand','duplicate')
foreach ($case in $cases) {
  $ok=& {
    param($Case)
    $script:Case=$Case
    $script:Base='C:\NIBRAS-F1-Mock'
    $runId='a'*32
    $runDirectory=Join-Path $script:Base ('run-'+$runId)
    $root=Join-Path $runDirectory 'documents'
    $script:Receipt=[pscustomobject]@{
      Version=1;RunId=$runId;Server='SADEEN\SQLEXPRESS';Database='NIBRAS_F1_Verify'
      OwnerSid=(Get-F1OwnerSid).Value;RepositoryRoot='D:\tasks\try\NibrasWeb'
      RunDirectory=$runDirectory;StorageRoot=$root;LandNumber='F1-20261022000000-abcdef12'
      LandOwnerId=777;LandId=42;LandCreationAttempted=$true;Status='Prepared';Scope='CurrentRunOnly'
      Documents=@();RemovedTemporaryFiles=0;LastCheckedUtc=$null
    }
    $script:Saved=$script:Receipt | ConvertTo-Json -Depth 8
    if ($Case -eq 'receiptMismatch') { $script:Saved=$script:Saved.Replace($runId,('b'*32)) }
    $script:Private=New-F1PrivateDescriptor -Directory
    if ($Case -eq 'unprotectedAcl') { $script:Private.SetAccessRuleProtection($false,$true) }
    $script:Broad=New-F1PrivateDescriptor -Directory
    $foreign=New-Object Security.Principal.SecurityIdentifier('S-1-5-32-545')
    $rule=New-Object Security.AccessControl.FileSystemAccessRule($foreign,'Read','Allow')
    [void]$script:Broad.AddAccessRule($rule)
    $script:RealPath=Join-Path $root ('land-42/'+('c'*32)+'.pdf')
    $temp=Join-Path $root ('.tmp/'+('d'*32)+'.tmp')
    $bytes=[Text.Encoding]::ASCII.GetBytes('%PDF isolated bytes')
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
    $script:Files=@{}
    $script:Files[$script:RealPath]=[pscustomobject]@{Length=$bytes.Length;Hash=$hash;Time=[DateTime]'2026-10-22T00:00:00Z'}
    $script:Files[$temp]=[pscustomobject]@{Length=7;Hash=('e'*64);Time=[DateTime]'2026-10-22T00:00:00Z'}
    $row=[pscustomobject]@{Id=1000;LandId=42;Version=1;FilePath=('land-42/'+('c'*32)+'.pdf');FileSizeBytes=$bytes.Length;Sha256=$hash;ContentType='application/pdf';OriginalFileName='fixture.pdf'}
    $legacy=[pscustomobject]@{Id=1001;LandId=42;Version=2;FilePath='legacy/no-bytes.pdf';FileSizeBytes=$null;Sha256=$null;ContentType=$null;OriginalFileName=$null}
    $script:Rows=@($row,$legacy);$script:References=@($row.FilePath,$legacy.FilePath)
    if ($Case -in @('multipleCommitted','secondMissing')) {
      $second=[pscustomobject]@{Id=1002;LandId=42;Version=3;FilePath=('land-42/'+('f'*32)+'.pdf');FileSizeBytes=$bytes.Length;Sha256=$hash;ContentType='application/pdf';OriginalFileName='second.pdf'}
      $script:Rows+=,$second;$script:References+=,$second.FilePath
      if ($Case -eq 'multipleCommitted') { $script:Files[(Join-Path $root $second.FilePath)]=$script:Files[$script:RealPath] }
    }
    if ($Case -eq 'missing') { $script:Files.Remove($script:RealPath) }
    if ($Case -eq 'size') { $row.FileSizeBytes++ }
    if ($Case -eq 'hash') { $row.Sha256='f'*64 }
    if ($Case -eq 'escapingKey') { $row.FilePath='../escape.pdf' }
    if ($Case -eq 'wrongLand') { $row.LandId=43 }
    if ($Case -eq 'duplicate') { $script:Rows=@($row,$row) }
    if ($Case -eq 'referencedTemporary') { $script:References+=('.tmp/'+('d'*32)+'.tmp') }
    if ($Case -eq 'orphan') { $script:Files[(Join-Path $root ('land-42/'+('e'*32)+'.pdf'))]=$script:Files[$script:RealPath] }
    if ($Case -eq 'legacy') { $script:Rows=@($legacy);$script:Files=@{} }
    $script:Deletes=New-Object System.Collections.Generic.List[string]
    $script:Writes=0
    function Get-F1StorageBase { return $script:Base }
    function Test-Path {
      param($LiteralPath,$PathType)
      if ($LiteralPath -eq 'C:\mock-harness-identity.json') { return $script:Case -eq 'identity' }
      if ($PathType -eq 'Leaf') { return $script:Files.ContainsKey($LiteralPath) }
      # Virtual directories/ancestors and the manifest exist; missing document leaves do not.
      if ($LiteralPath -like '*.pdf' -or $LiteralPath -like '*.tmp') { return $script:Files.ContainsKey($LiteralPath) }
      return $true
    }
    function Get-Item {
      param($LiteralPath,[switch]$Force,$ErrorAction)
      $file=$script:Files[$LiteralPath]
      $attributes=if($file){[IO.FileAttributes]::Normal}else{[IO.FileAttributes]::Directory}
      if ($script:Case -eq 'reparse' -and $LiteralPath -eq $script:Receipt.StorageRoot) { $attributes=$attributes -bor [IO.FileAttributes]::ReparsePoint }
      return [pscustomobject]@{FullName=$LiteralPath;PSIsContainer=($null -eq $file);Attributes=$attributes;Length=$file.Length;LastWriteTimeUtc=$file.Time}
    }
    function Get-Acl {
      param($LiteralPath,$ErrorAction)
      if ($script:Case -eq 'foreignAcl' -and $LiteralPath -eq $script:RealPath) { return $script:Broad }
      return $script:Private
    }
    function Get-Content { param($LiteralPath,[switch]$Raw,$ErrorAction) return $script:Saved }
    function Get-FileHash {
      param($LiteralPath,$Algorithm,$ErrorAction)
      if ($Algorithm -ne 'SHA256') { throw 'Unexpected hash algorithm.' }
      $value=$script:Files[$LiteralPath]
      if ($script:Case -eq 'changing') { $value.Time=$value.Time.AddSeconds(1) }
      return [pscustomobject]@{Hash=$value.Hash}
    }
    function Get-ChildItem {
      param($LiteralPath,[switch]$Force,$ErrorAction)
      $prefix=$LiteralPath.TrimEnd('\')+'\'
      $dirs=New-Object 'System.Collections.Generic.HashSet[string]'
      foreach ($file in @($script:Files.Keys)) {
        if (-not $file.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { continue }
        $relative=$file.Substring($prefix.Length)
        if ($relative.Contains('\')) {
          $dir=Join-Path $LiteralPath ($relative.Split('\')[0])
          if ($dirs.Add($dir)) { Get-Item -LiteralPath $dir }
        } else { Get-Item -LiteralPath $file }
      }
    }
    function Remove-Item {
      param($LiteralPath,[switch]$Force,$ErrorAction)
      if ($LiteralPath -notlike '*\.tmp\*.tmp' -or $LiteralPath -eq $script:RealPath) { throw 'Mock forbids committed-file deletion.' }
      if ($script:Case -eq 'temporaryCleanupFailure') { throw 'Isolated deletion failure.' }
      [void]$script:Deletes.Add($LiteralPath)
      if ($script:Case -ne 'temporaryStillPresent') { $script:Files.Remove($LiteralPath) }
    }
    function Get-NetTCPConnection { param($State,$ErrorAction) if ($script:Case -eq 'listener') { [pscustomobject]@{LocalPort=5199} } }
    function Get-VerificationApiIdentityFile { return 'C:\mock-harness-identity.json' }
    function Get-F1RunDocumentSnapshot {
      param($Receipt)
      if ($script:Case -eq 'snapshotFailure') { throw 'Isolated snapshot failure.' }
      return [pscustomobject]@{LandId=42;Documents=$script:Rows;References=$script:References}
    }
    function Write-F1StorageReceipt {
      param($Receipt,$Create)
      $script:Writes++
      if ($script:Case -eq 'receiptWriteFailure') { throw 'Isolated receipt failure.' }
      $script:Saved=$Receipt | ConvertTo-Json -Depth 8
    }
    $caught=$null;$result=$null
    try {
      $result=Confirm-F1StorageRetention $script:Receipt
      if ($Case -eq 'shutdown') { $result=Confirm-F1StorageRetention $script:Receipt }
    } catch { $caught=$_.Exception.Message }
    if ($Case -in @('retain','shutdown','multipleCommitted','legacy')) {
      $expectedReal=if($Case -eq 'legacy'){0}elseif($Case -eq 'multipleCommitted'){2}else{1}
      $expectedDeleted=if($Case -eq 'legacy'){0}else{1}
      return ($null -eq $caught -and $script:Receipt.Status -eq 'Verified' -and
        $result.RealFiles -eq $expectedReal -and $result.LegacyRecords -eq 1 -and
        $script:Files.Count -eq $expectedReal -and $script:Deletes.Count -eq $expectedDeleted -and
        $(if($expectedReal){$script:Files.ContainsKey($script:RealPath)}else{$true}))
    }
    return ($null -ne $caught -and $null -eq $result -and
      $(if($Case -eq 'missing'){$true}else{$script:Files.ContainsKey($script:RealPath)}) -and
      @($script:Deletes | Where-Object {$_ -eq $script:RealPath}).Count -eq 0)
  } $case
  if ($ok -eq $true) { $passCount++;Write-Output "$case PASS" } else { $failCount++;Write-Output "$case FAIL" }
}
foreach ($case in @('newRun','wrongTarget','badMarker','insideRepository')) {
  $ok=& {
    param($Case)
    $script:InitializationCase=$Case
    $script:Created=New-Object System.Collections.Generic.List[string]
    $script:InitialReceipt=$null
    function Get-F1StorageBase { return 'C:\NIBRAS-F1-Mock' }
    function Get-TestTarget {
      [pscustomobject]@{Server=$(if($script:InitializationCase -eq 'wrongTarget'){'unapproved-instance'}else{'SADEEN\SQLEXPRESS'});Database='NIBRAS_F1_Verify'}
    }
    function New-F1PrivateDirectory { param($Path,[switch]$AllowExisting) [void]$script:Created.Add($Path) }
    function Write-F1StorageReceipt { param($Receipt,[switch]$Create) if(-not $Create){throw 'Unexpected initial receipt overwrite.'};$script:InitialReceipt=$Receipt }
    $repo=if($Case -eq 'insideRepository'){'C:\NIBRAS-F1-Mock'}else{'D:\tasks\try\NibrasWeb'}
    $marker=if($Case -eq 'badMarker'){'unowned'}else{'F1-20261022000000-abcdef12'}
    $caught=$false;$receipt=$null
    try { $receipt=New-F1RunStorage -RepositoryRoot $repo -LandNumber $marker } catch { $caught=$true }
    if($Case -ne 'newRun'){return ($caught -and $script:Created.Count -eq 0 -and $null -eq $script:InitialReceipt)}
    return (-not $caught -and $script:Created.Count -eq 3 -and $receipt.RunId -match '^[a-f0-9]{32}$' -and
      $receipt.StorageRoot -eq (Join-Path $receipt.RunDirectory 'documents') -and $receipt.Status -eq 'Prepared' -and
      $receipt.Scope -eq 'CurrentRunOnly' -and -not $receipt.LandCreationAttempted -and $receipt.Documents.Count -eq 0 -and
      [object]::ReferenceEquals($receipt,$script:InitialReceipt))
  } $case
  if($ok -eq $true){$passCount++;Write-Output "$case PASS"}else{$failCount++;Write-Output "$case FAIL"}
}
$descriptor=New-F1PrivateDescriptor -Directory
$sid=Get-F1OwnerSid
$rules=@($descriptor.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
if ($descriptor.AreAccessRulesProtected -and $descriptor.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $sid.Value -and
    $rules.Count -eq 1 -and $rules[0].IdentityReference.Value -eq $sid.Value -and
    $rules[0].InheritanceFlags -eq [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit') {
  $passCount++;'owner-only inheritable ACL descriptor PASS'
} else { $failCount++;'owner-only inheritable ACL descriptor FAIL' }
$fileDescriptor=New-F1PrivateDescriptor
$fileRules=@($fileDescriptor.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
if($fileDescriptor.AreAccessRulesProtected -and $fileRules.Count -eq 1 -and
   $fileDescriptor.GetOwner([Security.Principal.SecurityIdentifier]).Value -eq $sid.Value -and
   $fileRules[0].IdentityReference.Value -eq $sid.Value -and $fileRules[0].InheritanceFlags -eq 'None') {
  $passCount++;'owner-only protected receipt-file ACL descriptor PASS'
} else { $failCount++;'owner-only protected receipt-file ACL descriptor FAIL' }
$durable=Get-F1StorageBase
if ([IO.Path]::IsPathRooted($durable) -and -not (Test-F1Inside $durable 'D:\tasks\try\NibrasWeb') -and
    -not (Test-F1Inside $durable ([IO.Path]::GetTempPath())) -and
    -not (Test-F1Inside $durable 'C:\inetpub\wwwroot')) {
  $passCount++;'durable private base outside repository temp and web roots PASS'
} else { $failCount++;'durable private base outside repository temp and web roots FAIL' }
$createMethod=@([IO.Directory].GetMethods() | Where-Object {
  $_.Name -eq 'CreateDirectory' -and $_.GetParameters().Count -eq 2 -and
  $_.GetParameters()[1].ParameterType -eq [Security.AccessControl.DirectorySecurity]
})
if ($createMethod.Count -eq 1) { $passCount++;'PS51 atomic private-directory API PASS' } else { $failCount++;'PS51 atomic private-directory API FAIL' }
Write-Output "STORAGE_MOCK PASS=$passCount FAIL=$failCount"
if ($failCount) { exit 1 }
exit 0
