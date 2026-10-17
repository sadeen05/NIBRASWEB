#requires -Version 5.1
<#
.SYNOPSIS
    Regression check for the production Content-Security-Policy (SEC-16).

.DESCRIPTION
    SEC-16 requires the React production build to carry a strict CSP. The
    policy is injected by the `nibras-production-csp` Vite plugin in
    `frontend/vite.config.ts`, which is applied to `vite build` only so that
    `npm run dev` keeps working.

    This script:

      1. optionally runs `npm run build` in `frontend/`,
      2. HTML-decodes `frontend/dist/index.html`,
      3. asserts the built page contains a
         `<meta http-equiv="Content-Security-Policy">` tag whose content is
         exactly the policy required by SEC-16, and
      4. asserts the built page contains no inline `<script>` element, because
         the required `script-src 'self'` would refuse to execute one.

    No database access, no application start, no network access beyond the
    local `npm` process.

.PARAMETER RepoRoot
    Repository root. Defaults to the nearest ancestor of this script that
    contains `NibrasWeb.csproj`.

.PARAMETER SkipBuild
    Reuse the existing `frontend/dist` output instead of running a fresh build.

.EXAMPLE
    ./verify-frontend-csp.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

# SEC-16 production CSP (must match PRODUCTION_CSP in frontend/vite.config.ts).
$expectedCsp = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'"

if (-not $RepoRoot) {
    $dir = $PSScriptRoot
    while ($dir -and -not (Test-Path (Join-Path $dir 'NibrasWeb.csproj'))) {
        $dir = Split-Path $dir -Parent
    }
    if (-not $dir) { throw 'NibrasWeb.csproj not found above this script.' }
    $RepoRoot = $dir
}

$frontend = Join-Path $RepoRoot 'frontend'
$indexHtml = Join-Path $frontend 'dist\index.html'

if (-not $SkipBuild) {
    Write-Output '== building the frontend (npm run build) =='
    Push-Location $frontend
    # `npm` writes its chunk-size advisory to stderr; under
    # $ErrorActionPreference = 'Stop' Windows PowerShell would turn that native
    # stderr output into a terminating NativeCommandError, so relax it here and
    # rely on $LASTEXITCODE instead.
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & npm run build 2>&1 | Out-Null
        $buildExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorAction
        Pop-Location
    }
    if ($buildExit -ne 0) { throw "npm run build failed with exit code $buildExit." }
}

if (-not (Test-Path $indexHtml)) {
    throw "Built page not found at $indexHtml. Run a build first (or omit -SkipBuild)."
}

$raw = Get-Content -LiteralPath $indexHtml -Raw
$html = [System.Net.WebUtility]::HtmlDecode($raw)

$metaPattern = '<meta\s+http-equiv="Content-Security-Policy"\s+content="([^"]*)"'
$match = [regex]::Match($html, $metaPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
$actualCsp = if ($match.Success) { $match.Groups[1].Value } else { $null }

$inlineScripts = [regex]::Matches($html, '<script(?![^>]*\ssrc=)[^>]*>').Count

Write-Output "Built page        : $indexHtml"
Write-Output "CSP meta present  : $([bool]$match.Success)"
Write-Output "CSP content       : $actualCsp"
Write-Output "Inline <script>   : $inlineScripts"
Write-Output "Expected CSP      : $expectedCsp"

$failures = @()
if (-not $match.Success) {
    $failures += 'the built page has no Content-Security-Policy meta tag'
}
elseif ($actualCsp -ne $expectedCsp) {
    $failures += 'the CSP meta content does not match the SEC-16 policy byte-for-byte'
}
if ($inlineScripts -ne 0) {
    $failures += "the built page contains $inlineScripts inline <script> element(s), which script-src 'self' would block"
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output "FAIL              : $failure" }
    Write-Output 'RESULT            : FAIL'
    exit 1
}

Write-Output 'RESULT            : PASS'
exit 0
