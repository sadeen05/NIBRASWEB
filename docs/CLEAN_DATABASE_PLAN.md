# NIBRAS clean database plan — `NIBRAS_cleanvirsion`

**Status: RESTORE / ACCOUNT-PROVISIONING PLAN — NOT EXECUTED.** No database was restored,
dropped, renamed, migrated or seeded in this preparation; no account was created or modified;
no application security setting was changed. Every execution step below requires separate
explicit approval.

**Date:** 24 October 2026
**Branch:** `step-7-hardening` (documentation-only edits in this preparation; no Git operation and
no source, configuration or database change)

> **Current state (24 October 2026).** `NIBRAS_cleanvirsion` exists as `DB_ID` 10 and contains
> synthetic TEST data (9 users, 3 lands, 7 land documents, 4 offers, 1 contract). The clean
> baseline is backup #6. A prior restore of that backup to
> `NIBRAS_restoreverify_b6_20261023_01` (`DB_ID` 11) verified 21 migrations, 24 application
> tables / 263 columns / 59 foreign keys / 98 indexes, the exact reference data, one
> non-operational placeholder user and zero business rows. The authoritative next action is
> **restore**, not create. The creation steps in §4 are retained only as a historical fallback.

> **Target protection (current code).** `NIBRAS_cleanvirsion` is registered as a
> production-like database in `DatabaseTargetGuard`. Every application start — including the
> bootstrap invocation — must set `Database:ApprovedProductionLikeTargets` to include the exact
> name `NIBRAS_cleanvirsion`; the application refuses the target by default.

> **Naming is authoritative:** the target database name is exactly **`NIBRAS_cleanvirsion`**
> (server `SADEEN\SQLEXPRESS`). The earlier working name `NIBRAS_CleanCandidate` is
> **superseded** and must not be used in scripts, configuration, migrations or tests. Every
> future script, connection string and acceptance test for the clean database must use the
> exact string `NIBRAS_cleanvirsion`.

---

## 1. Decision and goal

- All existing application business data is **experimental**. The current `NIBRAS_cleanvirsion`
  contains synthetic TEST data and must be replaced by the clean baseline.
- The new goal is a **clean, restored database** built from the reviewed **21-migration** model,
  with the exact reference data and exactly the two operational accounts required below.
- The migration history and the final EF model stay **exactly as they are** (21 migrations,
  ending at `20261023173135_HardenLandLocationUniqueness`). No new migration is proposed.
- The restore and account provisioning are **not executed** in this phase.

### Exact target

| Item | Value |
|---|---|
| SQL Server instance | `SADEEN\SQLEXPRESS` |
| Database name | **`NIBRAS_cleanvirsion`** |
| Current `DB_ID` | **10** (existing, `ONLINE`, `MULTI_USER`) |
| Current state | Contains synthetic TEST data; to be replaced by backup #6 |
| Baseline source | `NIBRAS_cleanvirsion_full_copyonly_20261023T194235Z_191388c9202241099aad75074ab2a437.bak` (backup #6) |
| Final migration count | **21** |
| Final migration | `20261023173135_HardenLandLocationUniqueness` |
| Collation | `SQL_Latin1_General_CP1_CI_AS` (server default) |
| Compatibility level | `160` |
| Recovery model | `SIMPLE` |
| Final operational accounts | **exactly one SuperAdmin** and **exactly one Admin** |
| Final non-operational placeholder | `system@nibras.internal` (`ID = -1`, hash `N/A`) — retained, cannot log in |
| Final operational Landlord/Investor accounts | **0** |
| Final lands / documents / offers / contracts | **0 / 0 / 0 / 0** |
| Final tokens / security events | No live verification token; only the expected bootstrap/Admin audit events |

### Preservation (no deletions or renames)

- `NIBRAS_cleanvirsion` (`DB_ID` 10) is the target and must not be dropped.
- `NIBRAS_restoreverify_b6_20261023_01` (`DB_ID` 11) is the verified copy of baseline #6 and is
  kept until the restored target passes the same checks; then it is a retirement candidate under
  separate approval.
- `NIBRAS_F1_Verify` (`DB_ID` 8) is preserved and remains the target of the retained F1 tests.
- `NEBRASDB`, `NEBRASDB_Step1`, `NEBRASDB_Step3_Test` and
  `NEBRASDB_UpgradeRehearsal_20261023` were dropped on 24 October 2026 under the separate,
  completed retirement approval; their copy-only backups are preserved.
- Backup #6 and all forensic evidence are preserved outside the repository and outside the
  delivery ZIP.

---

## 2. Phase 1 audit — findings

### 2.1 Migration inventory (21, in order)

```
20260721113459_InitialCreate
20260727183836_AddCancellationAndTariffSchema
20260801212633_DropOfferStatusTable
20260801220718_AddOfferSystem
20260801231103_DropOfferFinancialColumns
20260801231519_DropAssessmentColumns
20260801231939_DropRegulatoryTariffSettings
20260801235633_AddIsCurrentColumnToOfferVersion
20261010075624_Step1_FixBreakages
20261010170736_Step2_NewSchemaAndSeeds
20261011221505_Step5_OffersAdjustments
20261015140150_Step5_OffersActiveInvestorLandUnique
20261017140348_Phase1_GovernoratesAndLandAssessment
20261018090000_Phase2_LandCorrectionAndChangeRequests
20261019165813_EmailVerification
20261019174547_EmailVerificationUniqueness
20261021163426_AddLandDocumentStorageMetadata
20261021224950_AddGovernanceHistorySnapshots
20261022183848_AddContractVersionHistory
20261022191140_AddContractRevisionWorkflow
20261023173135_HardenLandLocationUniqueness
```

The final model (migration 21) is 24 application tables plus `__EFMigrationsHistory`
(25 total), with the previously reviewed column / index / foreign-key surface and the
null-safe land-location unique filters.

### 2.2 Confirmed SQL-batching defect (systemic)

**Root cause.** `dotnet ef migrations script` emits **one `GO` batch per migration**. Several
migrations `ALTER TABLE … ADD [column]` and then reference that same column **later in the same
batch** (index, foreign key, `UPDATE`, or data seed). SQL Server resolves column names at batch
compile time, so the whole batch fails with `Msg 207 Invalid column name` and the migration is
not applied. This is an artifact of the generated script; EF's own command-by-command executor
(which is how `NEBRASDB` reached 8 and `NIBRAS_F1_Verify` reached 20) executes each operation as
its own batch inside the migration transaction and is not affected.

**Direct rehearsal proof.** Applying the reviewed 8→21 script to the isolated rehearsal
database with `sqlcmd -I -b`:

- migration 9 (`20261010075624_Step1_FixBreakages`) committed;
- migration 10 (`20261010170736_Step2_NewSchemaAndSeeds`) failed:
  `Msg 207 Invalid column name 'IS_REQUIRED_FOR_VERIFICATION'` at the
  `INSERT INTO [DOCUMENT_TYPES] ([ID], [IS_REQUIRED_FOR_VERIFICATION], [NAME])` statement, and
  `Msg 207 Invalid column name 'VILLAGE' / 'BASIN' / 'PARCEL_NUMBER'` at
  `CREATE UNIQUE INDEX [UQ_LAND_LOCATION] ON [LANDS] ([REGION_ID], [VILLAGE], [BASIN], [PARCEL_NUMBER]) …`,
  all in the same batch as the `ALTER TABLE … ADD` statements.

**Affected migrations (confirmed or directly equivalent pattern), for a fresh 0→21 build:**

| Migration | Same-batch pattern |
|---|---|
| `20260727183836_AddCancellationAndTariffSchema` | adds `OFFERS.ACCEPTED_VERSION_ID`, `LANDS.VERIFIED_AGAINST_CRITERION_ID`, `CONTRACTS.CANCELLATION_REQUESTED_BY_ID`, `CONTRACTS.OFFER_VERSION_ID`; then creates indexes and foreign keys on them |
| `20260801212633_DropOfferStatusTable` | adds `LAND_STATUS_HISTORY.LandStatusId`; then `UPDATE … SET [LandStatusId]`, `CREATE INDEX`, foreign key |
| `20260801220718_AddOfferSystem` | adds `LANDS.GRID_ID`; then `UPDATE LANDS SET GRID_ID …`; adds `GRID_CAPACITY_RESERVATIONS.OFFER_ID` referenced later |
| `20260801235633_AddIsCurrentColumnToOfferVersion` | adds `OFFER_VERSIONS.IS_CURRENT`; then `UPDATE ov SET ov.IS_CURRENT = 1` |
| `20261010170736_Step2_NewSchemaAndSeeds` | adds `USERS.IS_APPROVED`/company columns, `LANDS.VILLAGE/BASIN/PARCEL_NUMBER`, `DOCUMENT_TYPES.IS_REQUIRED_FOR_VERIFICATION`, `LAND_DOCUMENTS.REVIEWED_BY_ID`, `CONTRACTS.CRITERION_ID`; then `INSERT`/`UPDATE`/index/foreign key |
| `20261017140348_Phase1_GovernoratesAndLandAssessment` | adds `LANDS.GOVERNORATE_ID`; then `CREATE UNIQUE INDEX [UQ_LAND_LOCATION_GOV] … ([GOVERNORATE_ID], …)` |
| `20261021224950_AddGovernanceHistorySnapshots` | adds `LAND_CRITERIA.PREVIOUS_CRITERION_ID`, `LAND_ASSESSMENTS.CRITERION_ID`; then indexes and foreign keys |

**Consequence for the clean build:** the generated `sqlcmd` script **must not** be used as-is for
a fresh 0→21 build. The batching fix is an **execution** fix; the migration source and the 21
migrations are not changed.

### 2.3 Seed / reference-data inventory (what a fresh DB will contain)

Seeded by the migrations (retained in the final model):

| Table | Rows | Rows seeded | Classification |
|---|---:|---|---|
| `ROLES` | 4 | `1 Landlord`, `2 Investor`, `3 Admin`, `4 SuperAdmin` | required reference |
| `GOVERNORATES` | 12 | `1 AM Amman` … `12 AT Tafilah` | required reference |
| `DOCUMENT_TYPES` | 2 | `1 TitleDeed (required)`, `2 EncumbranceStatement (required)` | required reference |
| `LAND_CRITERIA` | 1 | placeholder `20 / 10 / 15 / 5 / NULL`, reason “ILLUSTRATIVE PLACEHOLDER …”, `UPDATED_BY_ID = -1` | **decision required** |
| `USERS` | 1 | `ID = -1`, `system@nibras.internal`, hash `N/A`, role 4 | **decision required** |

Transient seeds, later dropped by the migration path and therefore **not** present in the final
model: `CONTRACT_STATUSES`, `LAND_STATUSES`, `OFFER_STATUSES`, `REGULATORY_TARIFF_SETTINGS`,
`TARIFF_BRACKETS`.

**Synthetic-business-data check (positive):** the base migrations seed **no** lands, land
documents, offers, offer versions, contracts, contract versions, land-status history, land
assessments or security events. `REGIONS` and `GRIDS` are empty; `LANDS.REGION_ID` / `GRID_ID`
are nullable after migration 13, so the migration itself does not require them.

**Two seeded rows need a decision (see §9):** the unreachable `system@nibras.internal`
placeholder user and the illustrative `LAND_CRITERIA` placeholder.

### 2.4 Application seeding and SuperAdmin bootstrap

- `SuperAdminSeeder` and `DemoDataSeeder` are **Development-only** and run only inside
  `if (app.Environment.IsDevelopment())` in `Program.cs`.
  - `DemoDataSeeder` additionally requires `Seed:DemoData` to be exactly `true` and
    `Seed:DemoPassword` to be present.
  - `SuperAdminSeeder` additionally requires `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword`.
- The production/clean provisioning path is the explicit out-of-band maintenance command
  `--bootstrap-superadmin --confirm <email>` (`Service/SuperAdminBootstrap.cs`):
  - refuses in Development (exit 5);
  - credentials come only from `Bootstrap:SuperAdminEmail` / `Bootstrap:SuperAdminPassword` at
    invocation time; the password is never printed or logged;
  - creates exactly one approved, email-verified SuperAdmin with the reserved phone
    `9999999999`, BCrypt work-factor-12 hash, and one `SuperAdminBootstrapped` audit event in
    one transaction;
  - is disabled after its first success because a login-capable SuperAdmin already exists;
  - the model placeholder (`hash N/A`) never counts as a SuperAdmin and never blocks the first
    real account.
- `ProductionConfigurationGuard` refuses `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword`
  outside Development, and refuses a normal serving start that still carries the bootstrap keys.
- The bootstrap invocation runs after the normal startup guards. It therefore also requires:
  - a non-Development environment (`ASPNETCORE_ENVIRONMENT=Staging` or
    `DOTNET_ENVIRONMENT=Staging`);
  - `ConnectionStrings:DefaultConnection` pointing at the exact target;
  - `Database:ApprovedProductionLikeTargets` containing `NIBRAS_cleanvirsion`;
  - explicit `AllowedHosts` (no wildcard);
  - a valid `Jwt:Key` (Base64, at least 32 bytes) and the configured issuer/audience;
  - an absolute `Documents:StorageRoot` outside `wwwroot`.
- The bootstrap invocation itself does **not** send mail and does not need a working SMTP
  endpoint. The subsequent Admin invitation/activation does.
- There is **no** `Database.Migrate()` / `EnsureCreated()` in the web host; migrations are applied
  only by an explicit, approved step.

### 2.5 `DatabaseTargetGuard` and target pinning

- `DatabaseTargetGuard` is **unchanged and not bypassed**.
- Always forbidden (no configuration can approve): `NEBRAS`, `NEBRASDB_Step1`.
- Production-like (denied unless explicitly listed in
  `Database:ApprovedProductionLikeTargets`): `NEBRASDB` **and `NIBRAS_cleanvirsion`**.
- `NIBRAS_cleanvirsion` is therefore **not** admitted by default. Every bootstrap invocation and
  every normal Staging start must set
  `Database__ApprovedProductionLikeTargets=NIBRAS_cleanvirsion` (or a comma/semicolon list that
  contains the exact name).
- `DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase` also refuses Development seed/demo data
  on `NIBRAS_cleanvirsion`.
- The clean-database migration runner's `CleanDatabaseTargetGuard` still independently pins the
  exact server/database; the application guard approval does not replace that pin.
- The existing suite resolver `docs/tests/scripts/test_target.ps1` is pinned to
  `NIBRAS_F1_Verify` and must **not** be repointed at the clean database. A separate clean-DB
  resolver/pin is required for clean-DB tests.

### 2.6 Restore preflight (read-only, passed 24 October 2026)

| Check | Result |
|---|---|
| `@@SERVERNAME` | `SADEEN\SQLEXPRESS` |
| SQL Server | Express 16.0.1000.6, compatibility `160`, server collation `SQL_Latin1_General_CP1_CI_AS` |
| Target identity | `NIBRAS_cleanvirsion`, `DB_ID` 10, `ONLINE`, `MULTI_USER`, `SIMPLE`, read/write, owner `SADEEN\sadoon`, created 2026-10-23 22:00:34 |
| Active sessions/requests on target | **0 / 0** |
| Baseline backup #6 | `backup_set_id` 6, copy-only full, `is_copy_only=1`, `has_backup_checksums=1`, `is_damaged=0`, 4,743,168 bytes, backup GUID `47BD9278-6161-4593-9B5C-6BCA2DD8C57D`, file present to SQL Server |
| Prior restore evidence | Backup #6 restored to `NIBRAS_restoreverify_b6_20261023_01` on 2026-10-23 23:00:09; 21 migrations; 24 app tables / 263 columns / 59 FKs / 98 indexes; 4 roles / 12 governorates / 2 document types / 1 land criterion; 1 placeholder user; 0 business rows |
| Current target data | 9 users (2 role 4 including the placeholder, 1 role 3, 6 role 1/2), 3 lands, 7 documents, 4 offers, 7 offer versions, 1 contract, 9 contract versions, 1 contract revision, 13 status-history rows, 2 assessments, 1 change request, 1 grid reservation, 12 negotiation-history rows, 44 security events, 10 verification tokens, 1 region, 1 grid — all synthetic TEST data to be replaced |
| Backup file mapping | Logical files `NIBRAS_cleanvirsion` / `NIBRAS_cleanvirsion_log`; physical paths match the current target, so no `MOVE` is required |
| Disk | C: 37,754 MB free; D: 951,952 MB free (enough for the safety backup and restore) |
| Permissions | Current login `SADEEN\sadoon` is `sysadmin` and `dbcreator`; target owner; SQL Server can see the backup path; the interactive Windows user cannot enumerate the backup directory (ACL), so a fresh SHA-256 of backup #6 was not captured in this preparation |
| Rollback | Take a fresh copy-only safety backup of the current target before restore; baseline #6 remains untouched; restore the safety backup if the baseline restore fails |
| Verdict | **PASS — restore ready**; no `RESTORE`, `DROP`, backup or account operation was executed in this preparation |

**Why the SHA-256 is not a blocker:** SQL Server records `has_backup_checksums=1` and
`is_damaged=0`, and backup #6 has already been restored successfully to `DB_ID` 11 with the
expected schema and baseline counts. The interactive-user ACL prevents a second file hash from
being taken without changing permissions; changing the backup-directory ACL is not authorized.

---

## 3. Required source / execution controls

1. **Restore path (current).** The authoritative path is a SQL Server `RESTORE` of baseline #6
   over the existing target, followed by the acceptance checks in §6 and the runbook in §11.
   The migration runner in §4.3 is retained only for the historical case where the target is
   lost and must be recreated from migrations; it is not part of the current restore approval.
2. **Clean-DB target pinning (existing).** `CleanDatabase.ServerName` /
   `CleanDatabase.DatabaseName` and `CleanDatabaseTargetGuard` are the dedicated pin for
   `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`, separate from the F1 resolver. Any new clean-DB
   test must use them. The application's `Database:ApprovedProductionLikeTargets` approval is
   deployment configuration, not a test pin.
3. **Readiness checklist count (completed).** `docs/PRODUCTION_READINESS_CHECKLIST.md` now
   states the expected **21** migrations; no code change.
4. **Clean-DB acceptance tests (proposed).** Add suites pinned to `NIBRAS_cleanvirsion`
   (schema, reference data, bootstrap, account provisioning, behavior, security). New tests only;
   do not modify the F1 suites or their `NIBRAS_F1_Verify` pin.
5. **Placeholder decisions (see §9).** The `system@nibras.internal` row and the
   `LAND_CRITERIA` placeholder need an explicit decision. If removal is chosen, it requires a
   new migration (history 22) or a deliberately approved post-migration data step; either
   changes the “21 migrations” guarantee and needs separate approval.

**No change is proposed** to: `DatabaseTargetGuard.cs`, the 21 migration files, the model
snapshot, `appsettings.json`, `Program.cs`, the bootstrap/activation code, or any shared
backend/frontend file.

---

## 4. Clean database creation and migration plan (historical fallback)

> **Do not run this section against the existing target.** `NIBRAS_cleanvirsion` already exists
> as `DB_ID` 10. The authoritative path is the restore and account-provisioning runbook in §11.
> This section applies only if the target is lost and must be recreated from migrations under a
> new, separate approval.

### 4.1 Step 0 — Preflight (read-only; approval gate)

1. Confirm `SELECT DB_ID(N'NIBRAS_cleanvirsion') IS NULL` (does not exist).
2. Confirm `@@SERVERNAME = 'SADEEN\SQLEXPRESS'`.
3. Confirm `NIBRAS_F1_Verify` exists and is unchanged (read-only fingerprint); the legacy
   `NEBRASDB` is already retired.
4. Confirm the operator identity has `CREATE DATABASE` rights (see §8).
5. Confirm no other session is writing to the target (none can exist before creation).
6. Confirm the backup destination path is writable by the SQL Server service account.
7. Confirm the reviewed migration set still hashes/compares as reviewed and that
   `dotnet ef migrations has-pending-model-changes` reports no pending model changes.

**Stop if any check fails.**

### 4.2 Step 1 — Create the empty database (separate approval)

Proposed T-SQL (reviewed, to be executed once, target-pinned):

```sql
IF DB_ID(N'NIBRAS_cleanvirsion') IS NOT NULL
    THROW 53000, 'NIBRAS_cleanvirsion already exists; refusing to overwrite.', 1;

CREATE DATABASE [NIBRAS_cleanvirsion]
    COLLATE SQL_Latin1_General_CP1_CI_AS;
ALTER DATABASE [NIBRAS_cleanvirsion] SET COMPATIBILITY_LEVEL = 160;
ALTER DATABASE [NIBRAS_cleanvirsion] SET RECOVERY SIMPLE;
```

Default file placement (explicit paths if required by policy):
`C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\NIBRAS_cleanvirsion.mdf`
and `…\NIBRAS_cleanvirsion_log.ldf`. No `RESTORE`, no `SELECT INTO`, no copy of any existing
database.

### 4.3 Step 2 — Apply the 21 migrations (target-pinned, command-by-command)

**Preferred: the existing reviewed `tools/Nibras.CleanDbMigrator` runner.** It is tooling only,
never started by the web host, never seeds, and fails closed. It:

1. reads the pinned connection string for `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion` from
   `--connection` or the dedicated `NIBRAS_CLEAN_DB_CONNECTION` environment variable (never
   `appsettings.json`);
2. validates it with `CleanDatabaseTargetGuard` and **fails closed** unless:
   server is `SADEEN\SQLEXPRESS` (or its `…\SQLEXPRESS` alias) **and**
   database is exactly `NIBRAS_cleanvirsion` (case-insensitive) **and**
   `!DatabaseTargetGuard.IsAlwaysForbidden(database)`.
3. opens one connection and re-asserts the real target with the read-only inspector
   (`@@SERVERNAME`, `DB_NAME()`, `DB_ID()`) before any DDL;
4. refuses an existing non-empty or partially migrated target unless `--allow-existing` is
   separately approved;
5. calls `NebrasdbContext.Database.MigrateAsync()` (EF executes each migration operation as its
   own command inside the migration transaction — no `sqlcmd` batching problem);
6. asserts the applied history equals the model, last =
   `20261023173135_HardenLandLocationUniqueness`, and no migration is pending;
7. stops on the first failure; never retries, repairs or rewrites history. Exit codes are
   `0` success, `1` failure, `2` invalid configuration/target, `3` refused target state,
   `4` migration failed.

**Alternative A: `dotnet ef`.** `dotnet ef database update` with `--connection` pointing at the
pinned target and a startup project that can build a working `DbContext`. This must be validated
offline first because the web host's guards/config and the test project's `ModelOnlyFactory`
(`NoConnections`) can interfere; the command-by-command semantics are the same.

**Alternative B: reviewed batch-separated script.** Generate the full 0→21 script and
post-process it so that every statement referencing a column added earlier in the same migration
runs in a **separate `GO` batch**, while the migration's `BEGIN TRANSACTION` / `COMMIT` still
wraps the whole migration (a transaction spans `GO` batches in one `sqlcmd` session). Run with
`sqlcmd -I -b`, target-pinned with independent identity/SET-option checks. This preserves
transaction semantics but is more fragile and must be reviewed line-by-line.

### 4.4 Step 3 — Post-migration assertions (read-only)

Run the full checklist in §6 against `NIBRAS_cleanvirsion`. On any failure: stop, do not repair
in place, and roll back by restoring the pre-migration state (a fresh empty DB can simply be
dropped and recreated under a new approval; a populated DB uses backup/restore).

### 4.5 Step 4 — First-time SuperAdmin bootstrap (historical creation path)

> This step is retained for the historical case where the target is recreated from migrations.
> For the current existing target, use the restore runbook in §11.3 and the bootstrap in §11.4.

```text
# Non-Development environment (Staging), bootstrap invocation only:
ASPNETCORE_ENVIRONMENT        = Staging
Bootstrap__SuperAdminEmail    = <operator-chosen private address>   # secret store / env at invocation time
Bootstrap__SuperAdminPassword = <high-entropy password>             # secret store / env at invocation time
dotnet NibrasWeb.dll --bootstrap-superadmin --confirm <the same address>

# Required serving configuration for the bootstrap invocation:
ConnectionStrings__DefaultConnection = Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True;
Database__ApprovedProductionLikeTargets = NIBRAS_cleanvirsion
AllowedHosts = <explicit host names, no wildcard>
Jwt__Key     = <base64, >= 32 bytes>
Jwt__Issuer  = nibras-api
Jwt__Audience = nibras-web
Documents__StorageRoot = <absolute path outside wwwroot>
```

Expected: exit code `0`; exactly one approved, email-verified SuperAdmin with phone
`9999999999` and a BCrypt-12 hash; exactly one `SuperAdminBootstrapped` audit event; the
`system@nibras.internal` placeholder untouched. Then remove the bootstrap keys immediately.
The bootstrap invocation does **not** send mail and does not require SMTP. The success log names
the SuperAdmin address; treat that log as private and never copy the address into shared evidence.
The password is never printed.

### 4.6 Step 5 — Backups and rollback controls (see §7)

Immediately after migration (before any application start) and again after a successful
bootstrap: create a `COPY_ONLY`, `CHECKSUM` backup and verify it with `RESTORE VERIFYONLY`.
Rollback is **restore-from-backup**; EF `Down` is not a rollback path.

---

## 5. Fresh-install reference-data and bootstrap strategy

### 5.1 Target pinning

Every clean-DB operation (migration runner, tests, backups) must independently assert:
server `SADEEN\SQLEXPRESS`, database exactly `NIBRAS_cleanvirsion`, not forbidden, and the real
`DB_NAME()`/`DB_ID()` at execution time. No fallback target, no shared-target opt-in.

### 5.2 Reference data (minimum for a working fresh install)

| Data | Source | Required? |
|---|---|---|
| `ROLES` (4) | migration 2 seed | yes |
| `GOVERNORATES` (12) | migration 13 seed | yes |
| `DOCUMENT_TYPES` `TitleDeed`, `EncumbranceStatement` | migration 10 seed | yes |
| `LAND_CRITERIA` (1 row) | migration 10 conditional placeholder | yes structurally; **values are placeholders** |
| `USERS.ID = -1` placeholder | migration 2 seed | structurally referenced by the criterion seed; **decision** |
| `REGIONS`, `GRIDS` | none | no (created by an admin later) |

### 5.3 No synthetic business data and exact account scope

- Do **not** run `DemoDataSeeder` (Development-only; keep `Seed:DemoData` unset/false and run in
  a non-Development environment).
- Do **not** run `SuperAdminSeeder` (Development-only; keep `Seed:SuperAdminEmail` /
  `Seed:SuperAdminPassword` unset).
- Do **not** import any rows from legacy backups, `NIBRAS_F1_Verify` or any other database. The
  clean DB is restored from backup #6, not copied table-by-table.
- Do **not** create Landlord or Investor accounts.
- After Gate B, the only operational accounts are the bootstrapped SuperAdmin and the Admin
  created by that SuperAdmin. The `system@nibras.internal` placeholder remains non-operational.
- The only business rows are **zero**: no lands, land documents, offers, offer versions,
  contracts, contract versions, contract revisions, land-status history, assessments, change
  requests, deletion requests, grid reservations or negotiation history. The only
  `USER_SECURITY_EVENTS` / `EMAIL_VERIFICATION_TOKENS` rows are the expected bootstrap and Admin
  activation records.

---

## 6. Test and acceptance checklist

All checks run **read-only** against `NIBRAS_cleanvirsion`, except the two approved account writes
in Gate B. Full behavioral, landlord/investor and frontend suites run on a **disposable clone** or
another explicitly approved test target, never in the final authoritative database. The final
database must contain no test Landlord/Investor users and no test business rows.

### 6.1 Migration and model parity

- [ ] `__EFMigrationsHistory` has exactly **21** rows; last is
      `20261023173135_HardenLandLocationUniqueness`.
- [ ] `dotnet ef migrations has-pending-model-changes` → none (offline, against the same model).
- [ ] `dotnet ef migrations list` shows no pending migration.
- [ ] No `LAND_STATUSES`, `CONTRACT_STATUSES`, `OFFER_STATUSES`,
      `REGULATORY_TARIFF_SETTINGS`, `TARIFF_BRACKETS`.
- [ ] `GOVERNORATES`, `LAND_ASSESSMENTS`, `LAND_CHANGE_REQUESTS`,
      `EMAIL_VERIFICATION_TOKENS`, `USER_SECURITY_EVENTS`, `CONTRACT_VERSIONS`,
      `CONTRACT_REVISIONS`, `CONTRACT_REVISION_CONSENTS`, `OFFER_NEGOTIATION_HISTORY` exist.

### 6.2 Schema surface

- [ ] Application table / column / index / foreign-key counts match the restored baseline:
      **24 application tables, 263 application columns, 59 foreign keys, 98 indexes** (plus
      `__EFMigrationsHistory`). Re-capture and compare to the model snapshot at restore time.
- [ ] `UQ_LAND_LOCATION` and `UQ_LAND_LOCATION_GOV` exist with the **null-safe** filters
      `… IS NOT NULL` for all four components.
- [ ] `UQ_LAND_NUMBER_GOV` and `UQ_LAND_NUMBER_REGION` exist.
- [ ] `UX_OFFER_VERSIONS_CURRENT_OFFER`, `UX_OFFERS_ACTIVE_INVESTOR_LAND`,
      `UX_CV_DRAFT_VERSION`, `UX_USERS_PENDING_EMAIL`,
      `UX_EMAIL_VERIFICATION_TOKENS_LIVE` exist with their filters.
- [ ] Key foreign keys resolve; no orphan/nulls where the model requires non-null.

### 6.3 Reference data and account scope (expected exactly)

- [ ] `ROLES` = 4 (`Landlord`, `Investor`, `Admin`, `SuperAdmin`).
- [ ] `GOVERNORATES` = 12.
- [ ] `DOCUMENT_TYPES` = 2 (`TitleDeed` id 1, `EncumbranceStatement` id 2, both required).
- [ ] `LAND_CRITERIA` = 1 (placeholder or the approved real values per §9).
- [ ] Immediately after restore: `USERS` = **1** — only the non-operational
      `system@nibras.internal` placeholder (`ID = -1`, role 4, `PASSWORD_HASH = 'N/A'`,
      `IS_APPROVED = 0`, `EMAIL_VERIFIED IS NULL`).
- [ ] After Gate B: `USERS` = **3** — the placeholder plus exactly one operational SuperAdmin
      and exactly one operational Admin. No other rows.
- [ ] Operational SuperAdmin count = **1** (`ROLE_ID = 4`, `IS_DELETED = 0`,
      `IS_APPROVED = 1`, `EMAIL_VERIFIED = 1`, real BCrypt hash, phone `9999999999`).
- [ ] Operational Admin count = **1** (`ROLE_ID = 3`, `IS_DELETED = 0`,
      `IS_APPROVED = 1`, `EMAIL_VERIFIED = 1`, real BCrypt-12 hash).
- [ ] Operational Landlord/Investor count = **0**.
- [ ] No `nibras.test.*`, `@example.invalid`, `f1race-*`, `DEMO-*` or fixture-prefixed user rows.

### 6.4 Zero synthetic business data

- [ ] Immediately after restore, `LANDS`, `LAND_DOCUMENTS`, `OFFERS`, `OFFER_VERSIONS`,
      `CONTRACTS`, `CONTRACT_VERSIONS`, `CONTRACT_REVISIONS`, `LAND_STATUS_HISTORY`,
      `LAND_ASSESSMENTS`, `LAND_CHANGE_REQUESTS`, `DELETION_REQUESTS`,
      `GRID_CAPACITY_RESERVATIONS`, `OFFER_NEGOTIATION_HISTORY`, `REGIONS`, `GRIDS` are all
      **0**.
- [ ] Immediately after restore, `USER_SECURITY_EVENTS` and `EMAIL_VERIFICATION_TOKENS` are
      **0**.
- [ ] After Gate B, the business tables remain **0**; the only new rows are the two accounts and
      their expected `USER_SECURITY_EVENTS` / `EMAIL_VERIFICATION_TOKENS` records.
- [ ] No test lands, offers, documents, contracts, change requests, assessments, reservations or
      negotiation rows exist.
- [ ] No `@example.test`, `@nibras.demo`, `f1race-*`, `DEMO-*` or fixture-prefixed rows exist.

### 6.5 SuperAdmin bootstrap acceptance (out-of-band command)

- [ ] First run exit `0`; exactly one login-capable SuperAdmin with the approved address,
      `ROLE_ID` = SuperAdmin, `IS_APPROVED=1`, `IS_DELETED=0`, `EMAIL_VERIFIED=1`, phone
      `9999999999`, BCrypt-12 hash; the placeholder is untouched.
- [ ] Exactly one `USER_SECURITY_EVENTS` row `SuperAdminBootstrapped`; no orphan event.
- [ ] No Admin row exists yet.
- [ ] Repeat run exit `3`; no second account/event.
- [ ] Missing/blank/mismatched `--confirm` exit `2` with no DB access.
- [ ] Development invocation exit `5`.
- [ ] The password never appears in stdout/stderr/logs and is not stored in any column.
- [ ] The success log's SuperAdmin address is treated as private and is not copied into shared
      evidence, chat or the repository.

### 6.6 Behavioral / API integration

- [ ] Application starts in a non-Development environment with the clean connection string and
      `Database__ApprovedProductionLikeTargets=NIBRAS_cleanvirsion`; startup logs the sanitized
      target `SADEEN\SQLEXPRESS/NIBRAS_cleanvirsion`.
- [ ] `GET /health/live` 200 `Healthy`; `GET /health/ready` 200 `Healthy`.
- [ ] Login as the bootstrapped SuperAdmin; SuperAdmin-only surfaces work.
- [ ] Gate B Admin creation/activation is covered by §6.9. On a disposable clone only:
      additional Admin/Landlord/Investor registration, email verification and approval flows.
      Do not create Landlord/Investor fixtures in the final authoritative DB.
- [ ] On a disposable clone only (never the final authoritative DB):
  - [ ] Land create → document upload → submit → admin verify → investor visibility (including
        the required-document rule and the null-safe location behavior).
  - [ ] Offer create → version → accept → grid reservation.
  - [ ] Contract create → version history → revision workflow (admin/SuperAdmin decisions).
  - [ ] Land status history and governance snapshots are written correctly.
- [ ] Authorization denials (role boundaries), rate limiting, host filtering, security headers.
- [ ] Swagger is **unavailable** outside Development.
- [ ] Migration history is unchanged after application start.

### 6.7 Frontend / API integration

- [ ] `frontend` builds (`npm ci` + `npm run build`) against the clean API base URL.
- [ ] Same-origin (or approved exact-origin) topology; no wildcard CORS.
- [ ] Login, lands, documents, offers, contracts, admin queues and language toggle exercised
      against a disposable clone of the clean database; the final authoritative DB remains at
      zero business rows. No fixture assumptions (`f1race-*`, `DEMO-*`) leak into the UI.
- [ ] CSP/security headers verified from the running frontend + API.

### 6.8 Official / protected databases unchanged

- [ ] `NIBRAS_F1_Verify` (`DB_ID` 8) is unchanged and still the target of the retained F1 tests.
- [ ] `NIBRAS_restoreverify_b6_20261023_01` (`DB_ID` 11) is unchanged and retained until the
      restored target passes the same checks.
- [ ] No access or change to `NEBRAS` (it does not exist) or to any dropped legacy database.
- [ ] `NEBRASDB`, `NEBRASDB_Step1`, `NEBRASDB_Step3_Test` and
      `NEBRASDB_UpgradeRehearsal_20261023` remain dropped; their backups remain as evidence.

### 6.9 Final exact-account and login acceptance (Gate B)

- [ ] SuperAdmin login `POST /api/auth/login` returns `200`; `GET /api/auth/me` returns role
      `SuperAdmin`, `isApproved=true`, `emailVerified=true`.
- [ ] Admin login returns `200`; `GET /api/auth/me` returns role `Admin`, `isApproved=true`,
      `emailVerified=true`.
- [ ] The Admin receives `403` on a SuperAdmin-only endpoint (for example
      `POST /api/users/{id}/admin-invitation`) and cannot create a `roleId = 3` account.
- [ ] The SuperAdmin can reach the SuperAdmin-only endpoint; the Admin cannot manage another
      Admin or a SuperAdmin through the API.
- [ ] No session is issued by activation; the Admin signs in afterwards with their own password.
- [ ] The final database matches §6.3 and §6.4: exactly one operational SuperAdmin, exactly one
      operational Admin, zero operational Landlord/Investor, zero business rows, 21 migrations
      and the exact reference data.
- [ ] No direct SQL insert/update of `USERS`, `ROLES` or `EMAIL_VERIFICATION_TOKENS` was used.

---

## 7. Backup, restore and rollback controls

- **Before restore:** take a fresh `COPY_ONLY`, `CHECKSUM` backup of the current
  `NIBRAS_cleanvirsion` (synthetic data) to a path outside the repository and outside the
  delivery ZIP:
  `BACKUP DATABASE [NIBRAS_cleanvirsion] TO DISK = N'<outside-repo>\NIBRAS_cleanvirsion_pre_restore_<utc>.bak'
  WITH COPY_ONLY, CHECKSUM, INIT, STATS = 5;`
  then `RESTORE VERIFYONLY FROM DISK = N'…' WITH CHECKSUM;` — expect “valid”.
- **Baseline restore:** restore backup #6 over the target:
  `RESTORE DATABASE [NIBRAS_cleanvirsion] FROM DISK = N'<baseline #6>' WITH REPLACE, RECOVERY,
  CHECKSUM, STATS = 5;`
  The backup's logical files and physical paths match the current target, so no `MOVE` is
  required. If the paths ever differ, add explicit `MOVE` clauses and review them first.
- **No forced single-user:** stop the application and verify zero sessions; do not use
  `SINGLE_USER WITH ROLLBACK IMMEDIATE` unless that specific action is separately approved.
- **After restore:** verify the 21 migrations, schema counts, reference data, one placeholder
  user and zero business rows (§6). Keep `NIBRAS_restoreverify_b6_20261023_01` until the same
  checks pass on the restored target.
- **After a successful bootstrap and again after final account acceptance:** take another
  `COPY_ONLY`, `CHECKSUM` backup and verify it.
- **Rollback = restore from backup.** Do **not** use EF `Down` (it can recreate weaker indexes
  or delete seeded rows). If the baseline restore fails, restore the pre-restore safety backup.
- Keep all clean-DB backups outside the repository and outside the delivery ZIP, protected by
  NTFS ACLs. Every destructive cleanup (drop of the clean DB, the restore-verification DB, or
  any backup) needs its own explicit approval.
- If a document storage root is used, back up the volume together with the database and test the
  restore of both.

---

## 8. Exact permissions required for the next phase

| Purpose | Identity | Required rights |
|---|---|---|
| Restore baseline over `NIBRAS_cleanvirsion` | dedicated restore identity (Windows `SADEEN\sadoon` integrated, or a SQL login) | `sysadmin` or `dbcreator` + `db_owner` on the target; `RESTORE`/`BACKUP` rights; no application runtime use |
| Take/verify backups | backup operator / restore identity | `db_backupoperator` on `NIBRAS_cleanvirsion` (and write access to the backup path through the SQL Server service account) |
| Run the bootstrap command | operator shell identity using the application runtime login | read/write on `NIBRAS_cleanvirsion` (`db_datareader` + `db_datawriter` + `EXECUTE`) plus the one-time `Bootstrap:*` environment variables; **no** `sysadmin` needed |
| Run the Staging application | least-privilege runtime login | `db_datareader`, `db_datawriter`, `EXECUTE` on `NIBRAS_cleanvirsion`; no DDL, no restore/backup rights |
| Create the Admin through the API | SuperAdmin JWT over the Staging host | no direct SQL; the existing `POST /api/users` authorization and activation flow |
| Deliver the activation invitation | Staging host | working SMTP endpoint, SMTP credentials from the secret store, HTTPS `Email:FrontendBaseUrl` |
| Read-only verification | any operator | `db_datareader` on `NIBRAS_cleanvirsion` |

The runtime login is deliberately **not** `sysadmin` or `dbcreator`; restore and creation use a
separate approved identity. No password or secret is stored in the repository, scripts or logs.
The bootstrap success log names the SuperAdmin address; treat that log as private.

---

## 9. Open decisions

1. **`system@nibras.internal` (`USERS.ID = -1`)**: (a) keep it as an unreachable, non-login
   system placeholder (recommended — it cannot authenticate, the bootstrap ignores it, and the
   placeholder criterion references it), or (b) remove it, which requires a new migration or an
   approved data step and changes the 21-migration guarantee. The exact-account requirement is
   satisfied by counting **operational** accounts only; the placeholder is not operational.
2. **`LAND_CRITERIA` placeholder**: (a) keep the illustrative row and replace it with approved
   engineering values through the application before any real use, (b) seed real values in a
   reviewed step, or (c) leave the criteria table empty and require the first administrator to
   create criteria before verification. (b)/(c) may require a migration/seed change.
3. **Retention of `NIBRAS_restoreverify_b6_20261023_01`**: keep until the restored target passes
   the same checks; then decide whether to retire it under separate approval.
4. **Retention of `NIBRAS_F1_Verify`**: preserved for the retained F1 tests; any eventual
   archive/drop needs separate approval and a harness repoint.
5. **Staging SMTP and HTTPS frontend**: must be supplied and tested before Gate B. This is the
   current account-provisioning blocker.
6. **Production promotion**: when `NIBRAS_cleanvirsion` becomes the authoritative database,
   decide whether to rename it to a production-like name; if renamed to `NEBRASDB`-like, the
   `Database:ApprovedProductionLikeTargets` configuration must be updated deliberately.

---

## 10. Exact next approvals required

**No database will be restored, dropped or modified, and no account will be created or modified,
without separate explicit approval.** The approvals are deliberately split.

### Gate A — baseline restore (technical preflight PASS)

Authorize only:

1. the fresh copy-only safety backup of the current `NIBRAS_cleanvirsion` and its
   `RESTORE VERIFYONLY`;
2. the `RESTORE DATABASE [NIBRAS_cleanvirsion] ... WITH REPLACE, RECOVERY, CHECKSUM` from
   backup #6;
3. the read-only post-restore acceptance checks in §6.1–§6.4 and the comparison with
   `NIBRAS_restoreverify_b6_20261023_01`;
4. keeping the safety backup and `NIBRAS_restoreverify_b6_20261023_01` until acceptance.

Gate A does **not** authorize account creation, bootstrap, SMTP changes or any drop.

### Gate B — account provisioning (currently BLOCKED)

After Gate A passes, authorize:

1. the one-time SuperAdmin bootstrap (§11.4);
2. the SuperAdmin-only `POST /api/users` creation of the single Admin;
3. the Admin activation through the HTTPS invitation link and
   `POST /api/auth/activate-admin`;
4. the read-only final account/login acceptance in §6.9.

Gate B is **BLOCKED** until Staging has a working SMTP endpoint and an HTTPS frontend URL that
resolves the activation page. The checked-in `appsettings.json` has
`Email:DeliveryMode = Disabled` and blank SMTP/frontend values; outside Development the host
refuses that configuration. The validation evidence records SMTP authentication and real email
delivery as **untested** and Mailtrap Sandbox delivery as **BLOCKED** (no operator credentials);
only the Development FileOutbox token path was used. Gate B must not start until a controlled
test message has been delivered and the activation page has been exercised.

Until both gates are approved and their acceptance checks pass, this phase remains **read-only**:
no restore, no drop, no account creation, no application security change, no Git mutation, and
`docs/tests/scripts/s4_preflight.ps1` is never run, modified or deleted.

---

## 11. Restore and post-restore account-provisioning runbook

**Status: prepared, not executed.** Gate A and Gate B are separate approvals. Nothing in this
section authorizes execution.

### 11.1 Readiness verdicts

| Gate | Verdict | Reason |
|---|---|---|
| A — baseline restore | **READY** (technical preflight PASS) | Backup #6 exists, is copy-only/checksummed, has a prior successful restore to `DB_ID` 11 with the expected schema and baseline counts; target has 0 sessions; disk is sufficient; SQL Server can read the baseline path; rollback is defined. The pre-restore safety backup is the first execution step and proves write access before the restore; if it fails, Gate A stops before touching the target. Execution is **not authorized**. |
| B — account provisioning | **BLOCKED** | No verified Staging SMTP endpoint and no HTTPS activation URL. The checked-in email configuration is `Disabled` with blank SMTP/from/frontend values; the evidence records SMTP authentication and real delivery as untested and Mailtrap Sandbox delivery as BLOCKED (only the Development FileOutbox path was used). The Admin invitation cannot be delivered, so the Admin would remain unverified and unable to log in. |

### 11.2 Required private configuration (never committed; secrets never printed)

| Key | Source | Notes |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | deployment | Must be `Staging` (non-Development) for bootstrap and serving |
| `ConnectionStrings__DefaultConnection` | secret store / environment | Exact target `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`; runtime login only |
| `Database__ApprovedProductionLikeTargets` | deployment | Must contain `NIBRAS_cleanvirsion`; the target is protected by default |
| `AllowedHosts` | deployment | Explicit Staging host names; no wildcard |
| `Jwt__Key` | secret store | Base64, at least 32 bytes; never in the repo or logs |
| `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes` | deployment | Must match the API/frontend contract |
| `Documents__StorageRoot` | deployment | Absolute path outside `wwwroot` and outside the repository |
| `ReverseProxy__Enabled`, `ReverseProxy__KnownProxies` / `KnownNetworks`, `ReverseProxy__ForwardLimit` | deployment | Only if HTTPS is terminated by a reverse proxy; list the exact trusted proxy, never a wildcard |
| `Email__DeliveryMode` | deployment | `Smtp` outside Development |
| `Email__SmtpHost`, `Email__SmtpPort`, `Email__SmtpEnableSsl` | secret store / deployment | Real Staging SMTP endpoint; port 587 and TLS unless the provider requires otherwise |
| `Email__FromAddress`, `Email__SmtpUser`, `Email__SmtpPassword` | secret store | Sender and credentials; never in the repo |
| `Email__FrontendBaseUrl` | deployment | HTTPS Staging frontend origin that serves `/activate-admin` |
| `Email__AdminActivationTokenTtlHours` | deployment | 1–168; choose a short, reviewed lifetime |
| `Bootstrap__SuperAdminEmail`, `Bootstrap__SuperAdminPassword` | secret store | Only for the bootstrap invocation; remove immediately after success |
| `Seed__*` | not set | Must remain unset; Development seeders are not used |

Private identities:

- SuperAdmin: a private operator-controlled address and a high-entropy password from the secret
  store. Never reuse an Admin password.
- Admin: a separate private address and phone. The SuperAdmin supplies no password for the
  Admin; the Admin sets it through the activation link.
- No shared, default, demo or fixture password is used.
- The bootstrap success log names the SuperAdmin address; keep that log private. The password,
  JWT, SMTP password and activation token are never printed.

### 11.3 Gate A — restore baseline #6 (not executed)

Preconditions:

- Written Gate A approval and a maintenance window.
- Application stopped; `sys.dm_exec_sessions` shows **0** sessions and **0** active requests on
  `NIBRAS_cleanvirsion`.
- Backup #6 path and metadata confirmed; prior restore evidence reviewed.
- Disk space confirmed; SQL Server can read the baseline backup path. Write access for the new
  safety backup is proven by step 1. If step 1 fails, stop before the restore; do not proceed
  without a verified safety backup.
- The operator understands that this replaces all current synthetic TEST data.

Steps:

1. Take a fresh safety backup of the current target and verify it:

   ```sql
   BACKUP DATABASE [NIBRAS_cleanvirsion]
   TO DISK = N'<outside-repo>\NIBRAS_cleanvirsion_pre_restore_<utc>.bak'
   WITH COPY_ONLY, CHECKSUM, INIT, STATS = 5;

   RESTORE VERIFYONLY
   FROM DISK = N'<outside-repo>\NIBRAS_cleanvirsion_pre_restore_<utc>.bak'
   WITH CHECKSUM;
   ```

   Record the backup-set id, size and timestamp. Do not overwrite backup #6.

2. Restore baseline #6 over the target:

   ```sql
   RESTORE DATABASE [NIBRAS_cleanvirsion]
   FROM DISK = N'C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\NIBRAS_cleanvirsion_full_copyonly_20261023T194235Z_191388c9202241099aad75074ab2a437.bak'
   WITH REPLACE, RECOVERY, CHECKSUM, STATS = 5;
   ```

   The backup's logical files (`NIBRAS_cleanvirsion`, `NIBRAS_cleanvirsion_log`) and physical
   paths match the current target, so no `MOVE` is required. Do not use
   `SINGLE_USER WITH ROLLBACK IMMEDIATE` unless separately approved; stop the application and
   wait for sessions to clear instead.

3. Verify the restored target, read-only:

   - `__EFMigrationsHistory` = 21; last `20261023173135_HardenLandLocationUniqueness`.
   - 24 application tables, 263 columns, 59 foreign keys, 98 indexes (plus history table).
   - `ROLES` = 4; `GOVERNORATES` = 12; `DOCUMENT_TYPES` = 2; `LAND_CRITERIA` = 1.
   - `USERS` = 1: only `system@nibras.internal` (`ID = -1`, role 4, hash `N/A`,
     `IS_APPROVED = 0`, `EMAIL_VERIFIED IS NULL`).
   - `LANDS`, `LAND_DOCUMENTS`, `OFFERS`, `OFFER_VERSIONS`, `CONTRACTS`,
     `CONTRACT_VERSIONS`, `CONTRACT_REVISIONS`, `LAND_STATUS_HISTORY`, `LAND_ASSESSMENTS`,
     `LAND_CHANGE_REQUESTS`, `DELETION_REQUESTS`, `GRID_CAPACITY_RESERVATIONS`,
     `OFFER_NEGOTIATION_HISTORY`, `USER_SECURITY_EVENTS`, `EMAIL_VERIFICATION_TOKENS`,
     `REGIONS`, `GRIDS` are all **0**.
   - The same checks pass on `NIBRAS_restoreverify_b6_20261023_01`.

4. If any check fails: stop. Restore the pre-restore safety backup with `REPLACE, RECOVERY,
   CHECKSUM`. Do not repair the restored database in place and do not retry blindly.

5. Keep the safety backup and `NIBRAS_restoreverify_b6_20261023_01`. Do not drop either without
   separate approval.

### 11.4 Gate B — bootstrap SuperAdmin (blocked until SMTP/HTTPS is ready)

Preconditions:

- Gate A passed and the restored baseline checks are recorded.
- Staging host can start in non-Development mode with the configuration in §11.2.
- A controlled SMTP test message has been delivered to a private mailbox; the activation page is
  reachable over HTTPS and points at the Staging API.
- Gate B approval is recorded; the private SuperAdmin identity is ready.

Steps:

1. Stop any serving process against the target. Confirm 0 sessions.
2. Set the bootstrap environment for one invocation only: `ASPNETCORE_ENVIRONMENT=Staging`,
   connection string, approved target, `AllowedHosts`, `Jwt__Key`, `Documents__StorageRoot`,
   `Bootstrap__SuperAdminEmail`, `Bootstrap__SuperAdminPassword`.
3. Run:

   ```text
   dotnet NibrasWeb.dll --bootstrap-superadmin --confirm <the same private email>
   ```

4. Expected: exit code `0`; exactly one approved, email-verified SuperAdmin with phone
   `9999999999`, BCrypt-12 hash, and one `SuperAdminBootstrapped` event. The placeholder is
   untouched. No Admin row exists yet.
5. Remove `Bootstrap__SuperAdminEmail` and `Bootstrap__SuperAdminPassword` immediately. Verify a
   normal serving start does not carry them.
6. Keep the bootstrap output private. The success log names the SuperAdmin address; do not copy
   it into shared evidence, chat or the repository. The password is never printed.

### 11.5 Gate B — create the single Admin and activate (blocked until SMTP/HTTPS is ready)

1. Start the Staging host with the §11.2 configuration. Confirm `GET /health/live` and
   `GET /health/ready` return `200 Healthy`. Do not run Development seeders.
2. Sign in as the SuperAdmin with `POST /api/auth/login` using the private address and password.
   Keep the returned JWT only in memory or a secure operator store; never print or commit it.
3. Create the Admin with `POST /api/users` and a SuperAdmin bearer token:

   ```json
   {
     "fullName": "<private Admin name>",
     "email": "<private Admin address>",
     "phone": "<unique private phone, not 9999999999>",
     "roleId": 3
   }
   ```

   Do **not** send a password. Expect `201 Created` with `invitation = "handed_over"`.
   If `invitation` is `"failed"`, the account exists but is unverified: fix SMTP and call
   `POST /api/users/{id}/admin-invitation` again. Do not create a second Admin.
4. Verify the pending Admin read-only: role 3, `IS_APPROVED = 1`, `IS_DELETED = 0`,
   `EMAIL_VERIFIED = 0`, unusable/random hash (not `N/A`), one `AdminAccountCreated` event and
   one live `AdminActivation` token. The SuperAdmin never knows the Admin password.
5. The Admin opens the HTTPS activation link from the private mailbox, chooses a password that
   meets the policy (10–128 characters, at most 72 UTF-8 bytes, no control characters), and
   submits `POST /api/auth/activate-admin` with the single-use token and password. Expect `204
   No Content`.
6. Verify the activated Admin read-only: `EMAIL_VERIFIED = 1`, `EMAIL_VERIFIED_AT` set,
   BCrypt-12 hash, `IS_APPROVED = 1`, token consumed, one `AdminActivated` and one
   `AdminPasswordSet` event, and no live activation token.
7. Sign in as the Admin with `POST /api/auth/login`; expect `200`. `GET /api/auth/me` must show
   role `Admin`, `isApproved = true`, `emailVerified = true`. Confirm a SuperAdmin-only endpoint
   returns `403` for the Admin and that the Admin cannot create `roleId = 3`.
8. Sign in as the SuperAdmin again and confirm the SuperAdmin-only endpoint works.

### 11.6 Final acceptance and evidence

- Final `USERS` = 3: one non-operational placeholder, one operational SuperAdmin, one
  operational Admin. No operational Landlord/Investor rows.
- Final role counts: role 4 = 2 rows (1 placeholder + 1 operational); role 3 = 1 operational;
  roles 1/2 = 0.
- Business tables remain zero; 21 migrations and reference data unchanged.
- Expected audit rows only: one `SuperAdminBootstrapped`, one `AdminAccountCreated`, one
  `VerificationEmailSent` for the successful invitation, one `AdminActivated`, one
  `AdminPasswordSet`; no live activation token. If a delivery retry was necessary, record the
  additional `VerificationEmailDeliveryFailed` / `VerificationEmailSent` rows and their reason.
- Record operator, approval reference, timestamps, exit codes, HTTP status codes and read-only
  database counts. Do not record passwords, tokens or full private addresses in shared evidence.
- Keep the baseline backup, the pre-restore safety backup and the post-bootstrap backup outside
  the repository. Keep `NIBRAS_restoreverify_b6_20261023_01` until final acceptance.
- No direct SQL user/role manipulation, no demo seeder, no security bypass, no Git mutation, and
  `docs/tests/scripts/s4_preflight.ps1` is never run, modified or deleted.

### 11.7 Failure handling

- **Restore failure:** restore the pre-restore safety backup; stop and investigate. Do not
  repair the restored database in place.
- **Bootstrap failure:** exit `2`/`3`/`4`/`5` means no new SuperAdmin. Fix the configuration or
  stop; do not retry with a different target or bypass the guard.
- **Invitation delivery failure:** the Admin remains unverified. Fix SMTP, then use the
  SuperAdmin-only resend endpoint, respecting its cooldown and daily cap. Do not create a second
  Admin and do not set a password by SQL.
- **Activation failure:** check expiry, supersession and policy; resend the invitation if
  allowed. Do not reset the password by SQL.
- **Wrong Admin address before activation:** correct the unverified account through the existing
  SuperAdmin `PUT /api/users/{id}` flow, then resend the invitation. No direct SQL.
- **Any account removal:** not part of this plan; it needs separate approval and a supported
  procedure.
