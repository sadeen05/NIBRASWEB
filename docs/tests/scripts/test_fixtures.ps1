# Shared, idempotent test fixtures for the PowerShell suites.
#
# - Ensures two distinctly named TEST regions and one TEST grid in the primary region.
# - Nothing is deleted or overwritten: rows are inserted only when absent, matched by NAME.
# - IDs are RESOLVED by name and returned; callers must never assume numeric IDs.
# - Kept separate from the DEMO seed data (all names are prefixed "TEST ").
#
# Usage (after $sqlServer / $dbName are set):
#   . (Join-Path $PSScriptRoot 'test_fixtures.ps1')
#   $fx = Ensure-TestFixtures -SqlServer $sqlServer -Database $dbName
#   $fx.PrimaryRegionId / $fx.PrimaryGridId / $fx.SecondaryRegionId

function Ensure-TestFixtures {
  param(
    [Parameter(Mandatory)][string]$SqlServer,
    [Parameter(Mandatory)][string]$Database
  )

  $primaryRegionName   = 'TEST Region - Primary'
  $secondaryRegionName = 'TEST Region - Secondary'
  $primaryGridName     = 'TEST Grid - Primary 100MW'

  function Invoke-FixtureSql([string]$q) {
    $out = & sqlcmd -S $SqlServer -E -d $Database -b -W -h -1 -Q ("SET NOCOUNT ON; " + $q) 2>&1
    if ($LASTEXITCODE -ne 0) { throw "test fixture SQL failed: $out" }
    return (@($out | Where-Object { $_ }) -join "`n").Trim()
  }

  # Regions: insert only when the stable name is absent.
  Invoke-FixtureSql "IF NOT EXISTS (SELECT 1 FROM REGIONS WHERE NAME_EN = N'$primaryRegionName') INSERT INTO REGIONS (NAME_AR, NAME_EN, PEAK_SUN_HOURS_PER_DAY, WHEELING_FEE_PER_KWH, LOSS_PERCENTAGE) VALUES (N'$primaryRegionName', N'$primaryRegionName', 5.5, 0, 0);" | Out-Null
  Invoke-FixtureSql "IF NOT EXISTS (SELECT 1 FROM REGIONS WHERE NAME_EN = N'$secondaryRegionName') INSERT INTO REGIONS (NAME_AR, NAME_EN, PEAK_SUN_HOURS_PER_DAY, WHEELING_FEE_PER_KWH, LOSS_PERCENTAGE) VALUES (N'$secondaryRegionName', N'$secondaryRegionName', 5.5, 0, 0);" | Out-Null

  $primaryRegionId   = [int](Invoke-FixtureSql "SELECT CAST(ID AS varchar(10)) FROM REGIONS WHERE NAME_EN = N'$primaryRegionName';")
  $secondaryRegionId = [int](Invoke-FixtureSql "SELECT CAST(ID AS varchar(10)) FROM REGIONS WHERE NAME_EN = N'$secondaryRegionName';")

  if ($primaryRegionId -le 0 -or $secondaryRegionId -le 0) {
    throw "test fixture regions could not be resolved"
  }

  # Grid: insert only when the stable name is absent; generous capacity for test reservations.
  Invoke-FixtureSql "IF NOT EXISTS (SELECT 1 FROM GRIDS WHERE NAME = N'$primaryGridName') INSERT INTO GRIDS (REGION_ID, NAME, CAPACITY_MW, STATUS) VALUES ($primaryRegionId, N'$primaryGridName', 100, N'Operational');" | Out-Null
  $primaryGridId = [int](Invoke-FixtureSql "SELECT CAST(ID AS varchar(10)) FROM GRIDS WHERE NAME = N'$primaryGridName';")

  if ($primaryGridId -le 0) { throw "test fixture grid could not be resolved" }

  return [pscustomobject]@{
    PrimaryRegionId     = $primaryRegionId
    SecondaryRegionId   = $secondaryRegionId
    PrimaryGridId       = $primaryGridId
    PrimaryRegionName   = $primaryRegionName
    SecondaryRegionName = $secondaryRegionName
    PrimaryGridName     = $primaryGridName
  }
}
