# NibrasWeb PowerShell test scripts

Portable copies of the Step-3/Step-4/Step-5 verification scripts. They contain
**no secrets** (SuperAdmin credentials are read at run time from user-secrets and
never printed) and **no machine-specific paths** (repo root is discovered by
walking up from the script to `NibrasWeb.csproj`; logs and scratch output go to
`%TEMP%`).

## Prerequisites

- A local SQL Server instance (the `$env:NIBRAS_SQL_SERVER` value, falling back to `.\SQLEXPRESS`) reachable with Windows auth
  (`Trusted_Connection=True`), database `NEBRASDB_Step1` already migrated/seeded.
- `sqlcmd` on `PATH` (used for DB assertions).
- .NET SDK (`dotnet` on `PATH`); app published/built so
  `bin\Debug\net10.0\NibrasWeb.dll` exists at the repo root.
- User secrets configured at the repo root
  (`dotnet user-secrets set "Seed:SuperAdminEmail" ...` / `Seed:SuperAdminPassword`).

## Environment variables

| Variable | Required | Meaning |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | optional | Overrides the DB connection string used by the app started via `s4_app.ps1`. Defaults to `Server=$env:NIBRAS_SQL_SERVER;Database=NEBRASDB_Step1;...`, where the server comes from `$env:NIBRAS_SQL_SERVER` (falling back to `.\SQLEXPRESS`) and the database remains `NEBRASDB_Step1`. |
| `RateLimit__AuthPerMinute` | set by `s4_app.ps1` | Auth rate limit for the app process. `Start-S4App -AuthPerMinute N` sets it. |
| `NIBRAS_TEST_PASSWORD` | optional | Shared password for script-created test users. If unset, each script generates a random one at run time. |
| `ASPNETCORE_URLS` / `ASPNETCORE_ENVIRONMENT` | set by `s4_app.ps1` | `http://localhost:5181`, `Development`. |

## Important rules

1. **One fresh app start per suite.** Each suite must run against a freshly
   started app (`Start-S4App` kills any existing listener on the port first), so
   the fixed-window rate limiter starts from a full window.
2. **Restart between suites** — do not run two suites back-to-back against the
   same app process; stop it (`Stop-S4App`) and start a new one.
3. Functional suites run with `-AuthPerMinute 1000` (never throttled). Only
   `step3_ratelimit.ps1` runs with `-AuthPerMinute 5` to exercise the 429
   boundary.
4. Scripts that need the seeded SuperAdmin read it from user-secrets at run time
   (`dotnet user-secrets list --project <repo>\NibrasWeb.csproj`). Credentials
   are never echoed, logged, or written to disk.
5. Test users/lands created by the scripts are disposable fixtures; the scripts
   also perform a small number of *approved* DB mutations (role change + restore
   in `step3_suite.ps1` Phase D) that they restore before exiting.

## Run order (A5 regression)

Build once first:

```powershell
dotnet build
```

Then, for **each** suite: start the app with the indicated limit, run the
script, stop the app.

```powershell
Set-Location <repo>
. .\docs\tests\scripts\s4_app.ps1

# 1) Step-3 functional suite (no rate-limit boundary here anymore)
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\step3_suite.ps1            # exit 0 = all pass
Stop-S4App

# 2) Step-4 main suite (includes the A2 admin cases S4-50..S4-63)
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_tests.ps1
Stop-S4App

# 3) Step-4 admin suite
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_admin.ps1
Stop-S4App

# 4) Step-4 regression suite
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_regression.ps1
Stop-S4App

# 5) Step-5 offers suite
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\s5_offers.ps1
Stop-S4App

# 6) Step-5 parallel approval race
Start-S4App -Port 5181 -AuthPerMinute 1000
& .\docs\tests\scripts\parallel-approval.ps1
Stop-S4App

# 7) Step-3 rate-limit boundary (SMALL limit, fresh window)
Start-S4App -Port 5181 -AuthPerMinute 5
& .\docs\tests\scripts\step3_ratelimit.ps1
Stop-S4App

# 8) Step-6 contracts suite (starts and stops its own app)
& .\docs\tests\scripts\s6_contracts.ps1

# 9) Step-7 status-filter regression (starts and stops its own app)
& .\docs\tests\scripts\s7_status_filter.ps1
```

`s4_tests.ps1` must run **before** `s4_admin.ps1`: it creates a criteria v2
with seed-equivalent thresholds; `s4_admin.ps1` then creates its own v2 and
asserts lands are evaluated against the newest version. All fixtures in every
suite pass under both the seed criteria and any v2 created by an earlier suite
(slope 5, area 25/100, grid 10 km, solar 6).

Every suite script exits non-zero when any case fails, so a wrapper can check
`$LASTEXITCODE` after each run.

## Script inventory

| Script | Purpose | Exit |
| --- | --- | --- |
| `s4_app.ps1` | Helper: `Start-S4App`/`Stop-S4App` (fresh start, kills port listener, sets env vars, waits for readiness). Dot-sourced by the suite scripts that manage their own app. | n/a |
| `step3_suite.ps1` | Step-3 non-privileged suite: auth policies, headers, registration/login, role change + restore (approved DB mutation). | 0/1 |
| `step3_ratelimit.ps1` | Dedicated auth rate-limit boundary (run with `-AuthPerMinute 5`): first N logins 401, N+1st 429, shared partition with register, non-auth endpoints unaffected. | 0/1 |
| `s4_tests.ps1` | Step-4 main suite (SEC checks S4-01..S4-63) including the A2 admin-block: disposable SuperAdmin-provisioned Admin drives verify/review/reject/criteria/document-type cases, then is deactivated (S4-63). | 0/1 |
| `s4_admin.ps1` | Step-4 admin suite: admin browse, D2/D1 lifecycle, versioned criteria v2, document-types guards. | 0/1 |
| `s4_regression.ps1` | Step-4 regression suite (R-01..R-45): boundaries, overflow, decimal precision, paging. | 0/1 |
| `s5_offers.ps1` | Step-5 offers suite: investor offers on Verified lands, offer lifecycle, contract guards. | 0/1 |
| `parallel-approval.ps1` | Step-5 parallel-approval race: two concurrent verify requests, exactly one wins. | 0/1 |
| `s6_contracts.ps1` | Step-6 contracts suite: create from an Accepted offer, signatures, review (approve/reject), termination, reservation release, and Step-7 competing-offer closure when a contract becomes Active. Starts/stops its own app. | 0/1 |
| `s7_status_filter.ps1` | Step-7 status-filter regression: undefined numeric (`?status=99`), unknown and injection status values return 400 (previously 200 / 500) for both lands and offers. Starts/stops its own app. | 0/1 |
| `test_fixtures.ps1` | Shared idempotent fixtures: ensures the two `TEST Region …` regions and the `TEST Grid - Primary 100MW` grid exist (insert-only, matched by name) and resolves their IDs. Dot-sourced by the suites; never assumes numeric IDs. | n/a |
| `s4_checkpoint_a.ps1` | Step-4 checkpoint evidence script (supplementary). | 0/1 |
| `s4_probe.ps1`, `s4_page.ps1`, `s4_overflow.ps1` | Supplementary Step-4 probes (each starts/stops its own app via `s4_app.ps1`). | 0/1 |
| `s4_review.ps1`, `s4_review2.ps1` | Supplementary Step-4 review-workflow evidence. | 0/1 |
| `s4_preflight.ps1` | **Pre-Step-4 artifact, kept for reference only — do not run in the regression pass.** It performs a one-off DB write (placeholder user + FK repoint) that Step 4 already consumed. | n/a |

## Notes

- `step3_suite.ps1` originally contained a Phase C rate-limit window test and
  two 62-second sleeps; both moved to `step3_ratelimit.ps1` (single fresh
  window, no long sleeps in the functional suite).
- Passwords in the scripts are either deliberately-wrong literals (negative
  tests) or runtime-generated (`T5t!N1` + random suffix). Set
  `NIBRAS_TEST_PASSWORD` to pin a value across suites.
- `C:\Windows\win.ini` / `C:\evil` appearing in `s4_regression.ps1` /
  `s4_tests.ps1` are **test payloads** for path-validation negative cases, not
  machine references.
