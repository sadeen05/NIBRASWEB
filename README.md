# NIBRAS

NIBRAS is a renewable-energy land marketplace. Landlords submit and document land,
Administrators assess and verify it, Investors make competing offers, and the winning offer
becomes a signed contract with a grid-capacity reservation.

The application is a single ASP.NET Core 10 backend (`net10.0`) and a React 19 / Vite 8
frontend. SQL Server is accessed through EF Core 10. The repository contains 21 ordered
migrations; the authoritative database is `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`.

## Repository layout

```text
NibrasWeb/
|-- Program.cs                 Host, authentication, rate limits, health checks, startup guards
|-- Controllers/               13 thin HTTP controllers
|-- Service/                   Business rules, email verification, storage, database guards
|-- Models/                    EF Core entities and NebrasdbContext
|-- DTOs/                      Request and response contracts
|-- Enums/                     Status, role and workflow constants
|-- Migrations/                21 EF Core migrations plus generated designers and snapshot
|-- DatabaseTargetGuard.cs     Fail-closed database-name guard
|-- CleanDatabaseTargetGuard.cs Strict guard for the clean database runner
|-- frontend/                  React application, API clients, routes, i18n and theme
|-- tools/Nibras.CleanDbMigrator/
|                              Guarded clean-database create/migrate/verify runner
|-- tests/NibrasWeb.F1.Preparation/
|                              Offline static checks plus separately gated SQL/fake-SMTP checks
|-- docs/                      Architecture, security, storage, database, test and delivery docs
`-- docs/api/swagger.json      Exported API contract
```

## Prerequisites

- .NET SDK 10.0.x.
- Node.js >= 22.12 and npm.
- SQL Server Express instance `SADEEN\SQLEXPRESS` (or the approved deployment equivalent).
- `sqlcmd` only when using the PowerShell database regression harness.
- A Base64 JWT signing key of at least 32 decoded bytes. Keep it outside the repository.

## Configuration

`appsettings.json` is a safe template: the connection string, JWT key, SMTP password,
document-storage root and frontend URL are empty placeholders. Do not commit real values.
The project already has a `UserSecretsId`, so user secrets can be set from the repository root:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=SADEEN\SQLEXPRESS;Database=<approved-database>;Integrated Security=True;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:Key" "<base64-key-at-least-32-bytes>"
dotnet user-secrets set "Database:ApprovedProductionLikeTargets" "NIBRAS_cleanvirsion"
dotnet user-secrets set "Documents:StorageRoot" "<absolute-private-path-outside-wwwroot>"
```

Environment variables with `__` are equivalent, for example
`ConnectionStrings__DefaultConnection` and `Jwt__Key`.

`NIBRAS_cleanvirsion` is protected. The application and the clean-database runner refuse it
unless the exact database name is explicitly approved in
`Database:ApprovedProductionLikeTargets`. The historical names `NEBRAS` and `NEBRASDB_Step1`
cannot be approved.

## Database and migration

The host never calls `Database.Migrate()` and never seeds automatically. Apply migrations before
starting the API.

### Clean or authoritative baseline

The supported runner for `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion` is:

```powershell
$env:NIBRAS_CLEAN_DB_CONNECTION = 'Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project tools/Nibras.CleanDbMigrator            # dry run; no DDL
# dotnet run --project tools/Nibras.CleanDbMigrator -- --apply  # requires explicit approval
```

It refuses any other target, applies the 21 migrations in order and verifies migration history.
It never seeds. An existing non-empty or partially migrated target requires `--allow-existing`
plus separate approval.

The authoritative database `NIBRAS_cleanvirsion` is currently the **post-Gate-B clean
baseline**: 3 users (1 non-operational placeholder, 1 operational SuperAdmin, 1 operational
Admin), 21 applied migrations and 0 business records (no lands, offers, contracts, change
requests or deletion requests). A full Landlord/Investor demonstration therefore needs a
disposable seeded copy or a separately approved data-creation step.

### Other approved databases

For a disposable or deployment database, point `ConnectionStrings:DefaultConnection` at an
approved target and use the standard EF command:

```powershell
dotnet ef database update --project NibrasWeb.csproj
```

Development seeders and demo data are allowed only on a disposable development database. They
are refused on `NIBRAS_cleanvirsion` and `NEBRASDB`.

## Start the backend

```powershell
dotnet restore
dotnet build NibrasWeb.slnx -c Release
dotnet run --project NibrasWeb.csproj
```

The Development launch profile listens on `http://localhost:5181`. Swagger is enabled only in
Development. A non-Development run requires explicit `AllowedHosts`, a JWT key, a private
document-storage root, SMTP configuration and the approved database target; see
`docs/PRODUCTION_READINESS_CHECKLIST.md`.

For a controlled demo against the clean database, use a non-Development environment so the
Development seeders are not invoked, and keep the database target explicitly approved.

## Start the frontend

```powershell
cd frontend
npm ci
npm run dev
```

Vite proxies `/api` to `http://localhost:5181`. The browser therefore stays same-origin; no CORS
policy is configured. Production build:

```powershell
npm run typecheck
npm run lint
npm run build
```

`frontend/dist/` and `node_modules/` are generated and are not part of the source delivery.

## Tests and static checks

```powershell
dotnet build NibrasWeb.slnx -c Release
dotnet build tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj -c Release
dotnet run --project tests\NibrasWeb.F1.Preparation\NibrasWeb.F1.Preparation.csproj -c Release --no-build -- --static
node tests\NibrasWeb.F1.Preparation\approvalTime.mock.mjs
node tests\NibrasWeb.F1.Preparation\devApiTarget.mock.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File docs\tests\scripts\static_contract_checks.ps1
```

The maintained PowerShell regression harness lives under `docs/tests/scripts/`. Its SQL modes
target `SADEEN\SQLEXPRESS / NIBRAS_F1_Verify` and require separate approval. The protected
`s4_preflight.ps1` is intentionally excluded from this delivery package and must never be run or
modified. Because the full `run_regression.ps1` manifest pins that protected file, full manifest
runs must be performed in the controlled source repository, not from this package.

## Security and delivery notes

- Authentication uses JWT bearer tokens; role and email-verification state are re-read from the
  database on every authenticated request.
- The UI guards are navigation aids only. Every API endpoint enforces its own authorization.
- Uploaded documents are private files outside `wwwroot`; production requires an absolute,
  writable, backed-up storage root.
- This package excludes `.git`, build output, IDE state, `node_modules`, `dist`, `App_Data`,
  databases, backups, raw evidence, logs, test uploads, user secrets and the protected
  `s4_preflight.ps1`.
- Historical evidence removed during the 24 October 2026 cleanup is retained outside the
  repository. The completed cleanup record and current file map are in
  `docs/NIBRAS_FILE_GUIDE.md`.

## Further documentation

- `docs/NIBRAS_FILE_GUIDE.md` - current file map, cleanup record and delivery map.
- `docs/PRODUCTION_READINESS_CHECKLIST.md` - deployment gates and known blockers.
- `docs/CLEAN_DATABASE_PLAN.md` - clean-database construction and guard policy.
- `docs/SECURITY_TRACEABILITY.md` - security requirements to controls and test evidence.
- `docs/EMAIL_VERIFICATION.md` - registration, verification and address-change flows.
- `docs/DOCUMENT_STORAGE.md` - private document storage and hardening.
- `docs/api/swagger.json` - exported API contract.
