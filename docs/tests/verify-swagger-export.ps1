#requires -Version 5.1
<#
.SYNOPSIS
    Regression check that the committed Swagger export matches the live API surface.

.DESCRIPTION
    Amendment A1 requires the API to stay stable and its Swagger JSON to be
    exported after every backend step, so `docs/api/swagger.json` is the
    published contract for the frontend and for the reviewers.

    This script compares the committed export with the specification served by
    the current build and fails when an operation (method + path) or a schema
    name exists on one side only. Property-level ordering inside a schema is
    not compared.

    By default the script starts and stops its own application instance on
    the dedicated harness port (5199) via `s4_app.ps1`, with both startup seeders
    forced to a no-op
    (`Seed__SuperAdminEmail` whitespace, `Seed__DemoData` unset), so the run
    performs no database writes. Pass -BaseUrl to reuse an already-running
    instance instead.

.PARAMETER BaseUrl
    Base URL of a running instance. When omitted the script starts its own.

.PARAMETER ExportPath
    Path to the committed export. Default: docs/api/swagger.json at the repo root.

.EXAMPLE
    ./verify-swagger-export.ps1
#>
[CmdletBinding()]
param(
    [string] $BaseUrl,
    [string] $ExportPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'NibrasWeb.csproj'))) {
    $repoRoot = Split-Path $repoRoot -Parent
}
if (-not $repoRoot) { throw 'NibrasWeb.csproj not found above this script.' }

if (-not $ExportPath) { $ExportPath = Join-Path $repoRoot 'docs\api\swagger.json' }
if (-not (Test-Path $ExportPath)) { throw "Committed export not found at $ExportPath." }

function Get-OperationSet {
    param($Document)
    $set = New-Object System.Collections.Generic.List[string]
    foreach ($path in $Document.paths.PSObject.Properties) {
        foreach ($operation in $path.Value.PSObject.Properties) {
            if ($operation.Name -in @('get', 'post', 'put', 'delete', 'patch')) {
                $set.Add($operation.Name.ToUpper() + ' ' + $path.Name)
            }
        }
    }
    return @($set | Sort-Object)
}

function Get-SchemaSet {
    param($Document)
    $set = New-Object System.Collections.Generic.List[string]
    if ($Document.components -and $Document.components.schemas) {
        foreach ($schema in $Document.components.schemas.PSObject.Properties) { $set.Add($schema.Name) }
    }
    return @($set | Sort-Object)
}

$ownsApp = $false
$app = $null
if (-not $BaseUrl) {
    $env:Seed__SuperAdminEmail = ' '
    $env:Seed__SuperAdminPassword = ' '
    Remove-Item Env:Seed__DemoData -ErrorAction SilentlyContinue
    . (Join-Path $repoRoot 'docs\tests\scripts\s4_app.ps1')
    $app = Start-S4App -AuthPerMinute 1000
    $ownsApp = $true
    $BaseUrl = "http://localhost:$($app.Port)"
}

try {
    $live = (Invoke-WebRequest -Uri "$($BaseUrl.TrimEnd('/'))/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 30).Content | ConvertFrom-Json
    $committed = Get-Content -LiteralPath $ExportPath -Raw | ConvertFrom-Json

    $liveOps = Get-OperationSet $live
    $exportOps = Get-OperationSet $committed
    $liveSchemas = Get-SchemaSet $live
    $exportSchemas = Get-SchemaSet $committed

    $missing = @($liveOps | Where-Object { $_ -notin $exportOps })
    $extra = @($exportOps | Where-Object { $_ -notin $liveOps })
    $schemaMissing = @($liveSchemas | Where-Object { $_ -notin $exportSchemas })
    $schemaExtra = @($exportSchemas | Where-Object { $_ -notin $liveSchemas })

    Write-Output "Live spec        : $($liveOps.Count) operations, $($liveSchemas.Count) schemas"
    Write-Output "Committed export : $($exportOps.Count) operations, $($exportSchemas.Count) schemas"
    Write-Output "Export file      : $ExportPath"

    foreach ($pair in @(
            @{ Label = 'served live but missing from the export'; Items = $missing },
            @{ Label = 'in the export but not served live'; Items = $extra },
            @{ Label = 'live schemas missing from the export'; Items = $schemaMissing },
            @{ Label = 'export schemas not served live'; Items = $schemaExtra })) {
        Write-Output "  $($pair.Label):"
        if ($pair.Items.Count -eq 0) { Write-Output '    (none)' } else { $pair.Items | ForEach-Object { '    ' + $_ } }
    }

    $drift = $missing.Count + $extra.Count + $schemaMissing.Count + $schemaExtra.Count
    if ($drift -gt 0) {
        Write-Output "RESULT           : FAIL ($drift difference(s) - re-export the specification)"
        exit 1
    }

    Write-Output 'RESULT           : PASS (the exported contract matches the live API surface)'
    exit 0
}
finally {
    if ($ownsApp) { Stop-S4App $app }
}
