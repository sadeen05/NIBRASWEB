# NibrasWeb PowerShell test scripts

Portable copies of the Step-3/Step-4/Step-5 verification scripts. They contain
**no secrets** (SuperAdmin credentials are read at run time from user-secrets and
never printed) and **no machine-specific paths** (repo root is discovered by
walking up from the script to `NibrasWeb.csproj`; logs and scratch output go to
`%TEMP%`).

> **Delivery-package note:** the protected `s4_preflight.ps1` is intentionally omitted from
> `NIBRAS_Final_Clean.zip` and must never be copied, run or modified as part of a presentation.
> The full `run_regression.ps1` manifest pins that protected file, so complete manifest runs are
> performed in the controlled source repository, not from the delivery ZIP. The individual
> suites and offline checks remain available for review.

## Prerequisites

- The disposable SQL Server target `SADEEN\SQLEXPRESS` (or the local alias of the same instance) reachable with Windows auth
  (`Trusted_Connection=True`), database `NIBRAS_F1_Verify` already migrated/seeded. The harness refuses every other target.
- `appsettings.json` ships an empty `ConnectionStrings:DefaultConnection` placeholder and no server
  name, so no usable connection string is stored in the repository. The test harness (or the
  environment you run the app in) must supply the real target; `s4_app.ps1` sets
  `ConnectionStrings__DefaultConnection` unless the caller has already set it.
- `sqlcmd` on `PATH` (used for DB assertions).
- .NET SDK (`dotnet` on `PATH`); app published/built so
  `bin\Debug\net10.0\NibrasWeb.dll` exists at the repo root.
- User secrets configured at the repo root
  (`dotnet user-secrets set "Seed:SuperAdminEmail" ...` / `Seed:SuperAdminPassword`).

## Environment variables

| Variable | Required | Meaning |
| --- | --- | --- |
| `ConnectionStrings__DefaultConnection` | yes (supplied by the harness) | The DB connection string for the app started via `s4_app.ps1`. `appsettings.json` ships an empty `DefaultConnection` placeholder, so no usable value is stored in the repository and the harness supplies it. The suites are fail-closed to the disposable target `SADEEN\SQLEXPRESS / NIBRAS_F1_Verify`; any other database, including shared/production names, is refused. |
| `RateLimit__AuthPerMinute` | set by `s4_app.ps1` | Auth rate limit for the app process. `Start-S4App -AuthPerMinute N` sets it. |
| `NIBRAS_TEST_PASSWORD` | optional | Shared password for script-created test users. If unset, each script generates a random one at run time. |
| `ASPNETCORE_URLS` / `ASPNETCORE_ENVIRONMENT` | set by `s4_app.ps1` | `http://localhost:5199`, `Development`. |

## Important rules

1. **One fresh app start per suite.** Each suite must run against a freshly
   started app (`Start-S4App` refuses to start while the dedicated port is
   occupied), so the fixed-window rate limiter starts from a full window.
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
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\step3_suite.ps1            # exit 0 = all pass
Stop-S4App

# 2) Step-4 main suite (includes the A2 admin cases S4-50..S4-63)
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_tests.ps1
Stop-S4App

# 3) Step-4 admin suite
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_admin.ps1
Stop-S4App

# 4) Step-4 regression suite
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\s4_regression.ps1
Stop-S4App

# 5) Step-5 offers suite
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\s5_offers.ps1
Stop-S4App

# 6) Step-5 parallel approval race
Start-S4App -Port 5199 -AuthPerMinute 1000
& .\docs\tests\scripts\parallel-approval.ps1
Stop-S4App

# 7) Step-3 rate-limit boundary (SMALL limit, fresh window)
Start-S4App -Port 5199 -AuthPerMinute 5
& .\docs\tests\scripts\step3_ratelimit.ps1
Stop-S4App

# 8) Step-6 contracts suite (starts and stops its own app)
& .\docs\tests\scripts\s6_contracts.ps1

# 9) Step-7 status-filter regression (starts and stops its own app)
& .\docs\tests\scripts\s7_status_filter.ps1

# 10) Step-7 reservation surface (starts and stops its own app)
& .\docs\tests\scripts\s7_reservations.ps1
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
| `s7_reservations.ps1` | Step-7 reservation surface: the Admin `POST /api/GridCapacityReservations/{id}/release` route (authz, not-found, idempotent repeated release, capacity returned to the grid) and the removal of the direct reservation POST/PUT/DELETE routes (405). Also covers Decision C: manual release is refused with 409 while the linked contract is `PendingSignatures` or `Active`, and allowed when there is no contract or the contract has ended. Reservations are created only by workflow code. F1/F2 concurrency: overlapping releases keep the first release's audit trail, and a release racing a contract creation never leaves a hold both released and linked to a live contract. Does not exercise expiry. Starts/stops its own app. | 0/1 |
| `p2_change_requests.ps1` | **Phase-2 suite (Phase D only — requires the Phase-2 migration to be applied; the routes do not exist before it).** Change-request submit/decide (validation, ownership, SuperAdmin-only decisions, malformed stored snapshots, dangling pinned criteria), the immediate investor visibility restriction, evidence invalidation + SuperAdmin re-review, the correction-upload restriction, the hidden/outstanding offer gates, change-request submit/decision races, and the regression invariants. Performs explicitly-marked FIXTURE-LOCAL `UPDATE`s on rows it created in the same run only (malformed-snapshot and dangling-pin fault injection). Cases and expectations: `docs/tests/PHASE2_MATRIX.md`. | 0/1 |
| `test_fixtures.ps1` | Shared idempotent fixtures: ensures the two `TEST Region …` regions and the `TEST Grid - Primary 100MW` grid exist (insert-only, matched by name) and resolves their IDs. Dot-sourced by the suites; never assumes numeric IDs. It also resolves the governorate used by
  the land fixtures (`SELECT ID FROM GOVERNORATES WHERE CODE = ...`, a pure lookup: the twelve
  rows are seeded reference data and are never inserted by the fixtures). | n/a |
| `s4_checkpoint_a.ps1` | Step-4 checkpoint evidence script (supplementary). | 0/1 |
| `s4_probe.ps1`, `s4_page.ps1`, `s4_overflow.ps1` | Supplementary Step-4 probes (each starts/stops its own app via `s4_app.ps1`). | 0/1 |
| `s4_review.ps1`, `s4_review2.ps1` | Supplementary Step-4 review-workflow evidence. | 0/1 |
| `s4_preflight.ps1` | **Pre-Step-4 artifact, kept for reference only — do not run in the regression pass.** It performs a one-off DB write (placeholder user + FK repoint) that Step 4 already consumed. | n/a |

## Notes

- **Phase 1 land fixtures:** a landlord create body now carries only `governorateId`, the location
  identifiers and `areaDonum`. The region, the grid and the four technical measurements are
  recorded by `PUT /api/lands/{id}/assessment`, which is **SuperAdmin-only** (a regular Admin now
  receives 403, including during a rejection resubmission). Every suite
  that verifies a land therefore calls the assessment endpoint first; suites that only create
  lands just send `governorateId`. Verification of an unassessed land returns 409
  (`Technical assessment is incomplete`).

- **Phase 2 change requests:** `p2_change_requests.ps1` and the case matrix in
  `docs/tests/PHASE2_MATRIX.md` cover the approved-land change-request workflow,
  the `IS_HIDDEN_FROM_INVESTORS` flag and the derived evidence/visibility rules
  (offers and contracts are refused while a land is hidden or its required
  documents are being corrected). Run them only after
  `20261018090000_Phase2_LandCorrectionAndChangeRequests` is applied; nothing in
  the suite touches pre-existing business rows.
- `step3_suite.ps1` originally contained a Phase C rate-limit window test and
  two 62-second sleeps; both moved to `step3_ratelimit.ps1` (single fresh
  window, no long sleeps in the functional suite).
- Passwords in the scripts are either deliberately-wrong literals (negative
  tests) or runtime-generated (`T5t!N1` + random suffix). Set
  `NIBRAS_TEST_PASSWORD` to pin a value across suites.
- `C:\Windows\win.ini` / `C:\evil` appearing in `s4_regression.ps1` /
  `s4_tests.ps1` are **test payloads** for path-validation negative cases, not
  machine references.
