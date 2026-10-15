# Helper: start/stop the NibrasWeb app for verification runs (portable; no machine-specific paths).
# Repo root is found by walking up from this script to NibrasWeb.csproj; logs go to %TEMP%.
$script:RepoDir = $PSScriptRoot
while ($script:RepoDir -and -not (Test-Path (Join-Path $script:RepoDir 'NibrasWeb.csproj'))) {
  $script:RepoDir = Split-Path $script:RepoDir -Parent
}
if (-not $script:RepoDir) { throw 'NibrasWeb.csproj not found above the script directory' }
$script:LogDir = Join-Path ([IO.Path]::GetTempPath()) 'nibrasweb-test-logs'
New-Item -ItemType Directory -Path $script:LogDir -Force | Out-Null

function Start-S4App {
  param(
    [int]$Port = 5181,
    # AuthPerMinute for the RateLimit middleware; functional suites run with 1000,
    # the dedicated rate-limit suite runs with 5 (see README.md).
    [int]$AuthPerMinute = 1000
  )
  # Fresh start: make sure nothing is still holding the port.
  try {
    Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
      ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
  } catch {}

  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_URLS = "http://localhost:$Port"
  # Connection string override (env var set by the caller) wins; otherwise target NEBRASDB_Step1.
  if (-not $env:ConnectionStrings__DefaultConnection) {
    $sqlServer = if ($env:NIBRAS_SQL_SERVER) { $env:NIBRAS_SQL_SERVER } else { '.\SQLEXPRESS' }
    $env:ConnectionStrings__DefaultConnection = "Server=$sqlServer;Database=NEBRASDB_Step1;Trusted_Connection=True;TrustServerCertificate=True;"
  }
  $env:RateLimit__AuthPerMinute = [string]$AuthPerMinute
  $out = Join-Path $script:LogDir "app_${Port}_out.log"
  $err = Join-Path $script:LogDir "app_${Port}_err.log"
  Remove-Item $out, $err -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath 'dotnet' -ArgumentList "bin\Debug\net10.0\NibrasWeb.dll" `
        -WorkingDirectory $script:RepoDir -PassThru -NoNewWindow `
        -RedirectStandardOutput $out -RedirectStandardError $err
  $deadline = (Get-Date).AddSeconds(60)
  $ready = $false
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 700
    try {
      $r = Invoke-WebRequest -Uri "http://localhost:$Port/api/lands" -UseBasicParsing -ErrorAction Stop
      $ready = $true; break
    } catch {
      $resp = $_.Exception.Response
      if ($resp -and [int]$resp.StatusCode -eq 401) { $ready = $true; break }
      if ($resp -and [int]$resp.StatusCode -gt 0) { $ready = $true; break }
    }
  }
  if (-not $ready) { throw "App did not become ready on port $Port. See $err" }
  [pscustomobject]@{ Process = $p; Port = $Port; Out = $out; Err = $err; AuthPerMinute = $AuthPerMinute }
}

function Stop-S4App {
  param($App)
  if ($App -and $App.Process) {
    try { Stop-Process -Id $App.Process.Id -Force -ErrorAction SilentlyContinue } catch {}
    Start-Sleep -Milliseconds 800
    # also kill any stray listener on the port
    try {
      Get-NetTCPConnection -LocalPort $App.Port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
    } catch {}
    Start-Sleep -Milliseconds 500
  }
}
