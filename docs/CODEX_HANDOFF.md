# NIBRAS â€” Codex handoff

**Generated:** 23 October 2026  â€¢  **Delivery update:** 24 October 2026
**Project:** `D:\tasks\try\NibrasWeb`
**Branch:** `step-7-hardening`  â€¢  **HEAD:** `5ea7e20b775da6681f3cd2c34ebf79d0846a001f`  â€¢  **Upstream:** `origin/step-7-hardening`
**Official database:** `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion` (`DB_ID = 10`)

> This document is informational. It changes no code, no database and no Git state. It is the
> only file created for this handoff. All existing uncommitted/untracked work is preserved.

---

## 1. Current architecture and code paths

| Layer | Technology | Paths |
|---|---|---|
| Backend API | ASP.NET Core, `net10.0`, minimal hosting | `Program.cs`, `Controllers/` |
| Backend services | C# services | `Service/` |
| Data model / EF Core | EF Core 10, SQL Server provider | `Models/NebrasdbContext.cs`, `Models/*` |
| Migrations (21) | EF Core migrations + snapshot | `Migrations/` |
| DTOs / Enums | C# | `DTOs/`, `Enums/` |
| Backend guard | Target guard | `DatabaseTargetGuard.cs`, `CleanDatabase.cs`, `CleanDatabaseTargetGuard.cs` |
| Clean-DB migration runner | Dedicated console tool | `tools/Nibras.CleanDbMigrator/` |
| Offline test/acceptance harness | Console app (excluded from web build) | `tests/NibrasWeb.F1.Preparation/` |
| Frontend | React 19 + TypeScript + Vite 8 + Ant Design 6 + react-i18next | `frontend/` |
| Frontend API clients | axios | `frontend/src/api/` |
| Frontend pages / auth / i18n | React | `frontend/src/pages/`, `frontend/src/auth/`, `frontend/src/i18n/` |
| Frontend router | react-router | `frontend/src/app/router.tsx` |
| Frontend dev proxy | Vite (`/api` â†’ backend `http://localhost:5181`) | `frontend/vite.config.ts` |
| Frontend build/typecheck/lint | npm scripts | `frontend/package.json` |

Backend HTTP endpoint (dev profile): `http://localhost:5181` (see `Properties/launchSettings.json`).
The frontend proxies `/api` so the browser stays same-origin (no CORS policy is configured).

---

## 2. Exact clean database state (verified read-only)

`SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`, `DB_ID = 10`, `ONLINE`, compatibility `160`,
recovery `SIMPLE`, collation `SQL_Latin1_General_CP1_CI_AS` (functional; `sys.databases.collation_name`
reads NULL from `master` because the database was created with the Express default `AUTO_CLOSE ON`).

Files:
- `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\NIBRAS_cleanvirsion.mdf`
- `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\DATA\NIBRAS_cleanvirsion_log.ldf`

Schema (matches the EF model, verified against an offline `dotnet ef dbcontext script`):
- **21** migrations; final `20261023173135_HardenLandLocationUniqueness`
- **24** application tables (`+ __EFMigrationsHistory`), **263** application columns, **59** foreign keys, **99** indexes (98 app + history PK)
- Null-safe filtered unique indexes `UQ_LAND_LOCATION` / `UQ_LAND_LOCATION_GOV` (`â€¦ IS NOT NULL` on all four location components)

Migration order:

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

Reference seeds only (no business data):

| Table | Rows | Content |
|---|---:|---|
| `ROLES` | 4 | Landlord, Investor, Admin, SuperAdmin |
| `GOVERNORATES` | 12 | Amman â€¦ Tafilah |
| `DOCUMENT_TYPES` | 2 | `TitleDeed` (id 1, required), `EncumbranceStatement` (id 2, required) |
| `USERS` | 1 | `ID = -1`, `system@nibras.internal`, role 4, hash `N/A`, `IS_APPROVED = 0` â€” unreachable system placeholder |
| `LAND_CRITERIA` | 1 | id 1, `20 / 10 / 15 / 5 / NULL`, illustrative placeholder reason, `UPDATED_BY_ID = -1` |

Zero rows in: `LANDS`, `LAND_DOCUMENTS`, `LAND_STATUS_HISTORY`, `LAND_ASSESSMENTS`,
`LAND_CHANGE_REQUESTS`, `OFFERS`, `OFFER_VERSIONS`, `OFFER_NEGOTIATION_HISTORY`, `CONTRACTS`,
`CONTRACT_VERSIONS`, `CONTRACT_REVISIONS`, `CONTRACT_REVISION_CONSENTS`, `CONTRACT_REVIEWS`,
`DELETION_REQUESTS`, `GRID_CAPACITY_RESERVATIONS`, `USER_SECURITY_EVENTS`,
`EMAIL_VERIFICATION_TOKENS`, `REGIONS`, `GRIDS`. Zero F1/fixture rows.

> **Engineering-policy decision pending:** the `LAND_CRITERIA` placeholder values are **not**
> approved production policy. The engineering owner must record the approved eligibility values
> (or approve replacing/removing the placeholder) before any real land evaluation.

---

## 3. Safety guards and protected databases

| Guard | File | Behaviour |
|---|---|---|
| Target guard | `DatabaseTargetGuard.cs` | Production-like set = `NEBRASDB` **and** `NIBRAS_cleanvirsion` (denied unless exactly listed in `Database:ApprovedProductionLikeTargets`). Always forbidden (no opt-in): `NEBRAS`, `NEBRASDB_Step1`. Parses the connection string; never opens a connection. |
| Clean-DB targeting | `CleanDatabaseTargetGuard.cs` | Accepts **only** `SADEEN\SQLEXPRESS` / `NIBRAS_cleanvirsion`; refuses every other server/database; used by the runner and tests. |
| Seeding refusal | `DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase` (called by `SuperAdminSeeder`, `DemoDataSeeder`) | Development seed/demo data cannot run against `NIBRAS_cleanvirsion` or `NEBRASDB`. |
| Production config | `Service/ProductionConfigurationGuard.cs` | Refuses missing/blank `AllowedHosts`, wildcards outside Development, Development-only seed credentials outside Development, and bootstrap credentials on a normal serving start. |
| Startup wiring | `Program.cs` | No `Database.Migrate()`; no auto-migration; Development seeders only in Development; bootstrap is an explicit out-of-band command. |

Protected / relevant databases: `NEBRASDB` and `NIBRAS_cleanvirsion` (production-like, approval
required for app use); `NEBRAS`, `NEBRASDB_Step1` (always forbidden); `NIBRAS_F1_Verify`
(disposable F1 test target, not guarded). `DatabaseTargetGuard` must never be weakened.

---

## 4. Git state (read-only snapshot)

- Branch `step-7-hardening`, HEAD `5ea7e20b775da6681f3cd2c34ebf79d0846a001f`, upstream `origin/step-7-hardening`.
- The worktree is intentionally dirty. All pre-existing uncommitted work is preserved.
- The four pre-existing obsolete files are **approved for deletion** and recorded in the Git
  history (24 October 2026 review, before staging): `IRepository.cs` and `Repository.cs` are an
  unreferenced generic repository wrapper superseded by the EF Core service layer,
  `Models/ContractStatusNames.cs` is superseded by the `Enums/ContractStatus.cs` enum, and
  `NibrasWeb.http` targets the removed `/weatherforecast/` scaffold route. None of them is
  referenced by any build, runtime or test path; `Repository.cs` also depends on
  `IRepository.cs`, so both are removed together. Their content stays available in the earlier
  commits.
- 24 October 2026 cleanup: the seven approved obsolete/historical items were archived outside the
  repository and removed. Two tracked deletions now appear: `01_LOGICAL_ANALYSIS.md` and
  `docs/tests/parallel-approval.ps1`. Five untracked items (two F1 reports, one one-off SQL file
  and two Phase 1 preparation directories) were removed. The modified root wrapper content is
  preserved in the external archive.
- Documentation delivery edits added `README.md`, updated stale links and refreshed
  `docs/NIBRAS_FILE_GUIDE.md`. Use `git status --porcelain` for the current authoritative file
  list; this document does not duplicate a moving status snapshot.
- No history rewrite was performed (no reset/clean/stash/rebase/commit/push). The approved commit
  groups are staged only; the commits themselves wait for separate approval, and the push for a
  further one.

---

## 5. Verified tests vs untested functionality

**Verified (offline, after the latest code changes):**

| Check | Result |
|---|---|
| `dotnet build NibrasWeb.csproj -c Release` | **PASS** â€” 0 warnings / 0 errors |
| `dotnet build tests/NibrasWeb.F1.Preparation/... -c Release` | **PASS** â€” 0/0 |
| `dotnet build tools/Nibras.CleanDbMigrator/... -c Release` | **PASS** â€” 0/0 |
| `dotnet run --project tests/... -c Release --no-build -- --static` | **PASS â€” 251 PASS / 0 FAIL** (clock, config, forwarded-header, `AllowedHosts`, JWT, DB-guard, clean-DB target guard, seeding refusal, 21-migration model set) |
| `dotnet ef migrations has-pending-model-changes` | **PASS** â€” â€œNo changes have been made to the model since the last migration.â€ |
| `dotnet ef migrations list` | **PASS** â€” 21 migrations, final `â€¦HardenLandLocationUniqueness` |
| `git diff --check` | **PASS** â€” exit 0 (CRLF advisories only) |
| Clean-DB runner dry run | **PASS** â€” detected absent target, no DDL |
| Clean-DB runner `--apply` | **PASS** â€” created `NIBRAS_cleanvirsion`, 21 migrations, DB_ID 10 |
| Clean-DB post-migration verification | **PASS** â€” 24/263/59/99 schema, exact seeds, zero business rows |
| `npm run typecheck` / `npm run lint` / `npm run build` | **PASS** - TypeScript, ESLint and Vite production build |
| `node tests/.../approvalTime.mock.mjs` | **PASS - 8 / 0** |
| `node tests/.../devApiTarget.mock.mjs` | **PASS - 44 / 0** |
| `docs/tests/scripts/static_contract_checks.ps1` | **PASS** - hard checks 1-2 and 6-7; 89 exported operations |

**Not yet verified / untested:**

- Full application startup outside Development against `NIBRAS_cleanvirsion` and a real HTTP smoke test.
- End-to-end API behavior on the clean DB: registration, login, email verification, admin activation, land lifecycle, document upload/download, offers, contracts/revisions, history, security denials.
- First-success SuperAdmin bootstrap (`--bootstrap-superadmin`) and its audit event.
- Frontend integration (build + same-origin run) against the clean API.
- SQL acceptance mode (`--sql-acceptance`) against a clean clone; `--fake-smtp`; focused document-storage suite.
- Clean-DB backup + restore test.
- HTTPS/HSTS, reverse proxy, real SMTP.
- The F1 suites remain pinned to `NIBRAS_F1_Verify` and have not been re-run.

---

## 6. Commands to build and test

```powershell
# Working directory: D:\tasks\try\NibrasWeb

# Backend, test harness, clean-DB runner (Release)
dotnet build NibrasWeb.csproj -c Release
dotnet build tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj -c Release
dotnet build tools\Nibras.CleanDbMigrator\Nibras.CleanDbMigrator.csproj -c Release

# Offline static/security checks (no database)
dotnet run --project tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj -c Release --no-build -- --static

# EF model checks (offline; ModelOnlyFactory + NoConnections)
dotnet ef migrations has-pending-model-changes --project NibrasWeb.csproj --startup-project tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj --no-build
dotnet ef migrations list --project NibrasWeb.csproj --startup-project tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj --no-build

# Clean-DB runner (dry run is the default; --apply is a database write and needs approval)
$env:NIBRAS_CLEAN_DB_CONNECTION = 'Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True'
dotnet run --project tools\Nibras.CleanDbMigrator -c Release --no-build              # dry run
# dotnet run --project tools\Nibras.CleanDbMigrator -c Release --no-build -- --apply # approved only

# Frontend
cd frontend
npm ci
npm run typecheck
npm run lint
npm run build
# npm run dev   # Vite dev server, proxies /api -> http://localhost:5181
```

---

## 7. Frontend/backend integration gaps

1. The app connection string is empty (`appsettings.json` `ConnectionStrings:DefaultConnection = ""`); it must be supplied at runtime.
2. `NIBRAS_cleanvirsion` is production-like, so it must be listed in `Database__ApprovedProductionLikeTargets` for the application to use it.
3. A non-Development profile still needs: `AllowedHosts`, `Jwt__Key` (Base64 â‰¥ 32 bytes), `Documents__StorageRoot`, `Email__*` (SMTP), `ReverseProxy:*` â€” see `docs/PRODUCTION_READINESS_CHECKLIST.md`.
4. No SuperAdmin can log in yet (only the unreachable `-1` placeholder exists); the bootstrap must run first.
5. The clean DB has no regions/grids/lands/offers/contracts; integration tests must create them at runtime (never seed the clean DB).
6. The frontend has no CORS policy; run same-origin (Vite proxy in dev, reverse proxy in production).
7. `AUTO_CLOSE ON` (Express default) on the clean DB; consider `ALTER DATABASE [NIBRAS_cleanvirsion] SET AUTO_CLOSE OFF` under separate approval.
8. Migration 10's generated `sqlcmd` script is not executable as a single batch; use the EF command-by-command runner (as already implemented).

---

## 8. SuperAdmin bootstrap procedure and restrictions

Command (out-of-band; the host never listens in this mode):

```text
# Non-Development environment only (Development is refused, exit 5).
Bootstrap__SuperAdminEmail    = <operator-chosen address>        # secret store / env at invocation only
Bootstrap__SuperAdminPassword = <high-entropy password>          # secret store / env at invocation only
dotnet NibrasWeb.dll --bootstrap-superadmin --confirm <the same address>

# Required serving config for the invocation:
ConnectionStrings__DefaultConnection = Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True;
Database__ApprovedProductionLikeTargets = NIBRAS_cleanvirsion
AllowedHosts = <explicit host names, no wildcard>
Jwt__Key     = <base64, >= 32 bytes>
```

Restrictions: refuses in Development (exit 5); requires `Bootstrap:*` only at invocation time; a
normal serving start carrying those keys is refused by `ProductionConfigurationGuard`; refuses if a
login-capable SuperAdmin already exists (exit 3) or if the target email/reserved phone `9999999999`
is in use (exit 4); creates exactly one approved, email-verified SuperAdmin with a BCrypt-12 hash
and one `SuperAdminBootstrapped` audit event in one transaction; remove the `Bootstrap:*` values
immediately after success. The `system@nibras.internal` placeholder never counts and is untouched.

---

## 9. Recommended isolated test-clone approach

Never run writing acceptance tests against `NIBRAS_cleanvirsion` directly.

1. Take a `COPY_ONLY`/`CHECKSUM` backup of `NIBRAS_cleanvirsion` (see Â§10).
2. Restore it to a **scratch clone** `NIBRAS_cleanvirsion_test_<utcstamp>` on the same instance
   (separate MDF/LDF; no `SELECT INTO`, no copy of any other database).
3. Create a fail-closed clone resolver/harness pinned to the exact clone name (mirror
   `CleanDatabaseTargetGuard` / `docs/tests/scripts/test_target.ps1`), and point the app and test
   suite at the clone only.
4. Run integration/behavior tests (registration, login, bootstrap, lands, documents, offers,
   contracts, history, security) against the clone; the authoritative clean DB stays read-only.
5. Drop the clone under separate approval; retain the backup and evidence.
6. Keep the F1 harness pinned to `NIBRAS_F1_Verify` (or repoint it to a dedicated clone) â€” do not
   point it at `NIBRAS_cleanvirsion`.

---

## 10. Required clean-DB backup and restore verification

1. Confirm 0 active sessions on `NIBRAS_cleanvirsion`.
2. `BACKUP DATABASE [NIBRAS_cleanvirsion] TO DISK = N'<path outside repo>\NIBRAS_cleanvirsion_pre_cleanup_<utc>.bak' WITH COPY_ONLY, CHECKSUM, INIT, STATS = 5;`
3. `RESTORE VERIFYONLY FROM DISK = N'â€¦' WITH CHECKSUM;` â†’ expect â€œvalidâ€.
4. Restore-test to a scratch DB (`MOVE` to temp files); verify 21 migrations, 24/263/59/99 schema,
   exact seeds, zero business rows; then drop the scratch DB under separate approval.
5. Record backup-set id, size, `RESTORE HEADERONLY`, and file SHA-256; store outside the repo/zip
   with NTFS ACLs. If document storage is later used, back up the volume too and test restoring both.

Existing backups that must be preserved:
`NEBRASDB_COPY_ONLY_20261023.bak` (5.96 MB, verified), `NEBRASDB_Step1_pre_phase2.bak` (8.84 MB),
`NEBRASDB_Step1_FULL_20261017_175104.bak` (8.34 MB), `NEBRASDB.bak` (6.14 MB, Jul 14).

---

## 11. Database baseline and retirement

| Database | DB_ID | Size | Migrations | Classification | Notes |
|---|---:|---:|---:|---|---|
| `NIBRAS_cleanvirsion` | 10 | 16 MB | 21 | **KEEP** | Target; restore baseline #6 after approval. Never drop. |
| `NIBRAS_restoreverify_b6_20261023_01` | 11 | 16 MB | 21 | **KEEP UNTIL ACCEPTANCE** | Verified restore of backup #6; retirement candidate after acceptance. |
| `NEBRASDB_Step3_Test` | 7 | 16 MB | 10 | **DROPPED 24 Oct 2026** | Test DB; retired under separate approval. |
| `NEBRASDB_UpgradeRehearsal_20261023` | 9 | 16 MB | 9 | **DROPPED 24 Oct 2026** | Experimental rehearsal; retired under separate approval. |
| `NEBRASDB` | 5 | 16 MB | 8 | **DROPPED 24 Oct 2026** | Old official experimental DB; verified backup preserved. |
| `NIBRAS_F1_Verify` | 8 | 80 MB | 20 | **KEEP** | Active F1 disposable target; harness must be repointed/retired before any drop. |
| `NEBRASDB_Step1` | 6 | 80 MB | 16 | **DROPPED 24 Oct 2026** | Retired under separate approval; backup preserved. |

`NEBRAS` does not exist. No cross-database dependencies, synonyms, Agent jobs, linked servers,
replication or snapshots. All remaining owners are `SADEEN\sadoon`; 0 active sessions everywhere.
Login `nibras_user` is unused (only `CONNECT SQL`, no DB mappings, not referenced in the repo).

Dependencies to repoint before dropping `NIBRAS_F1_Verify`: the F1 harness files
(`tests/NibrasWeb.F1.Preparation/SqlAcceptance.cs`, `TestEnvironment.cs`, `UserUpdateMocks.cs`;
`docs/tests/scripts/test_target.ps1`, `regression_manifest.psd1`, `f1_storage_*.ps1`).
Obsolete-but-preserve files referencing legacy names: `docs/tests/scripts/s4_preflight.ps1`
(**never run/modify**), `docs/tests/scripts/parallel-approval.ps1`, `docs/tests/PHASE2_MATRIX.md`.

---

## 12. Priority-ordered remaining tasks and required approvals

| # | Task | Approval required |
|---|---|---|
| 1 | Baseline backup #6 and restore-verification DB `NIBRAS_restoreverify_b6_20261023_01` completed and verified (21 migrations, baseline counts). | **Completed; retain both** |
| 2 | Non-Development profile + app pointed at the clean DB (`Database__ApprovedProductionLikeTargets`), including Staging SMTP/HTTPS | **Yes** â€” config/deploy |
| 3 | First-time SuperAdmin bootstrap on `NIBRAS_cleanvirsion`, then the single Admin through the API | **Yes** â€” bootstrap + `Bootstrap:*` secrets |
| 4 | Isolated backend/frontend integration tests on a clean clone | **Yes** â€” clone writes |
| 5 | Optional hardening `ALTER DATABASE [NIBRAS_cleanvirsion] SET AUTO_CLOSE OFF` | **Yes** |
| 6 | Decide engineering-policy `LAND_CRITERIA` values; decide `USERS.ID = -1` keep/remove | **Yes** â€” policy decision |
| 7 | Legacy DB retirement completed 24 Oct 2026 (`NEBRASDB`, `NEBRASDB_Step1`, `NEBRASDB_Step3_Test`, `NEBRASDB_UpgradeRehearsal_20261023` dropped; backups preserved). `NIBRAS_F1_Verify` remains for retained F1 tests. | **Completed; no further drop authorized** |
| 8 | Commit the reviewed uncommitted work | **Yes** â€” Git mutation (currently prohibited) |

Current readiness (24 Oct 2026): **RESTORE READY** (technical preflight PASS; execution not
authorized) and **ACCOUNT PROVISIONING BLOCKED** (no verified Staging SMTP endpoint or HTTPS
activation URL). See `docs/CLEAN_DATABASE_PLAN.md` §11 for the authoritative runbook.

No database writes/restores/creations/deletions/migrations, no bootstrap, no seeding, no source
edits and no Git mutations are authorized in this handoff itself.

---

## 13. Concise first-task prompt for Codex

```text
You are working in D:\tasks\try\NibrasWeb on branch step-7-hardening. The authoritative database is
SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion (DB_ID=10, 21 migrations). Do NOT write to it and do NOT touch
NEBRASDB, NEBRASDB_Step1, NIBRAS_F1_Verify or NEBRAS. Your task is preparation only:

1. Take a COPY_ONLY, CHECKSUM backup of NIBRAS_cleanvirsion to a path outside the repo, then run
   RESTORE VERIFYONLY WITH CHECKSUM and record the backup-set id, size, RESTORE HEADERONLY and the
   file SHA-256.
2. Restore that backup to a new scratch clone named NIBRAS_cleanvirsion_test_<utcstamp> on the same
   instance (separate files), and verify 21 migrations, 24 app tables, 263 columns, 59 FKs, 99 indexes,
   the exact reference seeds, and zero business rows.
3. Using the existing fail-closed pattern (CleanDatabaseTargetGuard / docs/tests/scripts/test_target.ps1),
   prepare an isolated integration-test harness pinned to the clone only; it must refuse the clean DB,
   NEBRASDB, NIBRAS_F1_Verify, NEBRAS and NEBRASDB_Step1.
4. Report exact identity checks, file paths, hashes, the clone verification results, and a test plan.
   Do not run writing tests, do not bootstrap, do not seed, do not migrate the clean DB, and do not
   modify DatabaseTargetGuard or s4_preflight.ps1. Request explicit approval before creating the clone.
```
