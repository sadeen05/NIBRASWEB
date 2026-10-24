#requires -Version 5.1
# Test-only retained storage. Loading this file performs no filesystem or SQL operation.
function Get-F1StorageBase {
  return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NIBRAS_F1_RetainedDocuments'
}
function Get-F1OwnerSid { return [Security.Principal.WindowsIdentity]::GetCurrent().User }
function Test-F1Inside([string]$Path,[string]$Directory) {
  $p=[IO.Path]::GetFullPath($Path).TrimEnd('\')
  $d=[IO.Path]::GetFullPath($Directory).TrimEnd('\')
  return $p.Equals($d,[StringComparison]::OrdinalIgnoreCase) -or $p.StartsWith($d+'\',[StringComparison]::OrdinalIgnoreCase)
}
function Assert-F1NoReparse([string]$Path) {
  $current=[IO.Path]::GetFullPath($Path)
  while ($current) {
    if (Test-Path -LiteralPath $current) {
      $item=Get-Item -LiteralPath $current -Force -ErrorAction Stop
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Retained storage contains a reparse point.' }
    }
    $current=[IO.Path]::GetDirectoryName($current)
  }
}
function New-F1PrivateDescriptor([switch]$Directory) {
  $sid=Get-F1OwnerSid
  $acl=if ($Directory) { New-Object Security.AccessControl.DirectorySecurity } else { New-Object Security.AccessControl.FileSecurity }
  $acl.SetOwner($sid)
  $acl.SetAccessRuleProtection($true,$false)
  $inherit=if ($Directory) { [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit' } else { [Security.AccessControl.InheritanceFlags]::None }
  $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'FullControl',$inherit,'None','Allow')
  [void]$acl.AddAccessRule($rule)
  return $acl
}
function Assert-F1PrivatePath([string]$Path,[switch]$RequireProtected) {
  Assert-F1NoReparse $Path
  $acl=Get-Acl -LiteralPath $Path -ErrorAction Stop
  $sid=Get-F1OwnerSid
  if ($RequireProtected -and -not $acl.AreAccessRulesProtected) { throw 'Retained storage ACL inheritance is not protected.' }
  if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $sid.Value) { throw 'Retained storage owner mismatch.' }
  $rules=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
  $full=[Security.AccessControl.FileSystemRights]::FullControl
  if ($rules.Count -eq 0 -or @($rules | Where-Object {
    $_.IdentityReference.Value -ne $sid.Value -or $_.AccessControlType -ne 'Allow'
  }).Count -ne 0 -or @($rules | Where-Object { ($_.FileSystemRights -band $full) -eq $full }).Count -eq 0) {
    throw 'Retained storage ACL is not owner-only full control.'
  }
}
function New-F1PrivateDirectory([string]$Path,[switch]$AllowExisting) {
  Assert-F1NoReparse $Path
  if (Test-Path -LiteralPath $Path) {
    if (-not $AllowExisting) { throw 'Run storage directory already exists.' }
  } else {
    # Windows PowerShell 5.1 creates the directory with this DACL atomically.
    $null=[IO.Directory]::CreateDirectory($Path,(New-F1PrivateDescriptor -Directory))
  }
  Assert-F1PrivatePath $Path -RequireProtected
}
function Assert-F1StoragePaths($Receipt) {
  $parsed=[guid]::Empty
  if ($Receipt.Version -ne 1 -or -not [guid]::TryParseExact([string]$Receipt.RunId,'N',[ref]$parsed) -or
      $Receipt.Server -cne 'SADEEN\SQLEXPRESS' -or $Receipt.Database -cne 'NIBRAS_F1_Verify' -or
      $Receipt.OwnerSid -ne (Get-F1OwnerSid).Value -or $Receipt.LandNumber -notmatch '^F1-\d{14}-[a-f0-9]{8}$') {
    throw 'Invalid retained-storage ownership receipt.'
  }
  $expected=Join-Path (Get-F1StorageBase) ('run-'+$Receipt.RunId)
  if ([IO.Path]::GetFullPath($Receipt.RunDirectory) -ine [IO.Path]::GetFullPath($expected) -or
      [IO.Path]::GetFullPath($Receipt.StorageRoot) -ine [IO.Path]::GetFullPath((Join-Path $expected 'documents')) -or
      (Test-F1Inside $expected $Receipt.RepositoryRoot) -or
      (Test-F1Inside $expected (Join-Path $Receipt.RepositoryRoot 'wwwroot')) -or
      (Test-F1Inside $expected 'C:\inetpub\wwwroot')) { throw 'Unsafe retained-storage location.' }
  Assert-F1PrivatePath (Get-F1StorageBase) -RequireProtected
  Assert-F1PrivatePath $Receipt.RunDirectory -RequireProtected
  Assert-F1PrivatePath $Receipt.StorageRoot -RequireProtected
}
function Assert-F1StorageReceipt($Receipt,[switch]$Creating) {
  Assert-F1StoragePaths $Receipt
  if (-not $Creating) {
    $path=Join-Path $Receipt.RunDirectory 'storage-receipt.json'
    Assert-F1PrivatePath $path -RequireProtected
    $saved=Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    foreach ($name in @('Version','RunId','Server','Database','OwnerSid','RunDirectory','StorageRoot','RepositoryRoot','LandNumber')) {
      if ([string]$saved.$name -cne [string]$Receipt.$name) { throw 'Stored ownership receipt mismatch.' }
    }
  }
}
function Write-F1StorageReceipt($Receipt,[switch]$Create) {
  Assert-F1StorageReceipt $Receipt -Creating:$Create
  $path=Join-Path $Receipt.RunDirectory 'storage-receipt.json'
  $staging=Join-Path $Receipt.RunDirectory ('receipt-'+[guid]::NewGuid().ToString('N')+'.tmp')
  $destination=if ($Create) { $path } else { $staging }
  $stream=New-Object IO.FileStream($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
  try {
    $bytes=[Text.Encoding]::UTF8.GetBytes(($Receipt | ConvertTo-Json -Depth 8))
    $stream.Write($bytes,0,$bytes.Length)
    $stream.Flush($true)
  } finally { $stream.Dispose() }
  Set-Acl -LiteralPath $destination -AclObject (New-F1PrivateDescriptor) -ErrorAction Stop
  Assert-F1PrivatePath $destination
  if ($Create) {
    Assert-F1PrivatePath $path
    return
  }
  # Windows PowerShell 5.1 cannot pass $null as File.Replace's backup path: it throws
  # "The path is not of a legal form." Use a valid, uniquely owned backup path inside the
  # protected run directory. File.Replace is crash-safe: on failure the original receipt
  # remains in place and the staging/backup files are preserved for diagnosis.
  $backup=Join-Path $Receipt.RunDirectory ('receipt-backup-'+[guid]::NewGuid().ToString('N')+'.tmp')
  [IO.File]::Replace($staging,$path,$backup)
  Assert-F1PrivatePath $path
  Assert-F1StorageReceipt $Receipt
  # The backup holds the previous valid receipt. Remove it only after the new receipt has
  # been verified and is owner-protected. If cleanup fails, preserve it and fail closed.
  if (Test-Path -LiteralPath $backup) {
    Assert-F1PrivatePath $backup
    Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $backup) { throw 'Receipt backup cleanup incomplete.' }
  }
}
function New-F1RunStorage([string]$RepositoryRoot,[string]$LandNumber) {
  $target=Get-TestTarget
  if ($target.Server -cne 'SADEEN\SQLEXPRESS' -or $target.Database -cne 'NIBRAS_F1_Verify') { throw 'Retained-storage target mismatch.' }
  $runId=[guid]::NewGuid().ToString('N')
  $directory=Join-Path (Get-F1StorageBase) ('run-'+$runId)
  $receipt=[pscustomobject]@{
    Version=1;RunId=$runId;Server=$target.Server;Database=$target.Database;OwnerSid=(Get-F1OwnerSid).Value
    CreatedUtc=[DateTime]::UtcNow.ToString('o');RepositoryRoot=[IO.Path]::GetFullPath($RepositoryRoot)
    RunDirectory=$directory;StorageRoot=(Join-Path $directory 'documents');LandNumber=$LandNumber
    LandOwnerId=0;LandId=0;LandCreationAttempted=$false;Status='Prepared';Scope='CurrentRunOnly'
    Documents=@();RemovedTemporaryFiles=0;LastCheckedUtc=$null
  }
  # Reject unsafe roots before creating any directories.
  if ($LandNumber -notmatch '^F1-\d{14}-[a-f0-9]{8}$' -or
      (Test-F1Inside $directory $RepositoryRoot) -or (Test-F1Inside $directory 'C:\inetpub\wwwroot')) { throw 'Unsafe new run location or marker.' }
  New-F1PrivateDirectory (Get-F1StorageBase) -AllowExisting
  New-F1PrivateDirectory $directory
  New-F1PrivateDirectory $receipt.StorageRoot
  Write-F1StorageReceipt $receipt -Create
  return $receipt
}
function Register-F1StorageLandAttempt($Receipt,[int]$OwnerId) {
  Assert-F1StorageReceipt $Receipt
  if ($OwnerId -le 0 -or $Receipt.LandCreationAttempted) { throw 'Invalid or repeated land-creation receipt.' }
  $Receipt.LandOwnerId=$OwnerId
  $Receipt.LandCreationAttempted=$true
  Write-F1StorageReceipt $Receipt
}
function Assert-F1StorageQuiescent {
  $listeners=@(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq 5199)
  if ($listeners.Count -ne 0 -or (Test-Path -LiteralPath (Get-VerificationApiIdentityFile))) {
    throw 'API containment is unresolved; retained storage was left untouched.'
  }
}
function Get-F1RunDocumentConnectionString($Target) {
  # Test-only: builds the effective snapshot connection string with the intended 8-second
  # timeout. Windows PowerShell 5.1 rejects the ConnectTimeout property setter; the supported
  # keyword form is the indexer below. The approved target is re-checked here as well.
  $builder=New-Object System.Data.SqlClient.SqlConnectionStringBuilder($Target.ConnectionString)
  if ($builder.DataSource -cne 'SADEEN\SQLEXPRESS' -or $builder.InitialCatalog -cne 'NIBRAS_F1_Verify' -or
      -not $builder.IntegratedSecurity) { throw 'Document snapshot target mismatch.' }
  $builder['Connect Timeout']=8
  return $builder.ConnectionString
}
function Get-F1RunDocumentSnapshot($Receipt) {
  # Runtime-only SELECTs. This function is mocked during offline validation.
  Assert-F1StorageReceipt $Receipt
  $target=Get-TestTarget
  $connectionString=Get-F1RunDocumentConnectionString $target
  $connection=New-Object System.Data.SqlClient.SqlConnection($connectionString)
  try {
    $connection.Open()
    $command=$connection.CreateCommand();$command.CommandTimeout=10
    try {
      $command.CommandText="SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME()"
      $reader=$command.ExecuteReader()
      try { if (-not $reader.Read() -or $reader.GetString(0) -cne 'SADEEN\SQLEXPRESS' -or $reader.GetString(1) -cne 'NIBRAS_F1_Verify') { throw 'Database identity mismatch.' } }
      finally { $reader.Dispose() }
      $landId=0
      if ($Receipt.LandCreationAttempted) {
        if ([int]$Receipt.LandOwnerId -le 0) { throw 'Missing run-owned landlord identity.' }
        $command.CommandText='SELECT ID FROM dbo.LANDS WHERE LAND_NUMBER=@marker AND LANDLORD_ID=@owner'
        [void]$command.Parameters.Add('@marker',[Data.SqlDbType]::NVarChar,50)
        $command.Parameters['@marker'].Value=$Receipt.LandNumber
        [void]$command.Parameters.Add('@owner',[Data.SqlDbType]::Int)
        $command.Parameters['@owner'].Value=[int]$Receipt.LandOwnerId
        $reader=$command.ExecuteReader()
        try {
          while ($reader.Read()) { if ($landId -ne 0) { throw 'Ambiguous run-owned land.' };$landId=$reader.GetInt32(0) }
        } finally { $reader.Dispose() }
        $command.Parameters.Clear()
      }
      if ([int]$Receipt.LandId -gt 0 -and $landId -ne [int]$Receipt.LandId) { throw 'Run land identity mismatch.' }
      $rows=New-Object System.Collections.Generic.List[object]
      if ($landId -gt 0) {
        $command.CommandText='SELECT ID,LAND_ID,VERSION,FILE_PATH,FILE_SIZE_BYTES,SHA256,CONTENT_TYPE,ORIGINAL_FILE_NAME FROM dbo.LAND_DOCUMENTS WHERE LAND_ID=@land ORDER BY ID'
        [void]$command.Parameters.Add('@land',[Data.SqlDbType]::Int);$command.Parameters['@land'].Value=$landId
        $reader=$command.ExecuteReader()
        try { while ($reader.Read()) {
          [void]$rows.Add([pscustomobject]@{
            Id=$reader.GetInt32(0);LandId=$reader.GetInt32(1);Version=$reader.GetInt32(2);FilePath=$reader.GetString(3)
            FileSizeBytes=$(if($reader.IsDBNull(4)){$null}else{$reader.GetInt64(4)})
            Sha256=$(if($reader.IsDBNull(5)){$null}else{$reader.GetString(5)})
            ContentType=$(if($reader.IsDBNull(6)){$null}else{$reader.GetString(6)})
            OriginalFileName=$(if($reader.IsDBNull(7)){$null}else{$reader.GetString(7)})
          })
        } } finally { $reader.Dispose() }
        $command.Parameters.Clear()
      }
      # Include references from every row, including legacy/history, before deleting any .tmp.
      $command.CommandText='SELECT FILE_PATH FROM dbo.LAND_DOCUMENTS WHERE FILE_PATH IS NOT NULL'
      $references=New-Object System.Collections.Generic.List[string]
      $reader=$command.ExecuteReader()
      try { while ($reader.Read()) { [void]$references.Add($reader.GetString(0).Replace('\','/')) } }
      finally { $reader.Dispose() }
      return [pscustomobject]@{LandId=$landId;Documents=@($rows.ToArray());References=@($references.ToArray())}
    } finally { $command.Dispose() }
  } catch { throw 'Run-owned document snapshot failed; all stored bytes were retained.' }
  finally { $connection.Dispose() }
}
function Get-F1StorageFiles([string]$Root) {
  $queue=New-Object System.Collections.Generic.Queue[string]
  $queue.Enqueue($Root)
  while ($queue.Count -gt 0) {
    $directory=$queue.Dequeue()
    Assert-F1PrivatePath $directory
    foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)) {
      Assert-F1PrivatePath $item.FullName
      if ($item.PSIsContainer) { $queue.Enqueue($item.FullName) } else { $item.FullName }
    }
  }
}
function Get-F1FileFacts([string]$Path) {
  Assert-F1PrivatePath $Path
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'A retained document file is missing.' }
  $before=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
  $hash=Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop
  $after=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
  if ($before.Length -ne $after.Length -or $before.LastWriteTimeUtc -ne $after.LastWriteTimeUtc) {
    throw 'A retained file changed during integrity inspection.'
  }
  return [pscustomobject]@{Length=$after.Length;Sha256=$hash.Hash.ToLowerInvariant()}
}
function Confirm-F1StorageRetention($Receipt) {
  Assert-F1StorageReceipt $Receipt
  Assert-F1StorageQuiescent
  try {
    $snapshot=Get-F1RunDocumentSnapshot $Receipt
    if ($snapshot.LandId -lt 0 -or ([int]$Receipt.LandId -gt 0 -and $snapshot.LandId -ne $Receipt.LandId)) { throw 'Snapshot ownership mismatch.' }
    $expected=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $ids=New-Object 'System.Collections.Generic.HashSet[int]'
    $records=New-Object System.Collections.Generic.List[object]
    foreach ($row in @($snapshot.Documents)) {
      if ([int]$row.Id -le 0 -or -not $ids.Add([int]$row.Id) -or [int]$row.LandId -ne $snapshot.LandId -or [int]$row.Version -lt 1) { throw 'Invalid run document identity.' }
      if ($null -eq $row.FileSizeBytes) {
        if ($null -ne $row.Sha256 -or $null -ne $row.ContentType -or $null -ne $row.OriginalFileName) { throw 'Inconsistent legacy-only metadata.' }
        [void]$records.Add([pscustomobject]@{Id=$row.Id;LandId=$row.LandId;Version=$row.Version;HasFile=$false})
        continue
      }
      $extension=switch ($row.ContentType) { 'application/pdf' {'pdf'} 'image/jpeg' {'jpg'} 'image/png' {'png'} default {throw 'Invalid retained content type.'} }
      $pattern='^land-'+[int]$row.LandId+'/[a-f0-9]{32}\.'+$extension+'$'
      if ($row.FilePath -cnotmatch $pattern -or [long]$row.FileSizeBytes -le 0 -or
          $row.Sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [string]::IsNullOrWhiteSpace($row.OriginalFileName)) { throw 'Invalid server-generated file metadata.' }
      $path=[IO.Path]::GetFullPath((Join-Path $Receipt.StorageRoot $row.FilePath.Replace('/','\')))
      if (-not (Test-F1Inside $path $Receipt.StorageRoot) -or -not $expected.Add($path)) { throw 'Duplicate or escaping retained file key.' }
      $facts=Get-F1FileFacts $path
      if ($facts.Length -ne [long]$row.FileSizeBytes -or $facts.Sha256 -ine $row.Sha256) { throw 'Retained document size or SHA-256 mismatch.' }
      [void]$records.Add([pscustomobject]@{Id=$row.Id;LandId=$row.LandId;Version=$row.Version;HasFile=$true;RelativePath=$row.FilePath;FileSizeBytes=$row.FileSizeBytes;Sha256=$facts.Sha256})
    }
    $temporary=New-Object System.Collections.Generic.List[string]
    $prefix=$Receipt.StorageRoot.TrimEnd('\')+'\'
    foreach ($file in @(Get-F1StorageFiles $Receipt.StorageRoot)) {
      $full=[IO.Path]::GetFullPath($file)
      if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Escaping storage inventory.' }
      if ($expected.Contains($full)) { continue }
      $relative=$full.Substring($prefix.Length).Replace('\','/')
      if ($relative -cnotmatch '^\.tmp/[a-f0-9]{32}\.tmp$' -or @($snapshot.References | Where-Object { $_.Replace('\','/') -ieq $relative }).Count -ne 0) {
        throw 'Unexpected or referenced file preserved for investigation.'
      }
      [void]$temporary.Add($full)
    }
    # Only unreferenced staging files are removable; committed and uncertain files stay put.
    foreach ($file in $temporary) {
      Assert-F1PrivatePath $file
      Remove-Item -LiteralPath $file -Force -ErrorAction Stop
      if (Test-Path -LiteralPath $file) { throw 'Temporary-upload cleanup incomplete.' }
    }
    $remaining=@(Get-F1StorageFiles $Receipt.StorageRoot)
    if ($remaining.Count -ne $expected.Count -or @($remaining | Where-Object {-not $expected.Contains([IO.Path]::GetFullPath($_))}).Count -ne 0) { throw 'Storage inventory changed after cleanup.' }
    $Receipt.LandId=$snapshot.LandId
    $Receipt.Documents=@($records.ToArray())
    $Receipt.RemovedTemporaryFiles=[int]$Receipt.RemovedTemporaryFiles+$temporary.Count
    $Receipt.Status='Verified'
    $Receipt.LastCheckedUtc=[DateTime]::UtcNow.ToString('o')
    Write-F1StorageReceipt $Receipt
    return [pscustomobject]@{RealFiles=$expected.Count;LegacyRecords=@($records | Where-Object {-not $_.HasFile}).Count;RemovedTemporaryFiles=$temporary.Count;ReceiptPath=(Join-Path $Receipt.RunDirectory 'storage-receipt.json')}
  } catch {
    $Receipt.Status='FAILED'
    # Failure to persist diagnostics must also remain a failure; never fabricate success.
    Write-F1StorageReceipt $Receipt
    throw
  }
}
