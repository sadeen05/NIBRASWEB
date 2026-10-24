#requires -Version 5.1
# The harness-owned API instance for the F1 tests.
#
# The suites never talk to an instance they did not start. The harness starts its own
# process on a DEDICATED port, sets the explicit disposable connection string, and proves the
# identity of what it launched: the PID it started is the one that owns the port, and the
# effective configuration is the disposable one. A port that is already occupied is a FAILURE:
# the harness never connects to a process it did not start.
#
# The identity is written to a small file so that a suite can verify it BEFORE its first
# write. The suites therefore never need a probe registration to protect themselves - the
# probe is optional additional evidence, never the primary protection.
#
# Usage:
#   . (Join-Path $PSScriptRoot 'test_harness.ps1')
#   $h = Start-TestApi                       # starts and proves the instance
#   ...                                      # the suites run against $h.BaseUrl
#   Stop-TestApi -Harness $h                 # stops only the process it started
#
# The harness writes only inside its own per-run outbox directory and its own identity file.

$script:TestApiIdentityFile = Join-Path ([IO.Path]::GetTempPath()) 'nibras-f1-api-identity.json'
$script:TestRunCredentialFile = Join-Path ([IO.Path]::GetTempPath()) 'nibras-f1-run-credentials.json'
$script:TestApiDefaultPort = 5199

Add-Type -AssemblyName System.Security | Out-Null

function Protect-TestFileToOwner {
  <#
    .SYNOPSIS
      Restricts a file to the current user, dropping every inherited permission.
    .DESCRIPTION
      The run credentials must not be readable by another local account. When the restriction
      cannot be applied, the caller fails closed instead of leaving the file unprotected.
  #>
  param([Parameter(Mandatory)][string]$Path)

  try {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
    $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($me, 'FullControl', 'Allow')
    $acl.AddAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
  } catch {
    throw "the credential file '$Path' could not be restricted to the current user: $($_.Exception.Message)"
  }
}

function ConvertTo-ProtectedString {
  <#
    .SYNOPSIS
      Encrypts a value with DPAPI, scoped to the current Windows user.
  #>
  param([Parameter(Mandatory)][string]$Plain)

  $bytes = [System.Text.Encoding]::UTF8.GetBytes($Plain)
  $blob = [System.Security.Cryptography.ProtectedData]::Protect(
    $bytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
  return [Convert]::ToBase64String($blob)
}

function ConvertFrom-ProtectedString {
  <#
    .SYNOPSIS
      Decrypts a DPAPI value that was encrypted for the current Windows user.
  #>
  param([Parameter(Mandatory)][string]$Protected)

  $blob = [Convert]::FromBase64String($Protected)
  $bytes = [System.Security.Cryptography.ProtectedData]::Unprotect(
    $blob, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
  return [System.Text.Encoding]::UTF8.GetString($bytes)
}

function Get-TestRunCredentials {
  <#
    .SYNOPSIS
      The disposable SuperAdmin identity of THIS approved run.
    .DESCRIPTION
      The identity and the password are generated once and then reused by every start of the
      run - the Phase B probe and the Phase C acceptance run use the same account, so the
      seeding stays idempotent across repeated starts of the same disposable database.

      The secret is stored DPAPI-encrypted for the current Windows user, in a file that is
      restricted to that user, outside the repository. It is never printed, never committed and
      never rotated silently:

      * a file that cannot be decrypted fails closed (it is never treated as a plaintext secret);
      * a file that belongs to another database fails closed (it is never reused across runs);
      * -NewRun deliberately discards the previous identity and generates a new one.
  #>
  param([switch]$NewRun)

  $target = Get-TestTarget

  if ($NewRun) { Clear-TestRunCredentials }

  if (Test-Path -LiteralPath $script:TestRunCredentialFile) {
    $raw = Get-Content -LiteralPath $script:TestRunCredentialFile -Raw
    try { $envelope = $raw | ConvertFrom-Json } catch { $envelope = $null }

    if (-not $envelope -or -not $envelope.Protected -or -not $envelope.Database) {
      throw ("the run credential file '{0}' is not in the protected format (a plaintext or unknown store is refused). " +
        "End the run with Clear-TestRunCredentials and start it again." -f $script:TestRunCredentialFile)
    }
    if ($envelope.Database -ine $target.Database) {
      throw ("the run credential file '{0}' belongs to another database ('{1}'). It is never reused across runs: " +
        "end the run with Clear-TestRunCredentials or start a new one with -NewRun." -f $script:TestRunCredentialFile, $envelope.Database)
    }

    try { $secret = ConvertFrom-ProtectedString -Protected $envelope.Protected } catch {
      throw ("the run credential file '{0}' cannot be decrypted by this Windows user. It is refused: " +
        "end the run with Clear-TestRunCredentials and start a new one." -f $script:TestRunCredentialFile)
    }

    try { $credentials = $secret | ConvertFrom-Json } catch { $credentials = $null }
    if (-not $credentials -or -not $credentials.Email -or -not $credentials.Password) {
      throw "the decrypted run credentials are incomplete; the run must be restarted."
    }

    return [pscustomobject]@{
      Email    = $credentials.Email
      Password = $credentials.Password
      RunId    = $envelope.RunId
      Database = $envelope.Database
      Created  = $envelope.Created
    }
  }

  $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
  $fresh = [pscustomobject]@{
    Email    = "f1-superadmin-$stamp@example.test"
    Password = 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,14) + 'Aa9'
  }
  $envelope = [pscustomobject]@{
    Version   = 2
    RunId     = [guid]::NewGuid().ToString('N')
    Database  = $target.Database
    Created   = (Get-Date).ToUniversalTime().ToString('o')
    Protected = (ConvertTo-ProtectedString -Plain ($fresh | ConvertTo-Json -Compress))
  }
  $envelope | ConvertTo-Json | Set-Content -LiteralPath $script:TestRunCredentialFile -Encoding UTF8
  Protect-TestFileToOwner -Path $script:TestRunCredentialFile

  return [pscustomobject]@{
    Email    = $fresh.Email
    Password = $fresh.Password
    RunId    = $envelope.RunId
    Database = $envelope.Database
    Created  = $envelope.Created
  }
}

function Clear-TestRunCredentials {
  <#
    .SYNOPSIS
      Ends the approved run: the disposable identity is discarded.
    .DESCRIPTION
      The NEXT approved run then seeds its own disposable SuperAdmin. The credentials are never
      rotated while a run is in progress, and nothing but this file is removed.
  #>
  Remove-Item -LiteralPath $script:TestRunCredentialFile -Force -ErrorAction SilentlyContinue
}

function Get-TestRunCredentialFile { return $script:TestRunCredentialFile }

function Get-TestApiPort {
  $port = if ($env:NIBRAS_TEST_API_PORT) { [int]$env:NIBRAS_TEST_API_PORT } else { $script:TestApiDefaultPort }
  if ($port -lt 1024 -or $port -gt 65535) { throw "the API port must be between 1024 and 65535 (got $port)" }
  return $port
}

function Get-TestApiIdentityFile { return $script:TestApiIdentityFile }

function Assert-TestApiPortFree {
  <#
    .SYNOPSIS
      Refuses a port that is already in use.
    .DESCRIPTION
      A busy port means that somebody else is listening: the harness would then be talking
      to a process it does not own, so the run stops.
  #>
  param([int]$Port)

  $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
  if ($listeners.Count -gt 0) {
    $owners = ($listeners | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique) -join ','
    throw "the port $Port is already in use (process $owners). The harness never connects to a process it did not start: choose another port with NIBRAS_TEST_API_PORT."
  }
}

function Get-TestApiOutboxPath {
  <#
    .SYNOPSIS
      A fresh, private per-run outbox directory outside the repository.
    .DESCRIPTION
      The directory holds the live verification links, so it is created per run, outside any
      Git working tree, and it is the only directory the harness ever removes.
  #>
  $root = Join-Path ([IO.Path]::GetTempPath()) ("nibras-f1-outbox-" + [guid]::NewGuid().ToString('N').Substring(0,12))
  New-Item -ItemType Directory -Path $root -Force | Out-Null
  return (Resolve-Path -LiteralPath $root).Path
}

function Start-TestApi {
  <#
    .SYNOPSIS
      Starts the API instance the suites use, and proves that it is the right one.
    .DESCRIPTION
      The instance is started by this harness, on a dedicated port, with the explicit
      disposable connection string. After the start the harness verifies that the process it
      started is the one that owns the port, and it records the identity so that a suite can
      verify it before its first write.
  #>
  param(
    [int]$Port = 0,
    [int]$AuthPerMinute = 1000,
    # FileOutbox is the only mode the token-reading suites can use. Disabled is accepted for
    # the negative delivery test: it starts the same guarded instance but every send fails
    # closed, so a suite can prove that a failed delivery changes no active address.
    [ValidateSet('FileOutbox', 'Disabled')]
    [string]$DeliveryMode = 'FileOutbox'
  )

  . (Join-Path $PSScriptRoot 'test_target.ps1')
  $target = Get-TestTarget
  $port = if ($Port -gt 0) { $Port } else { Get-TestApiPort }
  Assert-TestApiPortFree -Port $port

  $outbox = Get-TestApiOutboxPath
  $env:NIBRAS_EMAIL_OUTBOX = $outbox

  # The effective configuration of the instance: the disposable database, the per-run outbox,
  # the raised rate limits, and the disposable seed credentials when they are provided.
  # DOTNET_ENVIRONMENT is set as well: it takes precedence over ASPNETCORE_ENVIRONMENT, so
  # leaving it to the caller's shell would change how the instance starts.
  $env:DOTNET_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_ENVIRONMENT = 'Development'
  $env:ASPNETCORE_URLS = "http://127.0.0.1:$port"
  $env:ConnectionStrings__DefaultConnection = $target.ConnectionString
  # The application's own guard stays at its strictest: an empty list approves nothing.
  $env:Database__ApprovedProductionLikeTargets = ''
  $env:Email__DeliveryMode = $DeliveryMode
  $env:Email__OutboxPath = $outbox
  $env:Email__FrontendBaseUrl = "http://127.0.0.1:$port"
  $env:RateLimit__AuthPerMinute = if ($env:NIBRAS_TEST_AUTH_PER_MINUTE) { $env:NIBRAS_TEST_AUTH_PER_MINUTE } else { [string]$AuthPerMinute }
  if (-not $env:RateLimit__ActivationPerMinute) { $env:RateLimit__ActivationPerMinute = '1000' }
  if (-not $env:RateLimit__VerificationPerMinute) { $env:RateLimit__VerificationPerMinute = '1000' }
  if (-not $env:RateLimit__VerificationResendPerMinute) { $env:RateLimit__VerificationResendPerMinute = '1000' }
  Remove-Item Env:Seed__DemoData -ErrorAction SilentlyContinue
  # Belt and braces: the demo/business seeder only runs when this key is exactly "true", and the
  # harness pins it to "false", so no demo user, land, document or history can ever be created.
  $env:Seed__DemoData = 'false'
  # The disposable SuperAdmin identity of this run: generated once and reused by every start, so
  # the seeding is idempotent across the starts of one approved run. The harness owns the value,
  # so a stale value in the caller's shell cannot rotate it silently.
  $runCredentials = Get-TestRunCredentials
  $env:NIBRAS_TEST_SUPERADMIN_EMAIL = $runCredentials.Email
  $env:NIBRAS_TEST_SUPERADMIN_PASSWORD = $runCredentials.Password
  $env:Seed__SuperAdminEmail = $runCredentials.Email
  $env:Seed__SuperAdminPassword = $runCredentials.Password

  $repoRoot = $PSScriptRoot
  while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) { $repoRoot = Split-Path $repoRoot -Parent }
  if (-not $repoRoot) { throw 'NibrasWeb.csproj not found above the script directory' }
  $project = Join-Path $repoRoot 'NibrasWeb.csproj'

  # A FRESH build from the current (approved) commit. An existing DLL is never trusted just
  # because it is there: it could be stale, or built from another commit, and it is the binary
  # that would talk to the database.
  Write-Output "  building the application (Debug) from $project"
  $build = & dotnet build $project --configuration Debug --nologo 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "the application could not be built, so the harness refuses to start an instance:`n$build"
  }

  $dll = Join-Path $repoRoot 'bin\Debug\net10.0\NibrasWeb.dll'
  if (-not (Test-Path -LiteralPath $dll)) {
    throw "the application build '$dll' is missing after the build."
  }

  $out = Join-Path ([IO.Path]::GetTempPath()) "nibras-f1-api-$port.out.log"
  $err = Join-Path ([IO.Path]::GetTempPath()) "nibras-f1-api-$port.err.log"
  $process = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $repoRoot -PassThru -NoNewWindow `
    -RedirectStandardOutput $out -RedirectStandardError $err

  # The instance is stopped when the shell ends, even when the start failed half way: the
  # harness only ever stops the process it started. The handler is registered immediately,
  # before any later step that could fail.
  $startedPid = $process.Id
  $portForExit = [int]$port
  $identityFile = $script:TestApiIdentityFile
  $outboxForExit = $outbox
  Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action {
    # The exit handler is a last resort. It re-proves the same identity as Stop-TestApi:
    # a reused PID is never killed unless it still owns the recorded port. If ownership
    # cannot be proved, the identity file is left alone so the runner can fail closed.
    $exitProcess = Get-Process -Id $startedPid -ErrorAction SilentlyContinue
    $ownsPort = $false
    if ($exitProcess) {
      try {
        $exitListeners = @(Get-NetTCPConnection -LocalPort $portForExit -State Listen -ErrorAction Stop)
        $exitOwners = @($exitListeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
        $ownsPort = ($exitOwners.Count -eq 1 -and $exitOwners[0] -eq $startedPid)
      }
      catch { $ownsPort = $false }
      if ($ownsPort) { Stop-Process -Id $startedPid -Force -ErrorAction SilentlyContinue }
    }
    if ((-not $exitProcess) -or $ownsPort) {
      Remove-Item -LiteralPath $identityFile -Force -ErrorAction SilentlyContinue
    }
    # Same safety as Stop-TestApi: remove only the per-run outbox, and only while it holds
    # nothing but mail files. A foreign file means the directory is left alone.
    if ((-not $exitProcess -or $ownsPort) -and $outboxForExit -and (Test-Path -LiteralPath $outboxForExit)) {
      $foreign = @(Get-ChildItem -LiteralPath $outboxForExit -File -ErrorAction SilentlyContinue |
                      Where-Object { $_.Name -notlike 'mail-*.json' })
      if ($foreign.Count -eq 0) {
        Remove-Item -LiteralPath $outboxForExit -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  } | Out-Null

  # Readiness is proved by the anonymous registration endpoint itself, never by a protected
  # path. An empty JSON body fails the [ApiController] model validation inside the MVC
  # pipeline, before RegisterAsync runs - so the probe cannot create an account or touch the
  # database. A protected path answers 401 both while the endpoint routing is still being
  # built and afterwards, so it cannot tell the two states apart: 401, 404, 429, 5xx and
  # connection errors never count as ready. Only the exact validation problem document does.
  Add-Type -AssemblyName System.Net.Http | Out-Null
  $probeClient = New-Object System.Net.Http.HttpClient
  $probeClient.Timeout = [TimeSpan]::FromSeconds(5)
  $deadline = (Get-Date).AddSeconds(60)
  $ready = $false
  $lastProbe = 'no answer yet'
  try {
    while ((Get-Date) -lt $deadline) {
      Start-Sleep -Milliseconds 500
      if ($process.HasExited) {
        throw "the API process exited immediately (code $($process.ExitCode)); see '$out' and '$err'."
      }
      $probeRequest = New-Object System.Net.Http.HttpRequestMessage
      try {
        $probeRequest.Method = [System.Net.Http.HttpMethod]::Post
        $probeRequest.RequestUri = [Uri]("http://127.0.0.1:$port/api/auth/register")
        $probeRequest.Content = New-Object System.Net.Http.StringContent('{}', [System.Text.Encoding]::UTF8, 'application/json')
        $probeResponse = $probeClient.SendAsync($probeRequest).Result
        try {
          $probeBody = $probeResponse.Content.ReadAsStringAsync().Result
          $lastProbe = ("status {0}" -f [int]$probeResponse.StatusCode)
          if ([int]$probeResponse.StatusCode -eq 400) {
            $problem = $null
            try { $problem = $probeBody | ConvertFrom-Json } catch { }
            if ($problem -and $problem.status -eq 400 -and
                $problem.title -eq 'One or more validation errors occurred.' -and
                $problem.errors.Email -and $problem.errors.Phone -and
                $problem.errors.FullName -and $problem.errors.Password) {
              $ready = $true
              break
            }
            $lastProbe = 'a 400 that is not the registration validation problem document'
          }
        } finally { $probeResponse.Dispose() }
      } catch {
        $lastProbe = ("no HTTP answer ({0})" -f $_.Exception.GetBaseException().Message)
      } finally { $probeRequest.Dispose() }
    }
  } finally { $probeClient.Dispose() }
  if (-not $ready) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw ("the API did not prove a live anonymous pipeline on 127.0.0.1:{0} within the start period (last probe: {1}); see '{2}' and '{3}'." -f $port, $lastProbe, $out, $err)
  }

  # The identity of what was launched: the process the harness started must be the one that
  # owns the port. A foreign listener would fail this check.
  $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
  $owner = @($listeners | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique)
  if ($owner.Count -ne 1 -or $owner[0] -ne $process.Id) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    throw ("the port {0} is owned by {1} and not by the process {2} the harness started; the run stops." -f $port, ($owner -join ','), $process.Id)
  }

  $identity = [pscustomobject]@{
    Pid       = $process.Id
    Port      = $port
    BaseUrl   = "http://127.0.0.1:$port"
    Database  = $target.Database
    Server    = $target.Server
    Outbox    = $outbox
    StartedAt = (Get-Date).ToUniversalTime().ToString('o')
  }
  $identity | ConvertTo-Json | Set-Content -LiteralPath $script:TestApiIdentityFile -Encoding UTF8

  return $identity
}

function Get-TestApiIdentity {
  <#
    .SYNOPSIS
      The identity of the harness-owned instance, read from the identity file.
  #>
  if (-not (Test-Path -LiteralPath $script:TestApiIdentityFile)) {
    throw "no API identity file ('$script:TestApiIdentityFile'): the harness-owned instance was not started. The suites refuse to talk to an instance they do not own."
  }
  $raw = Get-Content -LiteralPath $script:TestApiIdentityFile -Raw
  try { return ($raw | ConvertFrom-Json) } catch { throw "the API identity file is unreadable: $raw" }
}

function Assert-TestApiOwned {
  <#
    .SYNOPSIS
      Proves that the harness-owned instance is still the one that owns the dedicated port.
    .DESCRIPTION
      This is the pre-write safety gate: it needs no write of its own. A suite calls it
      BEFORE its first mutation, so a foreign or restarted instance is refused before
      anything is written.
  #>
  param([object]$Identity)

  if (-not $Identity) { $Identity = Get-TestApiIdentity }

  $process = Get-Process -Id $Identity.Pid -ErrorAction SilentlyContinue
  if (-not $process) {
    throw "the harness-owned API process $($Identity.Pid) is gone: the instance is not the one the harness started, so the run stops."
  }
  $listeners = @(Get-NetTCPConnection -LocalPort ([int]$Identity.Port) -State Listen -ErrorAction SilentlyContinue)
  $owner = @($listeners | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique)
  if ($owner.Count -ne 1 -or $owner[0] -ne $Identity.Pid) {
    throw ("the port {0} is owned by {1} and not by the harness process {2}: something else is listening, so the run stops." -f $Identity.Port, ($owner -join ','), $Identity.Pid)
  }
  return $Identity
}

function Stop-TestApi {
  <#
    .SYNOPSIS
      Stops the instance the harness started, and removes only what it created.
    .DESCRIPTION
      Only the recorded PID is stopped, and only while that PID still owns the recorded port:
      a stale or reused PID can never kill a foreign process. Only the per-run outbox
      directory is removed - and only when it still holds nothing but the mail files of this
      run. The protected run credentials are never touched.
  #>
  param([object]$Identity)

  if (-not $Identity) {
    if (-not (Test-Path -LiteralPath $script:TestApiIdentityFile)) { return }
    $Identity = Get-TestApiIdentity
  }

  $process = Get-Process -Id $Identity.Pid -ErrorAction SilentlyContinue
  if ($process) {
    $listeners = @(Get-NetTCPConnection -LocalPort ([int]$Identity.Port) -State Listen -ErrorAction SilentlyContinue)
    $owners = @($listeners | ForEach-Object { $_.OwningProcess } | Sort-Object -Unique)
    if ($owners.Count -ne 1 -or $owners[0] -ne $Identity.Pid) {
      throw ("refusing to stop the process {0}: it does not own the port {1} recorded in the identity file. A foreign process is never killed." -f $Identity.Pid, $Identity.Port)
    }
    Stop-Process -Id $Identity.Pid -Force -ErrorAction SilentlyContinue
  }

  if ($Identity.Outbox -and (Test-Path -LiteralPath $Identity.Outbox)) {
    $foreign = @(Get-ChildItem -LiteralPath $Identity.Outbox -File -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -notlike 'mail-*.json' })
    if ($foreign.Count -eq 0) {
      Remove-Item -LiteralPath $Identity.Outbox -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
  Remove-Item -LiteralPath $script:TestApiIdentityFile -Force -ErrorAction SilentlyContinue
}
