# Helper: start/stop the NibrasWeb app for verification runs (portable; no machine-specific paths).
#
# The instance is owned by the harness (test_harness.ps1): it starts on the DEDICATED port,
# it refuses an occupied port, it sets the explicit disposable connection string, and it proves
# that the process it started is the one that owns the port. A suite never talks to an
# instance it did not start.
$script:RepoDir = $PSScriptRoot
while ($script:RepoDir -and -not (Test-Path (Join-Path $script:RepoDir 'NibrasWeb.csproj'))) {
  $script:RepoDir = Split-Path $script:RepoDir -Parent
}
if (-not $script:RepoDir) { throw 'NibrasWeb.csproj not found above the script directory' }
$script:LogDir = Join-Path ([IO.Path]::GetTempPath()) 'nibrasweb-test-logs'
New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null

function Start-S4App {
  param(
    # 0 means the dedicated harness port. Any other value is refused: the suites may only
    # use the one dedicated port, so that they can never reach a foreign instance.
    [int]$Port = 0,
    # AuthPerMinute for the RateLimit middleware; functional suites run with 1000,
    # the dedicated rate-limit suite runs with 5 (see README.md).
    [int]$AuthPerMinute = 1000
  )
    . (Join-Path $PSScriptRoot 'test_harness.ps1')
    $dedicated = Get-TestApiPort
    if ($Port -gt 0 -and $Port -ne $dedicated) {
      throw "the suites may only use the dedicated harness port $dedicated (asked for $Port)."
    }
    return Start-TestApi -Port $dedicated -AuthPerMinute $AuthPerMinute
}

function Stop-S4App {
  param($App)
    # Only the process the harness started is stopped, and only the per-run outbox directory
    # it created is removed. A foreign listener is never killed.
    . (Join-Path $PSScriptRoot 'test_harness.ps1')
    try { Stop-TestApi -Identity $App } catch {}
}
