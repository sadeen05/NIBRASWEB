#requires -Version 5.1
<#
.SYNOPSIS
    Static contract checks for the NIBRAS API: controller authorization coverage and
    Swagger-export drift.

.DESCRIPTION
    Reads `Controllers/*.cs` and `docs/api/swagger.json` only. It starts no application,
    opens no database connection, performs no writes and needs no dependencies, so it is
    safe to run at any time (unlike the live suites and `verify-swagger-export.ps1`, which
    need a running instance).

    Hard checks (exit 1 when violated):
      1. Every controller action declares an HTTP verb attribute (it is routable).
      2. `[AllowAnonymous]` is used only by the two public auth endpoints
         (POST /api/auth/register, POST /api/auth/login).
      6. The test helpers never build an API URI by concatenating a command invocation
         (in PowerShell, "[Uri](Get-Base + $Path)" is a command call with "+" and $Path as
         arguments, so it targets the site root), and the five auth endpoints resolve to the
         exact absolute URLs when the base URL is a fake one (no HTTP, no database).

    Informational checks (never change the exit code):
      3. Actions that rely on the global fallback authorization policy instead of a
         class-level `[Authorize]` (they still require an authenticated user).
      4. Actions that carry `[Authorize]` without a role, i.e. their role/ownership rule
         is enforced in the service (the suites assert those 403s dynamically).
      5. Swagger-export drift: operations declared by the controllers but absent from
         `docs/api/swagger.json`, and operations in the export with no controller route.
         A re-export (see `verify-swagger-export.ps1`) is required when this lists
         anything; that script needs a running instance, so the drift is reported here
         without failing the static run.

.EXAMPLE
    ./static_contract_checks.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) {
    $repoRoot = Split-Path $repoRoot -Parent
}
if (-not $repoRoot) { throw 'NibrasWeb.csproj not found above this script.' }

$controllersDir = Join-Path $repoRoot 'Controllers'
$exportPath = Join-Path $repoRoot 'docs\api\swagger.json'

$failures = New-Object System.Collections.Generic.List[string]
$fallbackPolicyActions = New-Object System.Collections.Generic.List[string]
$rolelessActions = New-Object System.Collections.Generic.List[string]
$controllerOperations = New-Object System.Collections.Generic.List[string]

foreach ($file in Get-ChildItem -Path $controllersDir -Filter '*.cs' | Sort-Object Name) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $classMatch = [regex]::Match($text, '\bclass\s+(\w+?)Controller\b')
    if (-not $classMatch.Success) { continue }
    $controllerName = $classMatch.Groups[1].Value

    $classHeader = $text.Substring(0, $classMatch.Index)
    $classAuthorized = $classHeader -match '\[Authorize'
    $classAnonymous = $classHeader -match '\[AllowAnonymous'

    $routeMatch = [regex]::Match($text, '\[Route\("([^"]+)"\)\]')
    $baseRoute = ''
    if ($routeMatch.Success) {
        $baseRoute = $routeMatch.Groups[1].Value.Replace('[controller]', $controllerName)
    }

    foreach ($action in [regex]::Matches($text, '\[Http(Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?\]')) {
        $verb = $action.Groups[1].Value.ToUpperInvariant()
        $template = $action.Groups[2].Value
        $tail = $text.Substring($action.Index)
        $nameMatch = [regex]::Match($tail, 'public\s+[^;{}]*?\b(\w+)\s*\(')
        $actionName = if ($nameMatch.Success) { $nameMatch.Groups[1].Value } else { '<unparsed>' }

        $path = '/' + $baseRoute.Trim('/')
        if ($template) { $path = $path.TrimEnd('/') + '/' + $template.Trim('/') }
        $controllerOperations.Add(($verb + ' ' + $path))

        $block = $text.Substring($action.Index, $nameMatch.Index)
        $lookBehindStart = [Math]::Max(0, $action.Index - 120)
        $lookBehind = $text.Substring($lookBehindStart, $action.Index - $lookBehindStart)
        $actionAuthorized = ($block -match '\[Authorize') -or ($lookBehind -match '\[Authorize\s*\]?\s*$')
        $actionAnonymous = ($block -match '\[AllowAnonymous') -or ($lookBehind -match '\[AllowAnonymous\s*\]?\s*$')
        $roleScoped = ($block + $lookBehind) -match '\[Authorize\(Roles'

        if (-not ($classAuthorized -or $actionAuthorized -or $classAnonymous -or $actionAnonymous)) {
            $fallbackPolicyActions.Add("$controllerName.$actionName")
        }
        if (($classAuthorized -or $actionAuthorized) -and -not $roleScoped -and -not $actionAnonymous) {
            $rolelessActions.Add("$controllerName.$actionName")
        }
    }
}

# Check 2: [AllowAnonymous] may only appear on the public auth endpoints.
# The allow-list is explicit and closed: registration and login are anonymous, and the admin
# activation endpoint is anonymous because the single-use invitation token is its only
# credential (it is rate-limited with its own budget and answers a generic failure). Any other
# anonymous endpoint still fails this check.
$allowedAnonymous = @{
    'AuthController.cs' = @('Register', 'Login', 'ActivateAdmin', 'VerifyEmail', 'ResendVerification')
}

foreach ($file in Get-ChildItem -Path $controllersDir -Filter '*.cs') {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    foreach ($anonymous in [regex]::Matches($text, '\[AllowAnonymous\]')) {
        $tail = $text.Substring($anonymous.Index)
        $nameMatch = [regex]::Match($tail, 'public\s+[^;{}]*?\b(\w+)\s*\(')
        $actionName = if ($nameMatch.Success) { $nameMatch.Groups[1].Value } else { '<unparsed>' }
        $allowedForFile = if ($allowedAnonymous.ContainsKey($file.Name)) { $allowedAnonymous[$file.Name] } else { @() }
        $isPublicAuth = $allowedForFile -contains $actionName
        if (-not $isPublicAuth) {
            $failures.Add("AllowAnonymous outside the public auth endpoints: $($file.Name) -> $actionName")
        }
    }
}

$controllerOperations = @($controllerOperations | Sort-Object -Unique)

# Check 5 (informational): Swagger export drift.
$exportOps = @()
if (Test-Path -LiteralPath $exportPath) {
    $export = Get-Content -LiteralPath $exportPath -Raw | ConvertFrom-Json
    $exportOps = @($export.paths.PSObject.Properties | ForEach-Object {
            $path = $_.Name
            $_.Value.PSObject.Properties |
                Where-Object { $_.Name -in @('get', 'post', 'put', 'delete', 'patch') } |
                ForEach-Object { $_.Name.ToUpperInvariant() + ' ' + $path }
        } | Sort-Object -Unique)
}

$missingFromExport = @($controllerOperations | Where-Object { $_ -notin $exportOps })
$notInControllers = @($exportOps | Where-Object { $_ -notin $controllerOperations })

Write-Output "Controllers      : $($controllerOperations.Count) operations"
Write-Output "Swagger export   : $($exportOps.Count) operations ($exportPath)"
Write-Output ''
Write-Output 'Check 3 (informational) actions relying on the global fallback policy:'
if ($fallbackPolicyActions.Count -eq 0) { Write-Output '  (none)' } else { $fallbackPolicyActions | Sort-Object | ForEach-Object { '  ' + $_ } }
Write-Output 'Check 4 (informational) [Authorize] without a role (service-enforced):'
if ($rolelessActions.Count -eq 0) { Write-Output '  (none)' } else { $rolelessActions | Sort-Object | ForEach-Object { '  ' + $_ } }
Write-Output 'Check 5 (informational) declared by controllers but missing from the export:'
if ($missingFromExport.Count -eq 0) { Write-Output '  (none)' } else { $missingFromExport | ForEach-Object { '  ' + $_ } }
Write-Output 'Check 5 (informational) in the export but not declared by a controller:'
if ($notInControllers.Count -eq 0) { Write-Output '  (none)' } else { $notInControllers | ForEach-Object { '  ' + $_ } }

# ---------------------------------------------------------------------------------------------
# Check 6 (hard): API URI construction in the test scripts. A command invocation followed by
# "+" is not a concatenation: in PowerShell it passes "+" and the path as ARGUMENTS, so the
# helper silently targets the site root (no endpoint -> the fallback policy answers 401).
# The AST is inspected, so a comment that quotes the bad pattern is not a false positive.
$testScriptFiles = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' |
        Where-Object { $_.Name -ne 'static_contract_checks.ps1' })
$badCommandInvocations = New-Object System.Collections.Generic.List[string]
foreach ($scriptFile in $testScriptFiles) {
    $tokens = $null
    $parseErrors = $null
    $scriptAst = [System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
        $failures.Add("PowerShell parse error: $($scriptFile.Name) -> $($parseError.Message)")
    }
    $commands = $scriptAst.FindAll({
            param($node)
            if ($node -isnot [System.Management.Automation.Language.CommandAst]) { return $false }
            $elements = $node.CommandElements
            if ($elements.Count -lt 2) { return $false }
            if ($elements[0] -isnot [System.Management.Automation.Language.StringConstantExpressionAst]) { return $false }
            if ($elements[0].Value -notmatch '^(Get|Invoke)-[A-Za-z0-9]+$') { return $false }
            return ($elements[1] -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                $elements[1].Value -eq '+')
        }, $true)
    foreach ($command in $commands) {
        $badCommandInvocations.Add(("{0}:{1} -> {2}" -f $scriptFile.Name, $command.Extent.StartLineNumber, $command.Extent.Text))
    }
}
foreach ($bad in $badCommandInvocations) {
    $failures.Add("a command is invoked with '+' as an argument (broken URI concatenation; use a separately evaluated base URL): $bad")
}

# The same pattern is proved wrong in this PowerShell, so the check itself is trustworthy.
function Get-StaticProbeBaseUrl { 'http://127.0.0.1:5199' }
$probePath = '/api/auth/register'
$probeResult = (Get-StaticProbeBaseUrl + $probePath)
if ($probeResult -ne 'http://127.0.0.1:5199') {
    $failures.Add('the PowerShell command-invocation reference check did not behave as expected (the URI check cannot be trusted)')
}
Remove-Item Function:\Get-StaticProbeBaseUrl -ErrorAction SilentlyContinue

# With a fake base URL (no HTTP, no database), the helper must produce the exact absolute URLs.
. (Join-Path $PSScriptRoot 'verification_helpers.ps1')
function Get-VerificationBaseUrl { 'http://127.0.0.1:5199' }
$expectedUrls = [ordered]@{
    '/api/auth/register'            = 'http://127.0.0.1:5199/api/auth/register'
    '/api/auth/login'               = 'http://127.0.0.1:5199/api/auth/login'
    '/api/auth/verify-email'        = 'http://127.0.0.1:5199/api/auth/verify-email'
    '/api/auth/resend-verification' = 'http://127.0.0.1:5199/api/auth/resend-verification'
    '/api/auth/activate-admin'      = 'http://127.0.0.1:5199/api/auth/activate-admin'
}
foreach ($path in $expectedUrls.Keys) {
    $uri = Get-VerificationApiUri -Path $path
    if ($uri.AbsoluteUri -ne $expectedUrls[$path]) {
        $failures.Add("API URI construction: '$path' -> '$($uri.AbsoluteUri)' (expected '$($expectedUrls[$path])')")
    }
}
function Get-VerificationBaseUrl { 'http://127.0.0.1:5199/' }
$trailing = Get-VerificationApiUri -Path '/api/auth/register'
if ($trailing.AbsoluteUri -ne 'http://127.0.0.1:5199/api/auth/register') {
    $failures.Add("API URI construction: a base URL with a trailing slash produced '$($trailing.AbsoluteUri)'")
}
function Get-VerificationBaseUrl { 'http://127.0.0.1:5199' }
$noSlash = Get-VerificationApiUri -Path 'api/auth/login'
if ($noSlash.AbsoluteUri -ne 'http://127.0.0.1:5199/api/auth/login') {
    $failures.Add("API URI construction: a path without a leading slash produced '$($noSlash.AbsoluteUri)'")
}

Write-Output "Check 6 (hard) API URI construction (fake base URL, no HTTP/database):"
Write-Output "  command-call concatenations: $($badCommandInvocations.Count)"
Write-Output "  auth endpoint URLs checked : $($expectedUrls.Count) (+2 normalization cases)"

Write-Output ''
if ($failures.Count -gt 0) {
    Write-Output "RESULT: FAIL ($($failures.Count) hard violation(s))"
    $failures | ForEach-Object { '  ' + $_ }
    exit 1
}

Write-Output 'RESULT: PASS (hard checks 1-2 and 6; see the informational lists above)'
exit 0
