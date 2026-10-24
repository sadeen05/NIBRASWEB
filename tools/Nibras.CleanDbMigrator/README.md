# Nibras clean database migration runner

Tooling for the one authoritative clean database:

| Item | Value |
|---|---|
| Instance | `SADEEN\SQLEXPRESS` |
| Database | `NIBRAS_cleanvirsion` |
| Model | the reviewed **21** EF Core migrations |

This tool is **not** started by the web host and is **not** an API route. It never seeds, never
calls `EnsureCreated`, never rewrites migration history and never touches `NEBRASDB`,
`NIBRAS_F1_Verify`, `NEBRAS` or `NEBRASDB_Step1`.

## Safety properties

- **Fail-closed targeting.** `CleanDatabaseTargetGuard` accepts exactly
  `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`. Every other server or database name is refused,
  including `NEBRASDB`, `NIBRAS_F1_Verify`, `NEBRAS` and `NEBRASDB_Step1`. The guard parses the
  connection string before opening any connection.
- **Real identity check before DDL.** The runner connects to `master`, confirms `@@SERVERNAME`
  is the approved instance and reads `DB_ID`; when the database exists it connects to it and
  confirms `DB_NAME()`/`DB_ID()` before any DDL.
- **No silent overwrite.** An existing non-empty or partially migrated target is refused unless
  `--allow-existing` is passed under separate explicit approval.
- **EF command-by-command migration.** `Database.MigrateAsync()` applies the migrations in order
  and each migration in its own transaction. The combined `sqlcmd` script is **not** used,
  because several migrations add a column and reference it in the same batch (a script-only
  `Msg 207` defect; see `docs/CLEAN_DATABASE_PLAN.md`).
- **Verification.** After the run the applied history must equal the model and no migration may
  remain pending.
- **No seeding.** Development seeders and demo data are refused against the clean database by
  `DatabaseTargetGuard.RefuseSeedingOnProtectedDatabase`.

## Usage

```text
# Preferred: connection string from the dedicated environment variable.
set NIBRAS_CLEAN_DB_CONNECTION=Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True;

# Dry run (default): validates the target and prints the current state. No DDL.
dotnet run --project tools/Nibras.CleanDbMigrator

# Create and migrate (requires explicit approval for the target state):
dotnet run --project tools/Nibras.CleanDbMigrator -- --apply

# Explicit connection string:
dotnet run --project tools/Nibras.CleanDbMigrator -- --connection "<connection string>" --apply

# An existing non-empty/partially migrated target (separate approval only):
dotnet run --project tools/Nibras.CleanDbMigrator -- --apply --allow-existing
```

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success (or a validated dry run). |
| 1 | Unexpected failure. |
| 2 | Invalid configuration/target (missing or ambiguous connection string, wrong server/database). |
| 3 | Refused target state (existing non-empty/partially migrated database without `--allow-existing`). |
| 4 | Migration or verification failure. |

## Access required

The connection identity needs `dbcreator` (or `CONTROL SERVER`) to create the database, and
`db_owner` on `NIBRAS_cleanvirsion` while migrating. The application runtime login stays
least-privilege and must **not** hold DDL.

## Reference data and the policy decision

A fresh build contains only the migration reference data: `ROLES` (4), `GOVERNORATES` (12),
`DOCUMENT_TYPES` (`TitleDeed`, `EncumbranceStatement`), the unreachable
`system@nibras.internal` placeholder (`USERS.ID = -1`, hash `N/A`), and one illustrative
`LAND_CRITERIA` placeholder row.

> **Engineering-policy decision required.** The seeded `LAND_CRITERIA` values
> (`20 / 10 / 15 / 5 / NULL`, reason “ILLUSTRATIVE PLACEHOLDER …”) are **not** approved
> production policy. Before any real land evaluation, the engineering owner must record the
> approved eligibility values (or approve replacing/removing the placeholder through the
> application). This runner deliberately does not decide that policy.

The operator also needs to record whether the `USERS.ID = -1` placeholder and the placeholder
criterion are kept or removed before an acceptance run that depends on them.
