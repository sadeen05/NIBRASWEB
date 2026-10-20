#requires -Version 5.1
# The single, fail-closed database target of the test suites.
#
# A suite never names a database itself: it resolves the target from the EFFECTIVE connection
# string of the application (ConnectionStrings__DefaultConnection), and only the disposable
# database on the approved instance is accepted. The shared target and the production-like
# name are always refused - there is no opt-in and no fallback. A missing, ambiguous or
# mismatched setting STOPS the run instead of silently using an existing database.
#
# The guard is additional to the application's own DatabaseTargetGuard, which is not weakened
# and not bypassed: the application resolves the same connection string and applies its own
# rules on top of it.
#
# Usage:
#   . (Join-Path $PSScriptRoot 'test_target.ps1')
#   $target = Get-TestTarget
#   $target.Server / $target.Database / $target.ConnectionString

$script:TestTargetDatabase = 'NIBRAS_F1_Verify'

# The approved SQL Server instance. It is a constant on purpose: an environment variable
# could point the run at any instance, so the override was removed. The local form of the
# same instance is accepted as well, because it is the same server.
$script:TestTargetServer = 'SADEEN\SQLEXPRESS'
$script:TestTargetServerAliases = @('SADEEN\SQLEXPRESS', "$env:COMPUTERNAME\SQLEXPRESS")

# Always refused, whatever the configuration says. There is no shared-target opt-in.
$script:RefusedTestDatabases = @('NEBRAS', 'NEBRASDB', 'NEBRASDB_Step1')

function Get-TestTarget {
  <#
    .SYNOPSIS
      The database the suites may run against, resolved from the effective connection string.
    .DESCRIPTION
      Fails closed: a missing, ambiguous or mismatched setting throws, and the run stops
      before a single statement is executed.
  #>
  $raw = $env:ConnectionStrings__DefaultConnection
  if ([string]::IsNullOrWhiteSpace($raw)) {
    throw ("ConnectionStrings__DefaultConnection is not set. The suites never fall back to an existing database. " +
      "Set it to the disposable database, e.g. `"Server=$script:TestTargetServer;Database=$script:TestTargetDatabase;Trusted_Connection=True;TrustServerCertificate=True;`"")
  }

  # The ambiguity is refused before anything resolves it: SQL Server's own parser picks the
  # last value of a repeated key, so the target would otherwise be silent.
  $databaseKeys = ([regex]::Matches($raw, '(?i)(?:^|;)\s*(?:database|initial\s+catalog)\s*=')).Count
  if ($databaseKeys -eq 0) {
    throw 'ConnectionStrings__DefaultConnection does not name a database.'
  }
  if ($databaseKeys -gt 1) {
    throw 'ConnectionStrings__DefaultConnection names more than one database; the target is ambiguous.'
  }

  $server = $null
  $database = $null
  foreach ($part in ($raw -split ';')) {
    $pair = $part -split '=', 2
    if ($pair.Count -ne 2) { continue }
    $key = $pair[0].Trim()
    $value = $pair[1].Trim().Trim('"').Trim("'")
    if ($key -match '^(?i)(server|data\s+source|addr|address|network\s+address)$') {
      if (-not $server) { $server = $value }
    }
    elseif ($key -match '^(?i)(database|initial\s+catalog)$') {
      if (-not $database) { $database = $value }
    }
  }

  if (-not $server) { throw 'ConnectionStrings__DefaultConnection does not name a server.' }
  if (-not $database) { throw 'ConnectionStrings__DefaultConnection does not name a database.' }

  foreach ($refused in $script:RefusedTestDatabases) {
    if ($database -ieq $refused) {
      throw "Refusing the database '$database'. The suites may only run against the disposable database '$script:TestTargetDatabase'."
    }
  }

  if ($database -ine $script:TestTargetDatabase) {
    throw "The target database must be '$script:TestTargetDatabase' (got '$database')."
  }
  $serverKnown = $false
  foreach ($alias in $script:TestTargetServerAliases) {
    if ($server -ieq $alias) { $serverKnown = $true; break }
  }
  if (-not $serverKnown) {
    throw "The target instance must be '$script:TestTargetServer' (got '$server'). The instance cannot be overridden by an environment variable."
  }

  return [pscustomobject]@{
    Server           = $server
    Database         = $database
    ConnectionString = $raw
  }
}

function Assert-TestTarget {
  <#
    .SYNOPSIS
      Stops the run when the target is not the approved disposable database.
    .DESCRIPTION
      Called at the start of a suite. It never changes anything; it only refuses to continue.
  #>
  param([string]$Database)

  $target = Get-TestTarget
  if ($Database -and $Database -ine $target.Database) {
    throw "The suite would run against '$Database'; only '$($target.Database)' is allowed."
  }
  return $target
}
