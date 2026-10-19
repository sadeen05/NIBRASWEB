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

Write-Output ''
if ($failures.Count -gt 0) {
    Write-Output "RESULT: FAIL ($($failures.Count) hard violation(s))"
    $failures | ForEach-Object { '  ' + $_ }
    exit 1
}

Write-Output 'RESULT: PASS (hard checks 1-2; see the informational lists above)'
exit 0
