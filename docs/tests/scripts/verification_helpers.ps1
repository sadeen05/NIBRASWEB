#requires -Version 5.1
# Shared helpers for the email verification flows (F1).
#
# The helpers complete the REAL API flow: the activation and verification tokens are read
# from the development outbox - the private per-run directory of the FileOutbox delivery
# mode - and are redeemed through the public endpoints. There is NO bypass: nothing here
# writes to the database, and no account is verified or activated by a direct update.
#
# The tokens are credentials: they are returned to the caller, but they are never printed
# and never written to a log.
#
# Usage:
#   . (Join-Path $PSScriptRoot 'verification_helpers.ps1')
#   $baseline = New-OutboxBaseline                        # BEFORE the request
#   $r = Call 'POST' '/api/auth/register' $body
#   $token = Get-VerificationTokenFromOutbox -Email $mail -Baseline $baseline
#   $r = Complete-EmailVerification -Token $token         # -> 204
#
# Configuration (environment variables):
#   (the API base address comes from the harness identity, never from the environment)
#   NIBRAS_EMAIL_OUTBOX    the FileOutbox directory of the running instance (required for
#                          the helpers that read a token); it must be the same directory the
#                          instance writes to, and it must be outside any Git working tree.

Add-Type -AssemblyName System.Net.Http | Out-Null
$script:VerificationHttp = New-Object System.Net.Http.HttpClient
$script:VerificationHttp.Timeout = [TimeSpan]::FromSeconds(60)

# The repository root of the test scripts: the outbox must never sit inside it.
$script:TestRepoRoot = $null
$repoDir = $PSScriptRoot
while ($repoDir -and -not (Test-Path (Join-Path $repoDir 'NibrasWeb.csproj'))) { $repoDir = Split-Path $repoDir -Parent }
if ($repoDir) { $script:TestRepoRoot = (Resolve-Path -LiteralPath $repoDir).Path }

function Assert-TestInstanceIdentity {
  <#
    .SYNOPSIS
      OPTIONAL additional evidence: the running instance writes into the disposable database.
    .DESCRIPTION
      This check registers a disposable probe account through the public API and reads the row
      back from the disposable database. It is NOT the primary protection - the pre-write gate
      is Initialize-TestApi, which needs no write. This probe is only useful as extra evidence
      that the effective configuration and the database really are the disposable ones, and it
      may only be called AFTER the gate has passed.

      No new endpoint is used: the check drives the public registration, which is exactly what
      the suites are about. The probe row is created by the API in the disposable database; the
      check itself only reads.
  #>
  param([string]$ProbeEmail)

  # The gate first: a probe must never be the thing that decides whether the run may write.
  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  $identity = Assert-TestApiOwned
  $target = Get-TestTarget

  $email = if ($ProbeEmail) { $ProbeEmail } else { "identity-probe-$([guid]::NewGuid().ToString('N').Substring(0,12))@example.test" }
  $phone = '9' + ('{0:D9}' -f (Get-Random -Minimum 100000000 -Maximum 999999999))
  $password = 'T5t!N1' + [guid]::NewGuid().ToString('N').Substring(0,12)
  $body = @{ fullName = 'Test identity probe'; email = $email; phone = $phone; password = $password; roleId = 1 } | ConvertTo-Json -Compress

  $response = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' -Json $body
  if ($response.Status -ne 200 -and $response.Status -ne 201) {
    throw ("the identity probe could not register against the running API (status {0}). " +
      "The instance must be running against the disposable database." -f $response.Status)
  }

  $out = & sqlcmd -S $target.Server -d $target.Database -E -b -W -h -1 -Q ("SET NOCOUNT ON; SELECT COUNT(*) FROM USERS WHERE EMAIL = N'" + $email + "';") 2>&1
  if ($LASTEXITCODE -ne 0) { throw "the identity probe could not read the disposable database: $out" }
  $rows = [int]((@($out | Where-Object { $_ }) -join '').Trim())
  if ($rows -ne 1) {
    throw ("the running API does NOT write into the disposable database '{0}': the probe account " +
      "was not found there. The suite would write into an unknown target - the run stops." -f $target.Database)
  }

  return [pscustomobject]@{ Database = $target.Database; Server = $target.Server; ProbeEmail = $email; BaseUrl = $identity.BaseUrl }
}

function Get-VerificationBaseUrl {
  <#
    .SYNOPSIS
      The base URL of the harness-owned instance.
    .DESCRIPTION
      There is no fallback: without a harness identity there is no instance to talk to.
  #>
  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  $identity = Get-TestApiIdentity
  return $identity.BaseUrl
}

function Get-VerificationApiUri {
  <#
    .SYNOPSIS
      The absolute URI of an API path on the harness-owned instance.
    .DESCRIPTION
      The base URL is evaluated in its own statement and the path is normalized, so the path
      is concatenated and never passed as an argument to the command.

      This is deliberate: in PowerShell, "[Uri](Get-VerificationBaseUrl + $Path)" is a
      COMMAND invocation with the two arguments "+" and $Path - not a concatenation. It
      silently returns the bare base URL, so the request goes to the site root, matches no
      endpoint and is answered by the fallback authorization policy with 401.
  #>
  param([Parameter(Mandatory)][string]$Path)

  $baseUrl = [string](Get-VerificationBaseUrl)
  if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    throw 'the harness identity has no base URL; the suite refuses to build a request.'
  }
  $baseUrl = $baseUrl.Trim().TrimEnd('/')

  # Exactly one leading slash, whatever the caller passed.
  $relative = '/' + $Path.Trim().TrimStart('/')
  return [Uri]($baseUrl + $relative)
}

function Start-TestApiInstance {
  <#
    .SYNOPSIS
      Starts the harness-owned API instance the suites use.
    .DESCRIPTION
      The instance is owned by the harness: a dedicated port, an explicit disposable connection
      string, and a proof that the process the harness started is the one that owns the port.
      A suite never talks to an instance it did not start.
  #>
  param(
    [ValidateSet('FileOutbox', 'Disabled')]
    [string]$DeliveryMode = 'FileOutbox'
  )

  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  return Start-TestApi -DeliveryMode $DeliveryMode
}

function Initialize-TestApi {  <#
    .SYNOPSIS
      The pre-write safety gate: proves the instance is the harness-owned one.
    .DESCRIPTION
      A suite calls this BEFORE its first mutation. The check needs no write of its own: it
      verifies that the process recorded in the identity file is alive and owns the dedicated
      port, so the suite cannot write through a foreign or restarted instance. It returns the
      base URL the suite must use.
  #>
  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  $identity = Assert-TestApiOwned
  return $identity.BaseUrl
}

function Stop-TestApiInstance {
  <#
    .SYNOPSIS
      Stops the instance this suite started, from the suite's own scope.
    .DESCRIPTION
      A suite cannot call Stop-TestApi directly: the harness is dot-sourced inside the helper
      functions, so its functions never reach the suite's scope. This wrapper keeps the same
      ownership guarantee - only the recorded PID is stopped, and only while it still owns
      the recorded port - so a stale or reused PID can never kill a foreign process.
  #>
  param([object]$Identity)

  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  Stop-TestApi -Identity $Identity
}

function Get-VerificationApiIdentityFile {
  # Resolve the harness function in the same local scope in which it is loaded.
  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  return Get-TestApiIdentityFile
}

function Get-VerificationApiIdentity {
  # Resolve the harness identity object in the same local scope in which it is loaded, so a
  # caller can pass a real object to Complete-TestApiCleanup without a top-level scope error.
  . (Join-Path $PSScriptRoot 'test_harness.ps1')
  return Get-TestApiIdentity
}

function Complete-TestApiCleanup {
  <#
    .SYNOPSIS
      Stops the harness-owned API and independently verifies that cleanup is complete.
    .DESCRIPTION
      Stop-TestApiInstance already enforces PID/port ownership before it stops anything. This
      wrapper adds the missing proof: it waits for the recorded process, its listener and the
      identity file to disappear, and it reports every remaining condition instead of
      swallowing it. A cleanup problem is written as a WARNING (the runner treats that as NOT
      PASS) and then thrown, so a suite cannot return 0 after a failed cleanup.

      Only observation happens here. A foreign listener is never killed.
  #>
  param([object]$Identity)

  if ($null -eq $Identity) {
    throw 'API cleanup failed: no harness identity was recorded; cleanup cannot be verified.'
  }
  # Reject malformed identities before any shutdown or observation command is invoked.
  if ([int]$Identity.Pid -le 0 -or [int]$Identity.Port -lt 1 -or [int]$Identity.Port -gt 65535) {
    throw 'API cleanup failed: the harness identity has an invalid PID or port.'
  }

  $problems = New-Object System.Collections.Generic.List[string]
  try {
    Stop-TestApiInstance -Identity $Identity
  }
  catch {
    [void]$problems.Add(('Stop-TestApiInstance failed: {0}' -f $_.Exception.Message))
  }

  $port = 0
  try { $port = [int]$Identity.Port }
  catch { [void]$problems.Add('the harness identity has no valid port') }

  $identityPath = $null
  try {
    $identityPath = Get-VerificationApiIdentityFile
  }
  catch {
    [void]$problems.Add(('the API identity file path could not be resolved: {0}' -f $_.Exception.Message))
  }

  $deadline = [DateTime]::UtcNow.AddSeconds(5)
  $identityPresent = $true
  $listeners = @()
  $apiProcess = $null
  $portCheckError = $null
  $processCheckError = $null

  do {
    if ($identityPath) { $identityPresent = Test-Path -LiteralPath $identityPath }
    $listeners = @()
    if ($port -gt 0) {
      try {
        $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop)
      }
      catch {
        if ($_.Exception.Message -notmatch 'No matching|not found' -and
            $_.FullyQualifiedErrorId -notmatch 'NotFound|NoMatching') {
          $portCheckError = $_.Exception.Message
        }
      }
    }

    try {
      $apiProcess = Get-Process -Id ([int]$Identity.Pid) -ErrorAction Stop
    }
    catch {
      $apiProcess = $null
      if ($_.FullyQualifiedErrorId -notmatch '^NoProcessFoundForGivenId,') {
        $processCheckError = $_.Exception.Message
      }
    }
    $stillPresent = $identityPresent -or ($listeners.Count -gt 0) -or ($null -ne $apiProcess) -or ($null -ne $portCheckError) -or ($null -ne $processCheckError)
    if (-not $stillPresent) { break }
    if ($null -ne $portCheckError -or $null -ne $processCheckError) { break }
    if ([DateTime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Milliseconds 250
  } while ($true)

  if ($identityPresent) { [void]$problems.Add('the API identity file still exists after cleanup') }
  if ($listeners.Count -gt 0) {
    $owners = @($listeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
    [void]$problems.Add(("port {0} still has listener process(es) {1}" -f $port, ($owners -join ',')))
  }
  if ($null -ne $apiProcess) { [void]$problems.Add(("API process {0} is still alive" -f $Identity.Pid)) }
  if ($null -ne $portCheckError) { [void]$problems.Add(('port cleanup check failed: {0}' -f $portCheckError)) }

  if ($null -ne $processCheckError) { [void]$problems.Add(('process cleanup check failed: {0}' -f $processCheckError)) }

  if ($problems.Count -gt 0) {
    foreach ($problem in $problems) { Write-Output ('WARNING: cleanup: ' + $problem) }
    throw ('API cleanup failed: ' + ($problems -join '; '))
  }
}

function Get-VerificationOutboxPath {
  <#
    .SYNOPSIS
      The outbox directory of the running instance.
    .DESCRIPTION
      The path comes from NIBRAS_EMAIL_OUTBOX. The check FAILS CLOSED: the path must be
      absolute, it must exist, and it must be outside any Git working tree - and when the
      repository check cannot be completed at all, the helper refuses the path instead of
      assuming it is safe. The outbox holds live activation links and must never be committed.
  #>
  param([string]$OutboxPath)

  $path = if ($OutboxPath) { $OutboxPath } else { $env:NIBRAS_EMAIL_OUTBOX }
  if (-not $path) {
    throw 'NIBRAS_EMAIL_OUTBOX is not set: the helpers cannot read a token without the outbox directory.'
  }
  if (-not [System.IO.Path]::IsPathRooted($path)) {
    throw "the outbox path '$path' must be absolute."
  }
  if (-not (Test-Path -LiteralPath $path -PathType Container)) {
    throw "the outbox directory '$path' does not exist: the instance is probably not running in the FileOutbox mode."
  }

  # The repository root of the test scripts is the known repository. The check is
  # deterministic and needs no external command: a path inside it is refused, a path
  # outside is accepted, and a path that cannot be checked at all is refused as well.
  if (-not $script:TestRepoRoot) {
    throw "the repository root could not be resolved: the outbox path '$path' cannot be checked, so it is refused."
  }
  $resolved = (Resolve-Path -LiteralPath $path).Path
  if ($resolved.StartsWith($script:TestRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "the outbox directory '$resolved' is inside the repository '$($script:TestRepoRoot)': the live links would be committed."
  }

  return $resolved
}

function New-OutboxBaseline {
  <#
    .SYNOPSIS
      A snapshot of the outbox, taken BEFORE the request that produces a message.
    .DESCRIPTION
      The reader only accepts the messages that are NOT part of the baseline, so a message
      left over from an earlier run can never be redeemed instead of the fresh one.
  #>
  param([string]$OutboxPath)

  $root = Get-VerificationOutboxPath -OutboxPath $OutboxPath
  $names = New-Object System.Collections.Generic.HashSet[string]
  foreach ($file in Get-ChildItem -LiteralPath $root -Filter 'mail-*.json' -File -ErrorAction SilentlyContinue) {
    [void]$names.Add($file.Name)
  }
  return [pscustomobject]@{ Root = $root; Names = $names }
}

function Get-VerificationTokenFromOutbox {
  <#
    .SYNOPSIS
      Reads the newest verification or activation link for an address out of the outbox.
    .DESCRIPTION
      The outbox holds one JSON file per message. The file name carries a timestamp and a
      random id, never an address. Only the messages that are NOT part of the baseline are
      accepted, and only the ones whose recipient matches exactly; the message must also
      match the purpose when a subject filter is given. A file that is still being written is
      skipped, and the poll is bounded.
    .OUTPUTS
      The plaintext token. It is a credential: never print it.
  #>
  param(
    [Parameter(Mandatory)][string]$Email,
    [string]$OutboxPath,
    [string]$SubjectLike,
    [object]$Baseline,
    [int]$TimeoutSeconds = 10
  )

  $root = if ($Baseline -and $Baseline.Root) { $Baseline.Root } else { Get-VerificationOutboxPath -OutboxPath $OutboxPath }
  $known = if ($Baseline -and $Baseline.Names) { $Baseline.Names } else { $null }
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  $found = $null

  while ((Get-Date) -lt $deadline) {
    $files = @(Get-ChildItem -LiteralPath $root -Filter 'mail-*.json' -File -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending)
    foreach ($file in $files) {
      # A message that is already part of the baseline is stale and is never used.
      if ($known -and $known.Contains($file.Name)) { continue }
      try {
        $message = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
      } catch {
        # A file that is still being written (or was truncated) is skipped and retried.
        continue
      }
      if (-not $message) { continue }
      if (-not $message.to -or ($message.to -ine $Email)) { continue }
      if ($SubjectLike -and (-not $message.subject -or $message.subject -notlike $SubjectLike)) { continue }
      $match = [regex]::Match([string]$message.body, 'token=([^&\s"''<>]+)')
      if (-not $match.Success) { continue }
      $found = [Uri]::UnescapeDataString($match.Groups[1].Value)
      break
    }
    if ($found) { break }
    Start-Sleep -Milliseconds 250
  }

  if (-not $found) {
    throw ("no verification message for '$Email' was found in the outbox '$root'. " +
      "Either the instance is not running in the FileOutbox mode (Email__DeliveryMode=FileOutbox), " +
      "or the delivery failed - the registration answer reports which of the two it was.")
  }
  return $found
}

function Invoke-VerificationApi {
  <#
    .SYNOPSIS
      One HTTP request to the harness-owned instance.
    .DESCRIPTION
      The optional BearerToken is an IN-MEMORY credential: it is attached to this request's
      Authorization header only. It is never printed, logged, serialised, persisted or passed
      to a child process. The returned object carries the status and the response body only,
      and callers must not log the body of an authenticated answer either (a sign-in body
      carries the token). Anonymous requests pass no token, so their behaviour is unchanged.
  #>
  param(
    [Parameter(Mandatory)][string]$Method,
    [Parameter(Mandatory)][string]$Path,
    [string]$Json,
    [string]$BearerToken
  )

  $req = New-Object System.Net.Http.HttpRequestMessage
  $req.Method = New-Object System.Net.Http.HttpMethod($Method)
  $req.RequestUri = (Get-VerificationApiUri -Path $Path)
  if ($Json) { $req.Content = New-Object System.Net.Http.StringContent($Json, [System.Text.Encoding]::UTF8, 'application/json') }
  if ($BearerToken) {
    $req.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $BearerToken)
  }
  try {
    $resp = $script:VerificationHttp.SendAsync($req).Result
    $body = $resp.Content.ReadAsStringAsync().Result
    return [pscustomobject]@{ Status = [int]$resp.StatusCode; Body = $body }
  } finally { $req.Dispose() }
}

function Invoke-ConcurrentApiRequests {
  <#
    .SYNOPSIS
      Sends two or more HTTP requests that genuinely overlap, in this process.
    .DESCRIPTION
      The requests are started on one HttpClient before any answer is awaited, so the calls
      overlap on the server without a child process, without Start-Job and without serialising
      a session JWT. The token is used only in the Authorization header of its own request;
      nothing is printed, logged or written to disk.

      The result is returned in request order; each item carries the status and the body.
      Transport failures throw (a race that never reached the server proves nothing); HTTP
      error statuses are returned normally, exactly like the suites' Call helpers.

      StartAtUnixMs optionally holds every request until one shared instant, so a race does
      not depend on how fast the caller built the list.
  #>
  param(
    [Parameter(Mandatory)][object[]]$Requests,
    [long]$StartAtUnixMs = 0,
    [int]$TimeoutSeconds = 90
  )

  if ($Requests.Count -lt 2) {
    throw 'a concurrency check needs at least two requests; a single request is not a race.'
  }

  # .NET Framework defaults to two connections per endpoint. A three-way race would queue
  # the third request behind the first two, so raise the limit for this test process only.
  $limit = [Math]::Max([int][System.Net.ServicePointManager]::DefaultConnectionLimit, $Requests.Count)
  [System.Net.ServicePointManager]::DefaultConnectionLimit = $limit

  $client = New-Object System.Net.Http.HttpClient
  $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)
  $messages = New-Object System.Collections.ArrayList
  $tasks = New-Object System.Collections.ArrayList
  try {
    foreach ($spec in $Requests) {
      $method = [string]$spec.Method
      $path = [string]$spec.Path
      if ([string]::IsNullOrWhiteSpace($method) -or [string]::IsNullOrWhiteSpace($path)) {
        throw 'every concurrent request needs a method and a path.'
      }
      $message = New-Object System.Net.Http.HttpRequestMessage
      $message.Method = New-Object System.Net.Http.HttpMethod($method)
      $message.RequestUri = (Get-VerificationApiUri -Path $path)
      if ($null -ne $spec.FileBytes) {
        $multipart = New-Object System.Net.Http.MultipartFormDataContent
        $message.Content = $multipart
        $typeField = New-Object System.Net.Http.StringContent([string]$spec.DocumentTypeId)
        [void]$multipart.Add($typeField, 'documentTypeId')
        $fileContent = New-Object System.Net.Http.ByteArrayContent -ArgumentList (,[byte[]]$spec.FileBytes)
        $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/pdf')
        [void]$multipart.Add($fileContent, 'file', [string]$spec.FileName)
      }
      elseif ($spec.Json) {
        $message.Content = New-Object System.Net.Http.StringContent([string]$spec.Json, [System.Text.Encoding]::UTF8, 'application/json')
      }
      if ($spec.BearerToken) {
        $message.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', [string]$spec.BearerToken)
      }
      [void]$messages.Add($message)
    }

    if ($StartAtUnixMs -gt 0) {
      while ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() -lt $StartAtUnixMs) {
        Start-Sleep -Milliseconds 5
      }
    }

    # Start every send BEFORE awaiting the first answer: this is the overlap.
    foreach ($message in $messages) {
      [void]$tasks.Add($client.SendAsync($message))
    }
    [System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]$tasks.ToArray())

    $results = @()
    for ($i = 0; $i -lt $messages.Count; $i++) {
      $response = $tasks[$i].Result
      try {
        $body = $response.Content.ReadAsStringAsync().Result
        $results += [pscustomobject]@{ Status = [int]$response.StatusCode; Body = $body }
      } finally { $response.Dispose() }
    }
    return ,$results
  } finally {
    foreach ($message in $messages) { $message.Dispose() }
    $client.Dispose()
  }
}

function Complete-EmailVerification {
  <#
    .SYNOPSIS
      Confirms the address of a Landlord or Investor account with the real endpoint.
    .DESCRIPTION
      POST /api/auth/verify-email with the token of the verification link. The answer is 204
      when the address was confirmed, 400 for an unusable token, 409 for a conflict and 429
      when the rate limit answered.
  #>
  param([Parameter(Mandatory)][string]$Token)

  $payload = @{ token = $Token } | ConvertTo-Json -Compress
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/verify-email' -Json $payload
}

function Complete-AdminActivation {
  <#
    .SYNOPSIS
      Activates a new administrator account with the real endpoint and a chosen password.
    .DESCRIPTION
      POST /api/auth/activate-admin with the token of the invitation and the password the
      administrator chooses. The password is never the one the creating administrator sent -
      the super administrator never learns it.
  #>
  param(
    [Parameter(Mandatory)][string]$Token,
    [Parameter(Mandatory)][string]$Password
  )

  $payload = @{ token = $Token; password = $Password } | ConvertTo-Json -Compress
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/activate-admin' -Json $payload
}

function Request-VerificationResend {
  <#
    .SYNOPSIS
      Asks for a new verification mail for an address.
    .DESCRIPTION
      POST /api/auth/resend-verification. The answer is the same generic 202 for every
      address, so the helper reports the status only.
  #>
  param([Parameter(Mandatory)][string]$Email)

  $payload = @{ email = $Email } | ConvertTo-Json -Compress
  return Invoke-VerificationApi -Method 'POST' -Path '/api/auth/resend-verification' -Json $payload
}

function Confirm-VerifiedAccount {
  <#
    .SYNOPSIS
      Confirms the address of an account that was registered before.
    .DESCRIPTION
      Reads the verification link of the address out of the outbox and redeems it through
      the real endpoint. The answer is the response of the endpoint: 204 when the address was
      confirmed, 400 for an unusable token, 409 for a conflict and 429 for the rate limit.
  #>
  param(
    [Parameter(Mandatory)][string]$Email,
    [string]$OutboxPath,
    [object]$Baseline
  )

  $token = Get-VerificationTokenFromOutbox -Email $Email -OutboxPath $OutboxPath -Baseline $Baseline
  return Complete-EmailVerification -Token $token
}

function Confirm-RegisteredAccount {
  <#
    .SYNOPSIS
      Registers an account and confirms its address before the suite signs in with it.
    .DESCRIPTION
      The registration and the confirmation are the real API calls; the account is only
      usable after the confirmation, so a suite that logs in right away has to confirm it
      first. The function returns the registration answer.
  #>
  param(
    [Parameter(Mandatory)][string]$RegistrationJson,
    [Parameter(Mandatory)][string]$Email,
    [string]$OutboxPath
  )

  $baseline = New-OutboxBaseline -OutboxPath $OutboxPath
  $registration = Invoke-VerificationApi -Method 'POST' -Path '/api/auth/register' -Json $RegistrationJson
  if ($registration.Status -ne 200 -and $registration.Status -ne 201) {
    return $registration
  }

  $confirmation = Confirm-VerifiedAccount -Email $Email -OutboxPath $OutboxPath -Baseline $baseline
  if ($confirmation.Status -ne 204) {
    throw "the verification of '$Email' failed with status $($confirmation.Status)"
  }
  return $registration
}

function Activate-AdminAccount {
  <#
    .SYNOPSIS
      Activates a new administrator account with a password of the suite's choosing.
    .DESCRIPTION
      The invitation of an administrator account is read out of the outbox and redeemed
      through POST /api/auth/activate-admin. The account can then sign in with that
      password.
  #>
  param(
    [Parameter(Mandatory)][string]$Email,
    [Parameter(Mandatory)][string]$Password,
    [string]$OutboxPath,
    [object]$Baseline
  )

  $token = Get-VerificationTokenFromOutbox -Email $Email -OutboxPath $OutboxPath -Baseline $Baseline -SubjectLike '*activation*'
  $activation = Complete-AdminActivation -Token $token -Password $Password
  if ($activation.Status -ne 204) {
    throw "the activation of '$Email' failed with status $($activation.Status)"
  }
  return $activation
}
