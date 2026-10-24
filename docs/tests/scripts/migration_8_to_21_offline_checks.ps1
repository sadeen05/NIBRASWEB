# Offline, read-only checks for the NEBRASDB migration 8->21 compatibility patch.
# This script does not connect to any database and does not modify any file.
# It fails with a non-zero exit code when an expected invariant is missing.

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$passes = 0
$failures = 0

function Check([bool]$condition, [string]$label) {
    if ($condition) {
        Write-Host "PASS $label"
        $script:passes++
    }
    else {
        Write-Host "FAIL $label"
        $script:failures++
    }
}

function Read-RepoText([string]$relativePath) {
    $full = Join-Path $repo $relativePath
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Missing expected file: $relativePath"
    }
    return Get-Content -Raw -LiteralPath $full
}

$nullSafeLocation = '[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL'
$nullSafeGovernorate = '[IS_DELETED] = 0 AND [GOVERNORATE_ID] IS NOT NULL AND [VILLAGE] IS NOT NULL AND [BASIN] IS NOT NULL AND [PARCEL_NUMBER] IS NOT NULL'
$weakLocation = '[IS_DELETED] = 0 AND [REGION_ID] IS NOT NULL'
$weakGovernorate = '[IS_DELETED] = 0 AND [GOVERNORATE_ID] IS NOT NULL'
$weakLocationPattern = '\[IS_DELETED\] = 0 AND \[REGION_ID\] IS NOT NULL(?! AND \[VILLAGE\])'
$weakGovernoratePattern = '\[IS_DELETED\] = 0 AND \[GOVERNORATE_ID\] IS NOT NULL(?! AND \[VILLAGE\])'

$model = Read-RepoText 'Models\NebrasdbContext.cs'
$snapshot = Read-RepoText 'Migrations\NebrasdbContextModelSnapshot.cs'
$step2 = Read-RepoText 'Migrations\20261010170736_Step2_NewSchemaAndSeeds.cs'
$step2Designer = Read-RepoText 'Migrations\20261010170736_Step2_NewSchemaAndSeeds.Designer.cs'
$phase1 = Read-RepoText 'Migrations\20261017140348_Phase1_GovernoratesAndLandAssessment.cs'
$phase1Designer = Read-RepoText 'Migrations\20261017140348_Phase1_GovernoratesAndLandAssessment.Designer.cs'

$hardenMigrationPath = (Get-ChildItem -Path (Join-Path $repo 'Migrations') -Filter '*HardenLandLocationUniqueness.cs' |
    Where-Object { $_.Name -notlike '*.Designer.cs' } | Sort-Object Name | Select-Object -Last 1).FullName
$hardenDesignerPath = (Get-ChildItem -Path (Join-Path $repo 'Migrations') -Filter '*HardenLandLocationUniqueness.Designer.cs' |
    Sort-Object Name | Select-Object -Last 1).FullName

if (-not $hardenMigrationPath -or -not $hardenDesignerPath) {
    throw 'The HardenLandLocationUniqueness migration or its designer is missing.'
}

$hardenMigration = Get-Content -Raw -LiteralPath $hardenMigrationPath
$hardenDesigner = Get-Content -Raw -LiteralPath $hardenDesignerPath
$hardenName = Split-Path -Leaf $hardenMigrationPath

# 1. Null-safe filters are applied consistently in the model, snapshot, migration 10,
#    migration 13, migration 21 and their designers.
Check ($model.Contains($nullSafeLocation)) 'model: UQ_LAND_LOCATION is null-safe'
Check ($model.Contains($nullSafeGovernorate)) 'model: UQ_LAND_LOCATION_GOV is null-safe'
Check ($snapshot.Contains($nullSafeLocation)) 'snapshot: UQ_LAND_LOCATION is null-safe'
Check ($snapshot.Contains($nullSafeGovernorate)) 'snapshot: UQ_LAND_LOCATION_GOV is null-safe'
Check ($step2.Contains($nullSafeLocation)) 'migration 10: UQ_LAND_LOCATION is null-safe'
Check ($step2Designer.Contains($nullSafeLocation)) 'migration 10 designer: UQ_LAND_LOCATION is null-safe'
Check ($phase1.Contains($nullSafeLocation)) 'migration 13: UQ_LAND_LOCATION is null-safe'
Check ($phase1.Contains($nullSafeGovernorate)) 'migration 13: UQ_LAND_LOCATION_GOV is null-safe'
Check ($phase1Designer.Contains($nullSafeLocation)) 'migration 13 designer: UQ_LAND_LOCATION is null-safe'
Check ($phase1Designer.Contains($nullSafeGovernorate)) 'migration 13 designer: UQ_LAND_LOCATION_GOV is null-safe'
Check ($hardenMigration.Contains($nullSafeLocation)) 'migration 21 Up: UQ_LAND_LOCATION is null-safe'
Check ($hardenMigration.Contains($nullSafeGovernorate)) 'migration 21 Up: UQ_LAND_LOCATION_GOV is null-safe'
Check ($hardenDesigner.Contains($nullSafeLocation)) 'migration 21 designer: UQ_LAND_LOCATION is null-safe'
Check ($hardenDesigner.Contains($nullSafeGovernorate)) 'migration 21 designer: UQ_LAND_LOCATION_GOV is null-safe'

# 2. Complete duplicate locations remain rejected: each unique index still covers all
#    four location components and the filter requires every component to be non-null.
Check ($model -match 'UQ_LAND_LOCATION"\)\s*\r?\n\s*\.IsUnique\(\)' -and
    $model -match 'new \{ e\.RegionId, e\.Village, e\.Basin, e\.ParcelNumber \}') 'model: complete region locations are unique'
Check ($model -match 'new \{ e\.GovernorateId, e\.Village, e\.Basin, e\.ParcelNumber \}') 'model: complete governorate locations are unique'
Check ($nullSafeLocation.Contains('[REGION_ID] IS NOT NULL') -and
    $nullSafeLocation.Contains('[VILLAGE] IS NOT NULL') -and
    $nullSafeLocation.Contains('[BASIN] IS NOT NULL') -and
    $nullSafeLocation.Contains('[PARCEL_NUMBER] IS NOT NULL')) 'UQ_LAND_LOCATION requires all components'
Check ($nullSafeGovernorate.Contains('[GOVERNORATE_ID] IS NOT NULL') -and
    $nullSafeGovernorate.Contains('[VILLAGE] IS NOT NULL') -and
    $nullSafeGovernorate.Contains('[BASIN] IS NOT NULL') -and
    $nullSafeGovernorate.Contains('[PARCEL_NUMBER] IS NOT NULL')) 'UQ_LAND_LOCATION_GOV requires all components'

# 3. Migration 10 no longer performs unconditional reference-row inserts.
Check (-not ($step2 -match 'InsertData\s*\(\s*table:\s*"DOCUMENT_TYPES"')) 'migration 10: DOCUMENT_TYPES InsertData removed'
Check (-not ($step2 -match 'InsertData\s*\(\s*table:\s*"LAND_CRITERIA"')) 'migration 10: LAND_CRITERIA InsertData removed'
Check ($step2 -match 'IF NOT EXISTS \(SELECT 1 FROM \[DOCUMENT_TYPES\] WHERE \[ID\] = 1\)') 'migration 10: DOCUMENT_TYPES ID 1 conditional insert'
Check ($step2 -match 'IF NOT EXISTS \(SELECT 1 FROM \[DOCUMENT_TYPES\] WHERE \[ID\] = 2\)') 'migration 10: DOCUMENT_TYPES ID 2 conditional insert'
Check ($step2 -match 'IF NOT EXISTS \(SELECT 1 FROM \[LAND_CRITERIA\] WHERE \[ID\] = 1\)') 'migration 10: LAND_CRITERIA ID 1 conditional insert'

# 4. Existing rows and their business values are never overwritten.
Check (-not ($step2 -match 'UPDATE \[DOCUMENT_TYPES\]\s+SET\s+\[IS_REQUIRED_FOR_VERIFICATION\]')) 'migration 10: existing document-required flag is not overwritten'
Check (-not ($step2 -match 'UPDATE \[LAND_CRITERIA\]')) 'migration 10: existing criterion values are not overwritten'
Check (-not ($step2 -match 'DELETE FROM \[DOCUMENT_TYPES\]')) 'migration 10: DOCUMENT_TYPES rows are not deleted'
Check (-not ($step2 -match 'DELETE FROM \[LAND_CRITERIA\]')) 'migration 10: LAND_CRITERIA rows are not deleted'

# 5. Ambiguous mappings fail closed.
Check ($step2 -match "THROW 51000") 'migration 10: conflicting DOCUMENT_TYPES ID 1 name fails closed'
Check ($step2 -match "THROW 51001") 'migration 10: duplicate TitleDeed identity fails closed'
Check ($step2 -match "THROW 51002") 'migration 10: conflicting DOCUMENT_TYPES ID 2 name fails closed'
Check ($step2 -match "THROW 51003") 'migration 10: duplicate EncumbranceStatement identity fails closed'
Check ($step2 -match "THROW 51004") 'migration 10: ambiguous non-empty LAND_CRITERIA fails closed'

# 6. Rollback caveats are documented; the only remaining weak filters are in migration 21 Down.
Check ($hardenMigration -match 'Rollback is restore-from-backup') 'migration 21 Down: rollback caveat documented'
Check ($hardenMigration -match $weakLocationPattern -and $hardenMigration -match $weakGovernoratePattern) 'migration 21 Down: previous weak filters documented'
Check (-not ($model -match $weakLocationPattern)) 'model: no weak UQ_LAND_LOCATION filter remains'
Check (-not ($snapshot -match $weakLocationPattern)) 'snapshot: no weak UQ_LAND_LOCATION filter remains'
Check (-not ($phase1 -match $weakLocationPattern)) 'migration 13: no weak UQ_LAND_LOCATION filter remains'
Check (-not ($phase1 -match $weakGovernoratePattern)) 'migration 13: no weak UQ_LAND_LOCATION_GOV filter remains'

# 7. The new migration is ordered after the previous latest migration.
$latestPrevious = '20261022191140_AddContractRevisionWorkflow'
Check ($hardenName -gt $latestPrevious) "migration 21 is ordered after $latestPrevious"

if ($failures -gt 0) {
    Write-Host "OFFLINE_MIGRATION_CHECKS FAIL=$failures"
    exit 1
}

Write-Host "OFFLINE_MIGRATION_CHECKS PASS=$passes FAIL=0"
exit 0
