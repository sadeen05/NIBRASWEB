#requires -Version 5.1
<#
.SYNOPSIS
    Bounded, fail-closed runner for the remaining NIBRAS F1 regression suites.

.DESCRIPTION
    This runner treats the child exit code as evidence, never as the whole result. It starts
    one suite per child PowerShell process, waits with a bounded poll (no Start-Process -Wait),
    parses the suite's own summary and markers, and independently verifies API cleanup.

    The runner never starts the API itself. The suite starts its harness-owned instance. The
    runner only refuses to continue when the approved database identity, port 5199, the
    harness identity file, or a runner-owned process is not in the expected state.

    Static/self-test use:
        powershell.exe -NoProfile -ExecutionPolicy Bypass `
            -File docs\tests\scripts\run_regression.ps1 -SelfTest

    A real suite run is deliberately separate and needs explicit authorization. The runner
    does not touch NEBRAS, NEBRASDB or NEBRASDB_Step1, and it never executes
    s4_preflight.ps1.
#>
[CmdletBinding()]
param(
    [string]$ManifestPath = '',
    [string[]]$SuiteNames = @(),
    [int]$TimeoutSeconds = 0,
    [switch]$SelfTest,
    [switch]$ValidateOnly,
    [switch]$StopOwnedChildOnTimeout
)

$ErrorActionPreference = 'Stop'
$script:ScriptRoot = $PSScriptRoot
$script:IdentityFile = Join-Path ([IO.Path]::GetTempPath()) 'nibras-f1-api-identity.json'
$script:ApiPort = 5199
$script:ExpectedSuites = @(
    's4_tests',
    's4_admin',
    's4_regression',
    's4_review',
    's4_review2',
    's4_checkpoint_a',
    's5_offers',
    's6_contracts',
    's7_reservations',
    'p2_change_requests',
    'parallel-approval',
    's4_c4_acceptance'
)

# ---------------------------------------------------------------------------------------------
# Safe text and private evidence
# ---------------------------------------------------------------------------------------------

function Protect-RunnerText {
    <#
      Redacts credentials that must never be printed or retained in runner evidence. The
      suites do not intentionally print tokens, but a failure response can still contain a
      verification link or a sensitive header.
    #>
    param([AllowEmptyString()][string]$Text)

    if ($null -eq $Text) { return '' }
    $safe = [string]$Text
    $safe = [regex]::Replace($safe, '(?im)(^[ \t]*(?:Authorization|Proxy-Authorization|Set-Cookie|Cookie|X-Api-Key|X-Auth-Token|Api-Key)[ \t]*:[ \t]*).*$', '$1<redacted>')
    $safe = [regex]::Replace($safe, '(?i)\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '<jwt-redacted>')
    $safe = [regex]::Replace($safe, '(?i)\bBearer[ \t]+[A-Za-z0-9._~+/-]+=*', 'Bearer <redacted>')
    $safe = [regex]::Replace($safe, '(?i)\b([A-Za-z0-9_]*(?:token|password|pwd|secret)[A-Za-z0-9_]*)[ \t]*[=:][ \t]*[^&\s"'']+', '$1=<redacted>')
    $safe = [regex]::Replace($safe, '(?i)https?://[^\s"'']*(?:verify|confirm|activate)[^\s"'']*', '<verification-link-redacted>')
    return $safe
}

function New-RunnerPrivateDirectory {
    <#
      Creates the per-run evidence directory outside the repository and removes inherited
      access. If the restriction cannot be applied, the caller fails closed.
    #>
    param([string]$Prefix = 'nibras-regression')

    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $path = Join-Path ([IO.Path]::GetTempPath()) ("{0}-{1}-{2}" -f $Prefix, $stamp, [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    try {
        $acl = Get-Acl -LiteralPath $path
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
        $me = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $ownerRule = New-Object -TypeName 'System.Security.AccessControl.FileSystemAccessRule' `
            -ArgumentList @($me, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($ownerRule)
        Set-Acl -LiteralPath $path -AclObject $acl
    }
    catch {
        throw ("the private evidence directory '{0}' could not be restricted to the current user: {1}" -f $path, $_.Exception.Message)
    }
    return (Resolve-Path -LiteralPath $path).Path
}

function Read-RunnerTextFile {
    param([string]$Path)

    # Read with a shared handle. The suite may leave the log handle open until its own cleanup
    # finishes; an exclusive File.ReadAllText would fail even though the bytes are available.
    # The retry loop is bounded so an unreadable file still becomes INDETERMINATE/NOT PASS.
    $last = $null
    for ($i = 0; $i -lt 12; $i++) {
        $stream = $null
        $reader = $null
        try {
            $stream = New-Object -TypeName 'System.IO.FileStream' -ArgumentList @(
                $Path,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
            )
            $reader = New-Object -TypeName 'System.IO.StreamReader' -ArgumentList @(
                $stream,
                [System.Text.Encoding]::UTF8,
                $true
            )
            return $reader.ReadToEnd()
        }
        catch {
            $last = $_
            Start-Sleep -Milliseconds 250
        }
        finally {
            if ($null -ne $reader) { $reader.Dispose() }
            elseif ($null -ne $stream) { $stream.Dispose() }
        }
    }
    throw $last
}

function Write-RunnerSanitizedLog {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $true }
    try {
        $raw = Read-RunnerTextFile -Path $Path
        $safe = Protect-RunnerText -Text $raw
        Set-Content -LiteralPath $Path -Value $safe -Encoding UTF8
        return $true
    }
    catch {
        # A log that cannot be sanitized is removed rather than retained with possible secrets.
        Remove-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        return $false
    }
}

# ---------------------------------------------------------------------------------------------
# Process/cleanup observation
# ---------------------------------------------------------------------------------------------

function Get-RunnerPortListeners {
    param([int]$Port)

    try {
        $listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction Stop)
        return @($listeners | Where-Object { $null -ne $_ })
    }
    catch {
        if ($_.Exception.Message -match 'No matching|not found' -or $_.FullyQualifiedErrorId -match 'NotFound|NoMatching') {
            return @()
        }
        throw
    }
}

function Get-RunnerApiIdentity {
    if (-not (Test-Path -LiteralPath $script:IdentityFile)) { return $null }
    try {
        $identity = Get-Content -LiteralPath $script:IdentityFile -Raw | ConvertFrom-Json
        if (-not $identity -or [int]$identity.Pid -le 0 -or [int]$identity.Port -le 0) {
            throw 'the identity file is incomplete'
        }
        return $identity
    }
    catch {
        throw 'the API identity file exists but is unreadable or incomplete'
    }
}

function Get-RunnerCleanupState {
    <#
      Reads the port/identity/child state only. It never stops a process. A foreign listener
      is reported and left alone.
    #>
    param([int]$SuiteProcessId = 0)

    $state = [ordered]@{
        Ok                  = $false
        CheckSucceeded      = $false
        State               = 'unknown'
        Reason              = ''
        Port                = $script:ApiPort
        ListenerPids        = @()
        IdentityPresent     = $false
        IdentityPid         = 0
        IdentityPort        = 0
        ChildPid            = $SuiteProcessId
        ChildAlive          = $false
    }

    try {
        $listeners = @(Get-RunnerPortListeners -Port $script:ApiPort)
        $state.CheckSucceeded = $true
        $owners = @($listeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique)
        $state.ListenerPids = $owners

        $identity = $null
        if (Test-Path -LiteralPath $script:IdentityFile) {
            $state.IdentityPresent = $true
            $identity = Get-RunnerApiIdentity
            $state.IdentityPid = [int]$identity.Pid
            $state.IdentityPort = [int]$identity.Port
        }

        if ($SuiteProcessId -gt 0) {
            $child = Get-Process -Id $SuiteProcessId -ErrorAction SilentlyContinue
            $state.ChildAlive = [bool]$child
        }
        if ($state.ChildAlive) {
            $state.State = 'child-alive'
            $state.Reason = "the runner-owned suite process $SuiteProcessId is still alive"
            return [pscustomobject]$state
        }

        if ($listeners.Count -eq 0) {
            if ($identity) {
                $state.State = 'stale-identity'
                $state.Reason = 'the API identity file exists but port 5199 has no listener'
            }
            else {
                $state.State = 'free'
                $state.Ok = $true
            }
            return [pscustomobject]$state
        }

        if ($owners.Count -ne 1) {
            $state.State = 'ambiguous-listener'
            $state.Reason = ("port 5199 has {0} listener owners: {1}" -f $owners.Count, ($owners -join ','))
            return [pscustomobject]$state
        }
        if (-not $identity) {
            $state.State = 'unowned-listener'
            $state.Reason = ("port 5199 is owned by process {0}, but no API identity file exists" -f $owners[0])
            return [pscustomobject]$state
        }
        if ($state.IdentityPort -ne $script:ApiPort) {
            $state.State = 'identity-port-mismatch'
            $state.Reason = ("the identity file records port {0}, not {1}" -f $state.IdentityPort, $script:ApiPort)
            return [pscustomobject]$state
        }
        if ($state.IdentityPid -ne $owners[0]) {
            $state.State = 'foreign-listener'
            $state.Reason = ("port 5199 is owned by process {0}, not the harness identity process {1}" -f $owners[0], $state.IdentityPid)
            return [pscustomobject]$state
        }

        $state.State = 'owned-api-running'
        $state.Reason = ("the harness-owned API process {0} is still listening on port 5199" -f $state.IdentityPid)
        return [pscustomobject]$state
    }
    catch {
        $state.State = 'unknown'
        $state.CheckSucceeded = $false
        $state.Reason = Protect-RunnerText -Text $_.Exception.Message
        return [pscustomobject]$state
    }
}

function Wait-RunnerCleanupSettled {
    <#
      Gives the suite's explicit finally block a bounded opportunity to stop its API and remove
      the identity file before final evidence ingestion. This only observes state; it never stops
      or kills a process. A timeout returns the last observation so the caller reports uncertainty.
    #>
    param(
        [int]$SuiteProcessId = 0,
        [int]$TimeoutSeconds = 5
    )

    $deadline = [DateTime]::UtcNow.AddSeconds([Math]::Max(1, $TimeoutSeconds))
    $last = $null
    do {
        try {
            $last = Get-RunnerCleanupState -SuiteProcessId $SuiteProcessId
        }
        catch {
            $last = [pscustomobject]@{
                Ok             = $false
                CheckSucceeded = $false
                State          = 'unknown'
                Reason         = (Protect-RunnerText -Text $_.Exception.Message)
                Port           = $script:ApiPort
                ListenerPids   = @()
                IdentityPresent = $false
                IdentityPid    = 0
                IdentityPort   = 0
                ChildPid       = $SuiteProcessId
                ChildAlive     = $false
            }
        }
        if ([bool]$last.Ok) { return $last }
        if ([DateTime]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds 250
    } while ($true)
    return $last
}

# ---------------------------------------------------------------------------------------------
# Result classification
# ---------------------------------------------------------------------------------------------

function Get-RunnerNamedInt {
    param($Match, [string]$Name)

    if ($null -eq $Match) { return 0 }
    $group = $Match.Groups[$Name]
    if ($null -eq $group -or -not $group.Success -or [string]::IsNullOrWhiteSpace($group.Value)) { return 0 }
    return [int]$group.Value
}

function Get-RunnerClassification {
    <#
      Classifies one observation. The raw exit code remains in Observation.RawExitCode; it is
      never overwritten by the parsed result. Any genuine failure marker or cleanup problem is
      NOT PASS, even when the child returned 0.
    #>
    param(
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)]$Observation
    )

    $text = if ($null -eq $Observation.Text) { '' } else { [string]$Observation.Text }
    $textReadable = $true
    if ($Observation.PSObject.Properties['TextReadable']) { $textReadable = [bool]$Observation.TextReadable }
    $sanitizationOk = $true
    if ($Observation.PSObject.Properties['SanitizationOk']) { $sanitizationOk = [bool]$Observation.SanitizationOk }
    $timedOut = $false
    if ($Observation.PSObject.Properties['TimedOut']) { $timedOut = [bool]$Observation.TimedOut }
    $rawExit = $null
    if ($Observation.PSObject.Properties['RawExitCode']) { $rawExit = $Observation.RawExitCode }

    $hardReasons = New-Object System.Collections.Generic.List[string]
    $indeterminateReasons = New-Object System.Collections.Generic.List[string]

    if ($timedOut) { [void]$hardReasons.Add('timeout') }
    elseif ($null -eq $rawExit) { [void]$hardReasons.Add('child-exit-unknown') }
    elseif ([int]$rawExit -ne 0) { [void]$hardReasons.Add(("nonzero-child-exit={0}" -f [int]$rawExit)) }

    # Unreadable/unsanitized evidence is a hard failure: an unknown result must never be
    # presented as a successful or zero-count run.
    if (-not $textReadable) { [void]$hardReasons.Add('suite-output-unreadable') }
    if (-not $sanitizationOk) { [void]$hardReasons.Add('log-sanitization-failed') }

    # Counts remain unknown until a complete summary is parsed. In particular, a missing
    # summary must not be rendered as PASS=0 FAIL=0 BLOCKED=0.
    $summaryPass = $null
    $summaryFail = $null
    $summaryBlocked = $null
    $summaryFound = $false
    $summaryAmbiguous = $false
    $successMarker = $false
    $failureMarker = $false

    if (-not [string]::IsNullOrWhiteSpace([string]$Entry.SummaryPattern)) {
        $summaryMatches = @([regex]::Matches($text, [string]$Entry.SummaryPattern))
        if ($summaryMatches.Count -eq 0) {
            [void]$hardReasons.Add('required-summary-missing')
        }
        elseif ($summaryMatches.Count -gt 1) {
            $summaryAmbiguous = $true
            [void]$indeterminateReasons.Add('summary-ambiguous')
        }
        else {
            $summaryFound = $true
            $summaryPass = Get-RunnerNamedInt -Match $summaryMatches[0] -Name 'pass'
            $summaryFail = Get-RunnerNamedInt -Match $summaryMatches[0] -Name 'fail'
            $summaryBlocked = Get-RunnerNamedInt -Match $summaryMatches[0] -Name 'blocked'
        }
    }
    elseif ([bool]$Entry.RequireSummary -and [string]::IsNullOrWhiteSpace([string]$Entry.SuccessPattern) -and -not [bool]$Entry.EvidenceOnly) {
        [void]$hardReasons.Add('required-summary-missing')
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$Entry.SuccessPattern)) {
        $successMarker = [regex]::IsMatch($text, [string]$Entry.SuccessPattern)
        $failureMarker = $false
        if (-not [string]::IsNullOrWhiteSpace([string]$Entry.FailurePattern)) {
            $failureMarker = [regex]::IsMatch($text, [string]$Entry.FailurePattern)
        }
        if (-not $successMarker -and -not $failureMarker) {
            [void]$indeterminateReasons.Add('required-success-marker-missing')
        }
    }

    $sqlFailures = 0
    foreach ($m in [regex]::Matches($text, '(?im)REQUIRED-SQL-FAILURES[ \t]*=[ \t]*(?<n>\d+)')) {
        $n = Get-RunnerNamedInt -Match $m -Name 'n'
        if ($n -gt $sqlFailures) { $sqlFailures = $n }
    }
    $skipCount = if ($textReadable) { 0 } else { $null }
    foreach ($m in [regex]::Matches($text, '(?im)\bSKIP[ \t]*=[ \t]*(?<n>\d+)')) {
        $n = Get-RunnerNamedInt -Match $m -Name 'n'
        if ($null -ne $skipCount -and $n -gt $skipCount) { $skipCount = $n }
    }

    $resultMatches = @([regex]::Matches($text, '(?m)^[ \t]*(?:(?<id>[A-Za-z][A-Za-z0-9_.-]*)[ \t]+)?(?<res>PASS|FAIL)[ \t]'))
    $resultPass = if ($textReadable) { @($resultMatches | Where-Object { $_.Groups['res'].Value -eq 'PASS' }).Count } else { $null }
    $resultFail = if ($textReadable) { @($resultMatches | Where-Object { $_.Groups['res'].Value -eq 'FAIL' }).Count } else { $null }

    $blockedMarkers = @([regex]::Matches($text, '(?m)\bBLOCKED\b(?!\s*=)')).Count
    $criticalMarkers = @([regex]::Matches($text, '(?m)\bCRITICAL\b(?!\s*=)')).Count
    $cleanupWarnings = @([regex]::Matches($text, '(?im)^[ \t]*WARNING:.*(?:cleanup|listener|port[ \t]+\d+)')).Count

    if ($null -ne $summaryFail -and $summaryFail -gt 0) { [void]$hardReasons.Add(("summary-fail={0}" -f $summaryFail)) }
    if ($null -ne $summaryBlocked -and $summaryBlocked -gt 0) { [void]$hardReasons.Add(("summary-blocked={0}" -f $summaryBlocked)) }
    if ($null -ne $resultFail -and $resultFail -gt 0) { [void]$hardReasons.Add(("result-fail-lines={0}" -f $resultFail)) }
    if ($blockedMarkers -gt 0) { [void]$hardReasons.Add(("blocked-marker={0}" -f $blockedMarkers)) }
    if ($criticalMarkers -gt 0) { [void]$hardReasons.Add(("critical-marker={0}" -f $criticalMarkers)) }
    if ($sqlFailures -gt 0) { [void]$hardReasons.Add(("required-sql-failures={0}" -f $sqlFailures)) }
    if ($cleanupWarnings -gt 0) { [void]$hardReasons.Add(("cleanup-warning={0}" -f $cleanupWarnings)) }
    if ($failureMarker) { [void]$hardReasons.Add('failure-marker') }

    if ($summaryFound -and $summaryPass -eq 0 -and $summaryFail -eq 0 -and $summaryBlocked -eq 0) {
        [void]$indeterminateReasons.Add('empty-summary')
    }

    $cleanupOk = $true
    if ($Observation.PSObject.Properties['Cleanup'] -and $null -ne $Observation.Cleanup) {
        if (-not [bool]$Observation.Cleanup.Ok) {
            $cleanupOk = $false
            $reason = if ($Observation.Cleanup.PSObject.Properties['Reason']) { [string]$Observation.Cleanup.Reason } else { 'cleanup not verified' }
            [void]$hardReasons.Add('cleanup-not-verified')
            if ($reason) { [void]$hardReasons.Add(('cleanup-detail=' + (Protect-RunnerText -Text $reason))) }
        }
    }
    else {
        $cleanupOk = $false
        [void]$hardReasons.Add('cleanup-not-verified')
    }

    $reasons = @($hardReasons) + @($indeterminateReasons)
    $status = 'PASS'
    if ($hardReasons.Count -gt 0) { $status = 'NOT PASS' }
    elseif ($indeterminateReasons.Count -gt 0) { $status = 'INDETERMINATE' }
    elseif ([bool]$Entry.EvidenceOnly) { $status = 'EVIDENCE' }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$Entry.SuccessPattern) -and -not $successMarker) { $status = 'INDETERMINATE' }
    elseif (-not $summaryFound -and -not [bool]$Entry.EvidenceOnly -and [string]::IsNullOrWhiteSpace([string]$Entry.SuccessPattern)) { $status = 'INDETERMINATE' }

    return [pscustomobject]@{
        Status          = $status
        Reasons         = $reasons
        SummaryFound    = $summaryFound
        SummaryAmbiguous = $summaryAmbiguous
        SuccessMarker   = $successMarker
        FailureMarker   = $failureMarker
        SummaryPass     = $summaryPass
        SummaryFail     = $summaryFail
        SummaryBlocked  = $summaryBlocked
        ResultPass      = $resultPass
        ResultFail      = $resultFail
        SkipCount       = $skipCount
        SqlFailures     = $sqlFailures
        BlockedMarkers  = $blockedMarkers
        CriticalMarkers = $criticalMarkers
        CleanupWarnings = $cleanupWarnings
        CleanupOk       = $cleanupOk
        HardFailure     = ($hardReasons.Count -gt 0)
        Indeterminate   = ($indeterminateReasons.Count -gt 0)
    }
}

# ---------------------------------------------------------------------------------------------
# Child process execution
# ---------------------------------------------------------------------------------------------

function Set-RunnerBuildEnvironment {
    # These affect the child and any build it starts. They do not claim that MSBuild caused the
    # previous timeout; they only make node reuse and the build server predictable.
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:MSBUILDNOINPROCNODE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = '1'
}

function Invoke-RunnerSuiteProcess {
    param(
        [Parameter(Mandatory)]$Entry,
        [Parameter(Mandatory)][string]$RunDirectory,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [switch]$StopOwnedChildOnTimeout
    )

    $name = [string]$Entry.Name
    $stdoutPath = Join-Path $RunDirectory ($name + '.stdout.log')
    $stderrPath = Join-Path $RunDirectory ($name + '.stderr.log')
    Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue

    $process = $null
    $processId = 0
    $startedUtc = [DateTime]::UtcNow
    $endedUtc = $startedUtc
    $timedOut = $false
    $rawExit = $null
    $stdoutText = ''
    $stderrText = ''
    $textReadable = $true
    $sanitizationOk = $true

    try {
        $powershellExe = Join-Path $PSHOME 'powershell.exe'
        if (-not (Test-Path -LiteralPath $powershellExe)) { $powershellExe = 'powershell.exe' }
        $argumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', [string]$Entry.FullPath)

        # Deliberately no -Wait. The process is polled below; redirected output handles cannot
        # keep this runner blocked after the child process has exited.
        $process = Start-Process -FilePath $powershellExe -ArgumentList $argumentList `
            -WorkingDirectory $script:ScriptRoot -PassThru -NoNewWindow `
            -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
        $processId = [int]$process.Id
        $startedUtc = [DateTime]::UtcNow

        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        while (-not $process.HasExited) {
            if ($watch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
                $timedOut = $true
                break
            }
            Start-Sleep -Milliseconds 250
            try { $process.Refresh() } catch { }
        }

        if (-not $timedOut) {
            try { $process.Refresh() } catch { }
            if ($process.HasExited) { $rawExit = [int]$process.ExitCode }
            else { $timedOut = $true }
        }
        $watch.Stop()
        $endedUtc = [DateTime]::UtcNow

        if ($timedOut -and $StopOwnedChildOnTimeout) {
            # Only the exact child PID is stopped. No process tree is killed and no API PID is
            # guessed. The cleanup check below decides whether the run may continue.
            try { Stop-Process -Id $processId -Force -ErrorAction Stop } catch { }
            Start-Sleep -Milliseconds 500
        }
    }
    finally {
        if ($null -ne $process) { $process.Dispose() }
    }

    if ($timedOut) { $rawExit = $null }

    # The suite's finally block should have stopped its API before the child exited. Wait a
    # bounded time for that deterministic cleanup to settle, then ingest output with a shared
    # handle. If cleanup remains uncertain, the shared read still preserves any available bytes;
    # the independent final check below keeps that uncertainty as a hard failure.
    $cleanup = Wait-RunnerCleanupSettled -SuiteProcessId $processId -TimeoutSeconds 5

    try {
        if (Test-Path -LiteralPath $stdoutPath) { $stdoutText = Read-RunnerTextFile -Path $stdoutPath }
        if (Test-Path -LiteralPath $stderrPath) { $stderrText = Read-RunnerTextFile -Path $stderrPath }
    }
    catch {
        $textReadable = $false
        Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue
    }

    if ($textReadable) {
        # Evaluate both calls: a short-circuited -and would leave the second raw log untouched.
        $stdoutSanitized = Write-RunnerSanitizedLog -Path $stdoutPath
        $stderrSanitized = Write-RunnerSanitizedLog -Path $stderrPath
        $sanitizationOk = $stdoutSanitized -and $stderrSanitized
    }

    # Independent final check after ingestion. This never stops a process.
    $cleanup = Get-RunnerCleanupState -SuiteProcessId $processId

    return [pscustomobject]@{
        SuiteName       = $name
        SuitePath       = [string]$Entry.FullPath
        ProcessId       = $processId
        StartTime       = $startedUtc.ToString('o')
        EndTime         = $endedUtc.ToString('o')
        DurationSeconds = [math]::Round(($endedUtc - $startedUtc).TotalSeconds, 3)
        TimedOut        = $timedOut
        RawExitCode     = $rawExit
        StdoutPath      = $stdoutPath
        StderrPath      = $stderrPath
        Text            = ($stdoutText + "`n" + $stderrText)
        TextReadable    = $textReadable
        SanitizationOk  = $sanitizationOk
        Cleanup         = $cleanup
    }
}

# ---------------------------------------------------------------------------------------------
# Manifest and preflight
# ---------------------------------------------------------------------------------------------

function Import-RunnerManifest {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { throw "the regression manifest was not found: $Path" }
    $data = Import-PowerShellDataFile -LiteralPath $Path
    if ($null -eq $data) { throw 'the regression manifest is empty' }
    if ([int]$data.ApiPort -ne $script:ApiPort) {
        throw ("the manifest API port must be {0} (got {1})" -f $script:ApiPort, $data.ApiPort)
    }
    if ([string]::IsNullOrWhiteSpace([string]$data.ApprovedServer) -or [string]::IsNullOrWhiteSpace([string]$data.ApprovedDatabase)) {
        throw 'the manifest does not declare the approved server/database'
    }

    $suites = @($data.Suites)
    if ($suites.Count -ne $script:ExpectedSuites.Count) {
        throw ("the manifest must list exactly {0} suites (got {1})" -f $script:ExpectedSuites.Count, $suites.Count)
    }
    $actualNames = @($suites | ForEach-Object { [string]$_.Name })
    if (($actualNames -join ',') -ne ($script:ExpectedSuites -join ',')) {
        throw ("the manifest suite order is not the documented order: {0}" -f ($actualNames -join ', '))
    }

    $root = (Resolve-Path -LiteralPath $script:ScriptRoot).Path
    $root = $root.TrimEnd([char[]]@('\', '/'))
    $resolvedSuites = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $suites.Count; $i++) {
        $entry = $suites[$i]
        if ([int]$entry.Order -ne ($i + 1)) {
            throw ("the manifest Order for '{0}' must be {1}" -f $entry.Name, ($i + 1))
        }
        if ([string]::IsNullOrWhiteSpace([string]$entry.File) -or ([string]$entry.File -match '[\\/]')) {
            throw ("the manifest File for '{0}' must be a filename in the scripts directory" -f $entry.Name)
        }
        $fullPath = Join-Path $script:ScriptRoot ([string]$entry.File)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw ("the suite file for '{0}' was not found: {1}" -f $entry.Name, $fullPath)
        }
        $resolved = (Resolve-Path -LiteralPath $fullPath).Path
        if (-not $resolved.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw ("the suite file for '{0}' escapes the scripts directory: {1}" -f $entry.Name, $resolved)
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$entry.SummaryPattern)) {
            try { [void][regex]::new([string]$entry.SummaryPattern) }
            catch { throw ("the summary pattern for '{0}' is invalid: {1}" -f $entry.Name, $_.Exception.Message) }
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$entry.SuccessPattern)) {
            try { [void][regex]::new([string]$entry.SuccessPattern) }
            catch { throw ("the success pattern for '{0}' is invalid: {1}" -f $entry.Name, $_.Exception.Message) }
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$entry.FailurePattern)) {
            try { [void][regex]::new([string]$entry.FailurePattern) }
            catch { throw ("the failure pattern for '{0}' is invalid: {1}" -f $entry.Name, $_.Exception.Message) }
        }
        if ([bool]$entry.RequireSummary -and [string]::IsNullOrWhiteSpace([string]$entry.SummaryPattern) -and
            [string]::IsNullOrWhiteSpace([string]$entry.SuccessPattern) -and -not [bool]$entry.EvidenceOnly) {
            throw ("'{0}' requires a summary but declares neither a summary nor a success pattern" -f $entry.Name)
        }

        $copy = [pscustomobject]@{}
        foreach ($property in $entry.Keys) {
            $copy | Add-Member -NotePropertyName ([string]$property) -NotePropertyValue $entry[$property]
        }
        $copy | Add-Member -NotePropertyName 'FullPath' -NotePropertyValue $resolved
        [void]$resolvedSuites.Add($copy)
    }

    if ([int]$data.DefaultTimeoutSeconds -le 0) { throw 'the manifest DefaultTimeoutSeconds must be positive' }
    return [pscustomobject]@{
        Data   = $data
        Suites = $resolvedSuites.ToArray()
    }
}

function Assert-RunnerPreflight {
    param([Parameter(Mandatory)]$Manifest)

    $data = $Manifest.Data
    $targetPath = Join-Path $script:ScriptRoot 'test_target.ps1'
    if (-not (Test-Path -LiteralPath $targetPath)) { throw 'test_target.ps1 is missing; the database guard cannot be verified' }
    $targetHash = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash
    if ($targetHash -ine [string]$data.TargetGuardSha256) {
        throw ("the database guard test_target.ps1 has changed (expected {0}, got {1}); refusing to run" -f $data.TargetGuardSha256, $targetHash)
    }

    $protectedPath = Join-Path $script:ScriptRoot 's4_preflight.ps1'
    if (-not (Test-Path -LiteralPath $protectedPath)) { throw 'the protected s4_preflight.ps1 is missing; refusing to run' }
    $protectedHash = (Get-FileHash -LiteralPath $protectedPath -Algorithm SHA256).Hash
    if ($protectedHash -ine [string]$data.ProtectedPreflightSha256) {
        throw ("the protected s4_preflight.ps1 has changed (expected {0}, got {1}); refusing to run" -f $data.ProtectedPreflightSha256, $protectedHash)
    }

    # The guard resolves the environment connection string only; it opens no database.
    . $targetPath
    $target = Get-TestTarget
    if ($target.Server -ine [string]$data.ApprovedServer) {
        throw ("the resolved server '{0}' is not the approved server '{1}'" -f $target.Server, $data.ApprovedServer)
    }
    if ($target.Database -ine [string]$data.ApprovedDatabase) {
        throw ("the resolved database '{0}' is not the approved database '{1}'" -f $target.Database, $data.ApprovedDatabase)
    }

    $port = if ($env:NIBRAS_TEST_API_PORT) { [int]$env:NIBRAS_TEST_API_PORT } else { $script:ApiPort }
    if ($port -ne $script:ApiPort) { throw ("NIBRAS_TEST_API_PORT must be {0} (got {1})" -f $script:ApiPort, $port) }
    $listeners = @(Get-RunnerPortListeners -Port $script:ApiPort)
    if ($listeners.Count -gt 0) {
        $owners = ($listeners | ForEach-Object { [int]$_.OwningProcess } | Sort-Object -Unique) -join ','
        throw ("port {0} is already in use by process(es) {1}; the runner never connects to or kills a foreign instance" -f $script:ApiPort, $owners)
    }
    if (Test-Path -LiteralPath $script:IdentityFile) {
        throw ("the harness identity file exists before the run: {0}; refusing to overwrite or delete it" -f $script:IdentityFile)
    }

    $cleanup = Get-RunnerCleanupState -SuiteProcessId 0
    if (-not $cleanup.Ok) {
        throw ("cleanup/identity state is not safe to start: {0}" -f $cleanup.Reason)
    }

    return [pscustomobject]@{
        TargetServer   = [string]$target.Server
        TargetDatabase = [string]$target.Database
        Port           = $script:ApiPort
        Cleanup        = $cleanup
        TargetHash     = $targetHash
        ProtectedHash  = $protectedHash
    }
}

# ---------------------------------------------------------------------------------------------
# Reporting and self-test
# ---------------------------------------------------------------------------------------------

function Format-RunnerCount {
    param($Value)
    if ($null -eq $Value) { return 'unknown' }
    return [string]$Value
}

function Write-RunnerResultFile {
    param(
        [Parameter(Mandatory)][string]$RunDirectory,
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Preflight,
        [Parameter(Mandatory)]$Records,
        [Parameter(Mandatory)][int]$ExitCode
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add('NIBRAS F1 REGRESSION RUNNER')
    [void]$lines.Add(("generated_utc={0}" -f [DateTime]::UtcNow.ToString('o')))
    [void]$lines.Add(("target_server={0}" -f $Preflight.TargetServer))
    [void]$lines.Add(("target_database={0}" -f $Preflight.TargetDatabase))
    [void]$lines.Add(("api_port={0}" -f $Preflight.Port))
    [void]$lines.Add(("runner_exit={0}" -f $ExitCode))
    [void]$lines.Add('')
    foreach ($r in $Records) {
        $c = $r.Classification
        $o = $r.Observation
        [void]$lines.Add(('suite={0}' -f $o.SuiteName))
        [void]$lines.Add(('status={0}' -f $c.Status))
        [void]$lines.Add(('child_pid={0}' -f $o.ProcessId))
        [void]$lines.Add(('raw_exit={0}' -f $(if ($null -eq $o.RawExitCode) { 'unknown' } else { [string]$o.RawExitCode })))
        [void]$lines.Add(('timed_out={0}' -f [string]$o.TimedOut))
        [void]$lines.Add(('start_utc={0}' -f $o.StartTime))
        [void]$lines.Add(('end_utc={0}' -f $o.EndTime))
        [void]$lines.Add(('duration_seconds={0}' -f $o.DurationSeconds))
        [void]$lines.Add(('pass={0} fail={1} blocked={2} skip={3} sql_failures={4}' -f `
            (Format-RunnerCount $c.SummaryPass), (Format-RunnerCount $c.SummaryFail), (Format-RunnerCount $c.SummaryBlocked), (Format-RunnerCount $c.SkipCount), (Format-RunnerCount $c.SqlFailures)))
        [void]$lines.Add(('result_lines_pass={0} result_lines_fail={1}' -f `
            (Format-RunnerCount $c.ResultPass), (Format-RunnerCount $c.ResultFail)))
        [void]$lines.Add(('cleanup_ok={0} cleanup_state={1}' -f [string]$c.CleanupOk, [string]$o.Cleanup.State))
        [void]$lines.Add(('reasons={0}' -f ($c.Reasons -join '; ')))
        [void]$lines.Add(('stdout={0}' -f $o.StdoutPath))
        [void]$lines.Add(('stderr={0}' -f $o.StderrPath))
        [void]$lines.Add('')
    }
    Set-Content -LiteralPath (Join-Path $RunDirectory 'results.txt') -Value $lines -Encoding UTF8
}

function New-RunnerSelfEntry {
    param(
        [string]$Name,
        [string]$SummaryPattern = '',
        [string]$SuccessPattern = '',
        [string]$FailurePattern = '',
        [bool]$RequireSummary = $true,
        [bool]$EvidenceOnly = $false
    )
    return [pscustomobject]@{
        Name           = $Name
        SummaryPattern = $SummaryPattern
        SuccessPattern = $SuccessPattern
        FailurePattern = $FailurePattern
        RequireSummary = $RequireSummary
        EvidenceOnly   = $EvidenceOnly
    }
}

function New-RunnerSelfObservation {
    param(
        [string]$Text,
        $RawExitCode = 0,
        [bool]$TimedOut = $false,
        [bool]$CleanupOk = $true,
        [bool]$TextReadable = $true,
        [bool]$SanitizationOk = $true,
        [int]$ProcessId = 0
    )
    return [pscustomobject]@{
        Text           = $Text
        RawExitCode    = $RawExitCode
        TimedOut       = $TimedOut
        TextReadable   = $TextReadable
        SanitizationOk = $SanitizationOk
        ProcessId      = $ProcessId
        Cleanup        = [pscustomobject]@{
            Ok     = $CleanupOk
            State  = if ($CleanupOk) { 'free' } else { 'owned-api-running' }
            Reason = if ($CleanupOk) { '' } else { 'synthetic cleanup failure' }
        }
    }
}

function Invoke-RunnerSelfTest {
    $failures = New-Object System.Collections.Generic.List[string]
    $cases = 0

    function Assert-RunnerSelfCase {
        param($Name, $Entry, $Observation, [string]$ExpectedStatus, [string]$ExpectedReason = '')
        $script:SelfCases++
        $result = Get-RunnerClassification -Entry $Entry -Observation $Observation
        if ($result.Status -ne $ExpectedStatus) {
            [void]$script:SelfFailures.Add(("{0}: expected status '{1}', got '{2}' ({3})" -f $Name, $ExpectedStatus, $result.Status, ($result.Reasons -join '; ')))
            return
        }
        if ($ExpectedReason -and (($result.Reasons -join '; ') -notmatch [regex]::Escape($ExpectedReason))) {
            [void]$script:SelfFailures.Add(("{0}: expected reason '{1}', got '{2}'" -f $Name, $ExpectedReason, ($result.Reasons -join '; ')))
        }
    }

    $script:SelfCases = 0
    $script:SelfFailures = $failures

    $numeric = '(?m)^TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)[ \t\r]*$'
    $review = '(?m)^REVIEW TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t\r]*$'
    $s5 = '(?m)^S5 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)(?:[ \t]*\(auth calls used: \d+\))?[ \t\r]*$'
    $c4 = '(?m)^C4 TOTAL PASS=(?<pass>\d+)[ \t]+FAIL=(?<fail>\d+)[ \t]+BLOCKED=(?<blocked>\d+)[ \t\r]*$'
    $s4 = New-RunnerSelfEntry -Name 's4_tests' -SummaryPattern $numeric
    $rev = New-RunnerSelfEntry -Name 's4_review' -SummaryPattern $review
    $s5e = New-RunnerSelfEntry -Name 's5_offers' -SummaryPattern $s5
    $c4e = New-RunnerSelfEntry -Name 's4_c4_acceptance' -SummaryPattern $c4
    $ev = New-RunnerSelfEntry -Name 's4_checkpoint_a' -RequireSummary $false -EvidenceOnly $true
    $par = New-RunnerSelfEntry -Name 'parallel-approval' -SummaryPattern '' -SuccessPattern '(?m)^PARALLEL APPROVAL PASS\b' -FailurePattern '(?m)^FAILURES:'

    Assert-RunnerSelfCase 'all-pass' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=3 FAIL=0 BLOCKED=0') 'PASS'
    Assert-RunnerSelfCase 'all-pass-crlf' $s4 (New-RunnerSelfObservation -Text ("TOTAL PASS=3 FAIL=0 BLOCKED=0`r`n")) 'PASS'
    Assert-RunnerSelfCase 'review-fail-exit-0' $rev (New-RunnerSelfObservation -Text 'REVIEW TOTAL PASS=3 FAIL=1') 'NOT PASS' 'summary-fail'
    Assert-RunnerSelfCase 's4-blocked-summary' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=2 FAIL=0 BLOCKED=1') 'NOT PASS' 'summary-blocked'
    Assert-RunnerSelfCase 'empty-summary' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=0 FAIL=0 BLOCKED=0') 'INDETERMINATE' 'empty-summary'
    Assert-RunnerSelfCase 's5-blocked-summary' $s5e (New-RunnerSelfObservation -Text 'S5 TOTAL PASS=3 FAIL=0 BLOCKED=1 (auth calls used: 2)') 'NOT PASS' 'summary-blocked'
    Assert-RunnerSelfCase 's5-blocked-marker' $s5e (New-RunnerSelfObservation -Text ('S5 TOTAL PASS=3 FAIL=0 BLOCKED=1 (auth calls used: 2)' + "`nBLOCKED S5-55: index absent")) 'NOT PASS' 'blocked-marker'
    Assert-RunnerSelfCase 'critical-exit-0' $s4 (New-RunnerSelfObservation -Text ('TOTAL PASS=1 FAIL=0 BLOCKED=0' + "`nCRITICAL: unsafe state")) 'NOT PASS' 'critical-marker'
    Assert-RunnerSelfCase 'sql-failure-exit-0' $s4 (New-RunnerSelfObservation -Text ('TOTAL PASS=1 FAIL=0 BLOCKED=0' + "`nREQUIRED-SQL-FAILURES=1")) 'NOT PASS' 'required-sql-failures'
    Assert-RunnerSelfCase 'cleanup-warning' $s4 (New-RunnerSelfObservation -Text ('TOTAL PASS=1 FAIL=0 BLOCKED=0' + "`nWARNING: port 5199 still has a listener after the cleanup")) 'NOT PASS' 'cleanup-warning'
    Assert-RunnerSelfCase 'nonzero-exit' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=1 FAIL=0 BLOCKED=0' -RawExitCode 1) 'NOT PASS' 'nonzero-child-exit'
    Assert-RunnerSelfCase 'timeout' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=1 FAIL=0 BLOCKED=0' -RawExitCode $null -TimedOut $true) 'NOT PASS' 'timeout'
    Assert-RunnerSelfCase 'cleanup-failure' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=1 FAIL=0 BLOCKED=0' -CleanupOk $false) 'NOT PASS' 'cleanup-not-verified'
    Assert-RunnerSelfCase 'missing-summary' $s4 (New-RunnerSelfObservation -Text 'no summary here') 'NOT PASS' 'required-summary-missing'
    Assert-RunnerSelfCase 'unreadable-evidence' $s4 (New-RunnerSelfObservation -Text '' -TextReadable $false) 'NOT PASS' 'suite-output-unreadable'
    Assert-RunnerSelfCase 'sanitization-failure' $s4 (New-RunnerSelfObservation -Text 'TOTAL PASS=1 FAIL=0 BLOCKED=0' -SanitizationOk $false) 'NOT PASS' 'log-sanitization-failed'
    Assert-RunnerSelfCase 'ambiguous-summary' $s4 (New-RunnerSelfObservation -Text ('TOTAL PASS=1 FAIL=0 BLOCKED=0' + "`nTOTAL PASS=2 FAIL=0 BLOCKED=0")) 'INDETERMINATE' 'summary-ambiguous'
    Assert-RunnerSelfCase 'evidence-only' $ev (New-RunnerSelfObservation -Text 'C1 expected 201 actual 201') 'EVIDENCE'
    Assert-RunnerSelfCase 'evidence-blocked' $ev (New-RunnerSelfObservation -Text 'C4 -> BLOCKED: missing fixture') 'NOT PASS' 'blocked-marker'
    Assert-RunnerSelfCase 'parallel-pass' $par (New-RunnerSelfObservation -Text 'PARALLEL APPROVAL PASS (one 200, one 409)') 'PASS'
    Assert-RunnerSelfCase 'parallel-fail' $par (New-RunnerSelfObservation -Text 'FAILURES: race lost') 'NOT PASS' 'failure-marker'
    Assert-RunnerSelfCase 'parallel-missing-marker' $par (New-RunnerSelfObservation -Text 'some other output') 'INDETERMINATE' 'required-success-marker-missing'
    Assert-RunnerSelfCase 'c4-pass' $c4e (New-RunnerSelfObservation -Text 'C4 TOTAL PASS=15 FAIL=0 BLOCKED=0') 'PASS'
    Assert-RunnerSelfCase 'c4-blocked-summary' $c4e (New-RunnerSelfObservation -Text 'C4 TOTAL PASS=14 FAIL=0 BLOCKED=1') 'NOT PASS' 'summary-blocked'
    Assert-RunnerSelfCase 'c4-blocked-marker' $c4e (New-RunnerSelfObservation -Text ('C4 TOTAL PASS=14 FAIL=0 BLOCKED=1' + "`nC4-13 BLOCKED known eligible Verified land not visible")) 'NOT PASS' 'blocked-marker'
    Assert-RunnerSelfCase 'c4-fail-summary' $c4e (New-RunnerSelfObservation -Text 'C4 TOTAL PASS=14 FAIL=1 BLOCKED=0') 'NOT PASS' 'summary-fail'
    Assert-RunnerSelfCase 'hidden-result-fail' $s4 (New-RunnerSelfObservation -Text ('TOTAL PASS=1 FAIL=0 BLOCKED=0' + "`nA-01 FAIL exp=200 act=500")) 'NOT PASS' 'result-fail-lines'

    $fakeJwt = 'eyJ' + 'hbGciOiJIUzI1NiJ9' + '.' + 'eyJzdWIiOiJ0ZXN0In0' + '.' + 'signature123'
    $fakeSecret = 'Sup3r' + 'SecretValue'
    $fakeLink = 'https://example.test/verify?token=' + 'abc123'
    $raw = ("Authorization: Bearer {0}`npassword={1}`n{2}" -f $fakeJwt, $fakeSecret, $fakeLink)
    $safe = Protect-RunnerText -Text $raw
    $script:SelfCases++
    if ($safe -match 'eyJ|Sup3r|abc123') {
        [void]$script:SelfFailures.Add('sanitizer: a credential or verification token remained')
    }

    # A log that is open by another handle must be readable through the shared read path.
    # An exclusive lock is allowed to fail, but only after the bounded retry loop; it must never
    # be interpreted as an empty, passing suite.
    $lockedPath = Join-Path ([IO.Path]::GetTempPath()) ('nibras-runner-locked-' + [guid]::NewGuid().ToString('N') + '.log')
    try {
        Set-Content -LiteralPath $lockedPath -Value 'LOCKED-LOG-PAYLOAD' -Encoding UTF8 -NoNewline
        $share = ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
        $holder = New-Object -TypeName 'System.IO.FileStream' -ArgumentList @(
            $lockedPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::ReadWrite,
            $share
        )
        try {
            $script:SelfCases++
            try {
                $sharedText = Read-RunnerTextFile -Path $lockedPath
                if ($sharedText -notmatch 'LOCKED-LOG-PAYLOAD') {
                    [void]$script:SelfFailures.Add('locked-log-shared-read: the shared read returned the wrong bytes')
                }
            }
            catch {
                [void]$script:SelfFailures.Add(('locked-log-shared-read: ' + $_.Exception.Message))
            }
        }
        finally { $holder.Dispose() }

        $exclusive = New-Object -TypeName 'System.IO.FileStream' -ArgumentList @(
            $lockedPath,
            [IO.FileMode]::Open,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None
        )
        try {
            $script:SelfCases++
            $readFailed = $false
            try { $null = Read-RunnerTextFile -Path $lockedPath }
            catch { $readFailed = $true }
            if (-not $readFailed) {
                [void]$script:SelfFailures.Add('locked-log-exclusive-read: an unreadable log was treated as readable')
            }
        }
        finally { $exclusive.Dispose() }
    }
    catch {
        [void]$script:SelfFailures.Add(('locked-log fixture: ' + $_.Exception.Message))
    }
    finally {
        Remove-Item -LiteralPath $lockedPath -Force -ErrorAction SilentlyContinue
    }

    try {
        $manifestPath = Join-Path $script:ScriptRoot 'regression_manifest.psd1'
        $selfManifest = Import-RunnerManifest -Path $manifestPath
        $script:SelfCases++
        if ($selfManifest.Suites.Count -ne $script:ExpectedSuites.Count) {
            [void]$script:SelfFailures.Add('manifest: the parsed suite count is wrong')
        }
        if ($selfManifest.Suites[0].FullPath -notlike '*s4_tests.ps1') {
            [void]$script:SelfFailures.Add('manifest: the first suite path was not resolved')
        }
    }
    catch {
        [void]$script:SelfFailures.Add(('manifest: ' + $_.Exception.Message))
    }

    try {
        $tempRun = Join-Path ([IO.Path]::GetTempPath()) ('nibras-runner-selftest-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempRun -Force | Out-Null
        try {
            $syntheticPreflight = [pscustomobject]@{
                TargetServer   = 'synthetic-server'
                TargetDatabase = 'synthetic-database'
                Port           = 5199
            }

            $obs = New-RunnerSelfObservation -Text 'TOTAL PASS=1 FAIL=0 BLOCKED=0' -ProcessId 4242
            $obs | Add-Member -NotePropertyName SuiteName -NotePropertyValue 's4_tests'
            $obs | Add-Member -NotePropertyName StartTime -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $obs | Add-Member -NotePropertyName EndTime -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $obs | Add-Member -NotePropertyName DurationSeconds -NotePropertyValue 1.25
            $obs | Add-Member -NotePropertyName StdoutPath -NotePropertyValue 'synthetic.stdout.log'
            $obs | Add-Member -NotePropertyName StderrPath -NotePropertyValue 'synthetic.stderr.log'
            $classification = Get-RunnerClassification -Entry $s4 -Observation $obs
            $record = [pscustomobject]@{ Observation = $obs; Classification = $classification }
            Write-RunnerResultFile -RunDirectory $tempRun -Manifest ([pscustomobject]@{}) `
                -Preflight $syntheticPreflight -Records @($record) -ExitCode 0

            $script:SelfCases++
            $written = (Get-Content -LiteralPath (Join-Path $tempRun 'results.txt') -Raw) -replace "`r", ''
            if ($written -notmatch '(?m)^child_pid=4242$') {
                [void]$script:SelfFailures.Add('pid-persisted: results.txt did not contain the suite child PID')
            }
            if ($written -notmatch '(?m)^pass=1 fail=0 blocked=0') {
                [void]$script:SelfFailures.Add('pid-persisted: parsed counts were not preserved')
            }

            $obsUnknown = New-RunnerSelfObservation -Text 'no summary here' -ProcessId 5252
            $obsUnknown | Add-Member -NotePropertyName SuiteName -NotePropertyValue 's4_tests'
            $obsUnknown | Add-Member -NotePropertyName StartTime -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $obsUnknown | Add-Member -NotePropertyName EndTime -NotePropertyValue ([DateTime]::UtcNow.ToString('o'))
            $obsUnknown | Add-Member -NotePropertyName DurationSeconds -NotePropertyValue 0.5
            $obsUnknown | Add-Member -NotePropertyName StdoutPath -NotePropertyValue 'synthetic.stdout.log'
            $obsUnknown | Add-Member -NotePropertyName StderrPath -NotePropertyValue 'synthetic.stderr.log'
            $classificationUnknown = Get-RunnerClassification -Entry $s4 -Observation $obsUnknown
            $recordUnknown = [pscustomobject]@{ Observation = $obsUnknown; Classification = $classificationUnknown }
            Write-RunnerResultFile -RunDirectory $tempRun -Manifest ([pscustomobject]@{}) `
                -Preflight $syntheticPreflight -Records @($recordUnknown) -ExitCode 1

            $script:SelfCases++
            $writtenUnknown = (Get-Content -LiteralPath (Join-Path $tempRun 'results.txt') -Raw) -replace "`r", ''
            if ($writtenUnknown -notmatch '(?m)^pass=unknown fail=unknown blocked=unknown') {
                [void]$script:SelfFailures.Add('unknown-counts: missing-summary counts were rendered as numbers')
            }
            if ($writtenUnknown -notmatch '(?m)^child_pid=5252$') {
                [void]$script:SelfFailures.Add('unknown-counts: the second synthetic child PID was not preserved')
            }
        }
        finally {
            Remove-Item -LiteralPath $tempRun -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    catch {
        [void]$script:SelfFailures.Add(('pid/count writer fixture: ' + $_.Exception.Message))
    }

    if ($script:SelfFailures.Count -gt 0) {
        Write-Host ("SELFTEST FAIL ({0} case(s), {1} failure(s))" -f $script:SelfCases, $script:SelfFailures.Count)
        $script:SelfFailures | ForEach-Object { Write-Host ('  ' + $_) }
        return $false
    }
    Write-Host ("SELFTEST PASS ({0} mocked cases; no API, database, or external suite was started)" -f $script:SelfCases)
    return $true
}

# ---------------------------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------------------------

if ($SelfTest) {
    if (Invoke-RunnerSelfTest) { exit 0 }
    exit 1
}

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $script:ScriptRoot 'regression_manifest.psd1'
}

$records = New-Object System.Collections.Generic.List[object]
$runDirectory = $null
$preflight = $null
$runnerExit = 3

try {
    $manifest = Import-RunnerManifest -Path $ManifestPath
    $preflight = Assert-RunnerPreflight -Manifest $manifest
    if ($ValidateOnly) {
        Write-Host ("VALIDATE PASS target={0}/{1} port={2} target_guard={3} protected_preflight={4}" -f `
            $preflight.TargetServer, $preflight.TargetDatabase, $preflight.Port, $preflight.TargetHash, $preflight.ProtectedHash)
        exit 0
    }
    $runDirectory = New-RunnerPrivateDirectory

    $selected = @()
    if ($null -ne $SuiteNames -and @($SuiteNames).Count -gt 0) {
        $requested = @($SuiteNames | ForEach-Object { [string]$_ })
        if (($requested | Sort-Object -Unique).Count -ne $requested.Count) {
            throw 'the requested suite list contains duplicates'
        }
        foreach ($name in $script:ExpectedSuites) {
            if ($requested -contains $name) {
                $selected += @($manifest.Suites | Where-Object { $_.Name -eq $name })
            }
        }
        if ($selected.Count -ne $requested.Count) {
            throw ("unknown suite name(s) requested: {0}" -f (($requested | Where-Object { $script:ExpectedSuites -notcontains $_ }) -join ', '))
        }
    }
    else {
        $selected = @($manifest.Suites)
    }

    $effectiveTimeout = if ($TimeoutSeconds -gt 0) { $TimeoutSeconds } else { [int]$manifest.Data.DefaultTimeoutSeconds }
    if ($effectiveTimeout -le 0) { throw 'the effective timeout must be positive' }
    if ($selected.Count -eq 0) { throw 'no suites were selected' }

    Set-RunnerBuildEnvironment

    Write-Host ("RUNNER target={0}/{1} port={2} timeout={3}s evidence={4}" -f `
        $preflight.TargetServer, $preflight.TargetDatabase, $preflight.Port, $effectiveTimeout, $runDirectory)

    $stop = $false
    foreach ($entry in $selected) {
        if ($stop) { break }
        Write-Host ("SUITE START {0}" -f $entry.Name)
        $observation = Invoke-RunnerSuiteProcess -Entry $entry -RunDirectory $runDirectory `
            -TimeoutSeconds $effectiveTimeout -StopOwnedChildOnTimeout:$StopOwnedChildOnTimeout
        $classification = Get-RunnerClassification -Entry $entry -Observation $observation
        [void]$records.Add([pscustomobject]@{ Observation = $observation; Classification = $classification })

        $rawExitText = if ($null -eq $observation.RawExitCode) { 'unknown' } else { [string]$observation.RawExitCode }
        Write-Host ("SUITE END {0} status={1} child_pid={2} raw_exit={3} timeout={4} pass={5} fail={6} blocked={7} cleanup={8} reasons={9}" -f `
            $entry.Name, $classification.Status, $observation.ProcessId, $rawExitText, [string]$observation.TimedOut,
            (Format-RunnerCount $classification.SummaryPass), (Format-RunnerCount $classification.SummaryFail), (Format-RunnerCount $classification.SummaryBlocked),
            $observation.Cleanup.State, ($classification.Reasons -join '; '))

        if ($classification.Status -ne 'PASS') { $stop = $true }
    }

    $anyNotPass = @($records | Where-Object { $_.Classification.Status -eq 'NOT PASS' }).Count -gt 0
    $anyIndeterminate = @($records | Where-Object { $_.Classification.Status -in @('INDETERMINATE', 'EVIDENCE') }).Count -gt 0
    $anyCleanupFailure = @($records | Where-Object { -not [bool]$_.Classification.CleanupOk }).Count -gt 0

    if ($anyCleanupFailure) { $runnerExit = 3 }
    elseif ($anyNotPass) { $runnerExit = 1 }
    elseif ($anyIndeterminate) { $runnerExit = 2 }
    else { $runnerExit = 0 }

    if ($null -ne $runDirectory) {
        Write-RunnerResultFile -RunDirectory $runDirectory -Manifest $manifest -Preflight $preflight -Records $records -ExitCode $runnerExit
        Write-Host ("EVIDENCE {0}" -f $runDirectory)
    }
    Write-Host ("RUNNER EXIT {0} (0=all PASS, 1=NOT PASS, 2=only INDETERMINATE/EVIDENCE, 3=preflight/cleanup failure)" -f $runnerExit)
    exit $runnerExit
}
catch {
    $message = Protect-RunnerText -Text $_.Exception.Message
    Write-Host ("RUNNER ERROR: {0}" -f $message)
    if ($null -ne $runDirectory) {
        Write-Host ("EVIDENCE {0}" -f $runDirectory)
    }
    exit 3
}
