# Disposable test database — preflight plan (READ ONLY)

This document is a **plan**. Nothing in it has been executed: no database was created,
restored, reset or migrated, and no application was started. Every step below needs an
explicit approval before it is carried out.

## 1. Why a disposable database is required

The F1 suites create data: they register accounts, confirm addresses, activate
administrators, create lands, offers and contracts. The approved target `NEBRASDB_Step1`
holds the existing users, lands, offers and contracts of the project, and the F1 change makes
**every new account unverified** — so a suite that registers and signs in now has to confirm
the address first, and a suite that runs against the shared target also changes its content.

The disposable database therefore exists so that:

* the F1 suites can create data without touching `NEBRASDB_Step1`, and
* the behaviour can be exercised against a **fresh** database (no legacy `NULL` accounts
  except the seeded ones) as well as against the migrated one.

## 2. The exact target

| Item | Value |
|---|---|
| SQL Server | `SADEEN\SQLEXPRESS` (the same instance as the approved target) |
| Database name | **`NIBRAS_F1_Verify`** |
| Connection string | `Server=SADEEN\SQLEXPRESS;Database=NIBRAS_F1_Verify;Trusted_Connection=True;TrustServerCertificate=True;` |
| How the application is pointed at it | the environment variable `ConnectionStrings__DefaultConnection` (never a change in `appsettings.json`) |

**One resolver, fail closed.** The suites never name a database themselves: they resolve it
from the effective connection string through `docs/tests/scripts/test_target.ps1`
(`Get-TestTarget`). The resolver

* **refuses** a missing, ambiguous (a repeated or mixed database key) or unparsable
  connection string — there is **no fallback** to an existing database;
* **always refuses** `NEBRAS`, `NEBRASDB` and `NEBRASDB_Step1` — there is **no shared-target
  opt-in**;
* accepts **only** `NIBRAS_F1_Verify`, and only on the approved instance
  (`NIBRAS_TEST_SQL_SERVER`, default `$env:COMPUTERNAME\SQLEXPRESS`);
* is used by the application start helper (`s4_app.ps1`), by the fixture writer
  (`test_fixtures.ps1`) and by every `sqlcmd` call of the suites, so the app and the scripts
  can never disagree about the target.

The application's own `DatabaseTargetGuard` is **not** weakened and **not** bypassed: it
still denies the production-like name, and the resolver adds the script-side rules on top.

**Why this name is safe:** it is neither `NEBRASDB` (the guarded production-like name) nor
`NEBRASDB_Step1` (the approved target), and the `NIBRAS_` prefix makes the two families
impossible to confuse in a log line, a connection string or a screenshot.

## 3. Prerequisites (all need approval)

1. **A SQL login** that may create a database on the instance (Windows integrated
   authentication of an account in the `dbcreator` role, or a SQL login with
   `CONTROL SERVER`/`dbcreator`). The credentials never go into the repository, a script or a
   log; they are entered at the prompt or passed through `NIBRAS_SQL_SERVER` and the Windows
   authentication of the shell.
2. **The migrations** applied to the disposable database:
   `dotnet ef database update --connection "<the disposable connection string>"`. This creates
   the schema (16 migrations) — it is the only schema operation and it happens **only** on the
   disposable database.
3. **The seeders** run against the disposable database (the roles, the regions, the grids and
   the `system@nibras.internal` account) — the suites need the SuperAdmin and the reference
   data. The seed credentials come from the user secrets:
   `Seed:SuperAdminEmail` and `Seed:SuperAdminPassword` (the suite reports them as SKIP when
   they are missing).
4. **The instance configuration** for the suites:
   * `Email__DeliveryMode=FileOutbox` with `Email__OutboxPath` pointing at a **per-run**
     directory **outside any Git working tree** (the outbox holds live activation links). The
     helpers refuse a path that is inside a repository, and they fail closed when the
     repository check cannot be completed at all;
   * `Email__FrontendBaseUrl=http://localhost:5181` (the link is only used as a carrier for
     the token, the suites read the token out of the outbox file);
   * `RateLimit__AuthPerMinute=1000` **and** the raised `RateLimit__ActivationPerMinute`,
     `RateLimit__VerificationPerMinute` and `RateLimit__VerificationResendPerMinute` for the
     suites that assert many calls in a row. The F1 suite makes about five confirmation calls
     in a minute, and the production default of five per minute would throttle it and mask the
     single-use result — the suite detects a 429 and reports it as an environment problem;
   * `NIBRAS_EMAIL_OUTBOX` exported for the shell that runs the suites, pointing at the same
     directory.
5. **The application started by hand** on the disposable database for the duration of the run
   (`dotnet run --no-launch-profile`), with the seeders' forced off only where a suite needs
   the clean database.

## 3a. The pre-write safety gate — a harness-owned instance

**No suite may talk to an instance it did not start.** A local environment variable does not
prove what a running API uses, and a registration probe cannot be the primary protection: it
is a write, so it would happen *before* the target is known.

`docs/tests/scripts/test_harness.ps1` owns the instance:

1. the **approved SQL Server** is a constant in `test_target.ps1` (`SADEEN\SQLEXPRESS`); an
   environment variable can no longer point a run at another instance, and the suites take
   their `$sqlServer`/`$server` from the resolver;
2. the **exact database** is `NIBRAS_F1_Verify`; `NEBRAS`, `NEBRASDB` and `NEBRASDB_Step1` are
   always refused and there is **no shared-target opt-in**;
3. the **effective connection string** is set explicitly
   (`ConnectionStrings__DefaultConnection`) and is validated by the resolver before the
   process starts;
4. the instance listens on a **dedicated port** (5199, `NIBRAS_TEST_API_PORT`): a port that is
   already occupied is a **failure** — the harness never connects to a process it did not
   start, and it never kills a foreign listener;
5. the **launched process identity** is verified: the harness waits for the HTTP endpoint, then
   checks that the process it started is the one that **owns the port**; anything else stops
   the run;
6. the identity (PID, port, base URL, target, outbox) is written to a small file, and a suite
   calls `Initialize-TestApi` **before its first mutation**: it re-verifies that the recorded
   process is alive and still owns the port. That check needs **no write of its own**.

The **post-start registration probe** (`Assert-TestInstanceIdentity`) is **optional additional
evidence** only: it registers a disposable account through the public API and reads the row
back from the disposable database. It may only run *after* the gate has passed, and it never
decides whether the run may write.

**Every HTTP-writing suite owns its instance**: the nine that previously expected a running
instance now start the harness-owned one, and the six that started the app themselves go
through the same harness. The instance is stopped when the suite ends (and by a
`PowerShell.Exiting` handler, so a failed run leaves nothing behind), and the stop only ever
touches the PID the harness started and the per-run outbox directory it created.

## 3b. The credentials the suites may use

| Credential | Where it comes from | Used for |
|---|---|---|
| Windows authentication with `dbcreator` on the instance | the operator's own account | creating and dropping the disposable database |
| `NIBRAS_TEST_SUPERADMIN_EMAIL` / `NIBRAS_TEST_SUPERADMIN_PASSWORD` | the environment of the run, **disposable credentials only** | the SuperAdmin flows (the admin creation, the activation, the legacy account access). When they are absent, those cases are reported as **SKIP**; the suite never enumerates the user secrets and never reads the seed credentials from the repository. |
| `NIBRAS_TEST_PASSWORD` | the environment, otherwise a runtime-generated password | the accounts the suites create |
| `Jwt:Key` | the user secrets (the application reads it) | the application start (a new key is fine on the disposable database) |
| `Email__SmtpPassword` | **not needed** — the `FileOutbox` mode is used | the delivery |

None of them may be written into a file in the repository, a script or a log line.

## 3c. The disposable SuperAdmin identity — one stable account per approved run

The harness owns the seeded SuperAdmin identity, and it keeps **one** identity and password for
the whole run:

- `Get-TestRunCredentials` generates the pair **once** (a `f1-superadmin-<stamp>@example.test`
  address and a random password) and stores it **DPAPI-encrypted for the current Windows user**
  in `%TEMP%\nibras-f1-run-credentials.json` — outside the repository, never printed and never
  committed. The file is additionally restricted to that user (inheritance dropped, a single
  FullControl rule for the owner); when the restriction cannot be applied, the harness fails
  closed instead of leaving the secret readable;
- the envelope records the `RunId` and the **database** it belongs to, so it is never reused
  across unrelated runs: a file that belongs to another database, that cannot be decrypted, or
  that is not in the protected format (a plaintext store) is **refused** with an explicit
  message. `-NewRun` deliberately discards the previous identity and generates a new one;
- every start of the run (the Phase B identity probe and the Phase C acceptance run) passes the
  **same** pair to the instance as `Seed__SuperAdminEmail` / `Seed__SuperAdminPassword`, so the
  seeding is **idempotent across repeated starts of the same disposable database**;
- the credentials are **never rotated while a run is in progress**: the caller's shell cannot
  change them, and `Clear-TestRunCredentials` is the explicit end-of-run action that discards
  them so the *next* approved run seeds its own identity.

**The seeding fails closed.** `SuperAdminSeeder` is idempotent by the configured email and
**never** changes an email, a password, a role, a phone number or an approval state. It refuses
to continue, and stops the host, when

- the configured identity already belongs to an account that is **not** an approved, active
  SuperAdmin; or
- the reserved seed phone number (`9999999999`) is already owned by **another** account — the
  case that previously aborted the host with a raw unique-constraint violation.

The insert itself is one `SaveChangesAsync`, so the seeded account exists completely or not at
all.

## 3d. Recovery options for a disposable database whose seed identity is stale

A run that was interrupted **before** the credential lifecycle existed can leave a disposable
SuperAdmin whose password was generated and never kept. It is **never** reset, replaced or
overwritten automatically. The safe options, each needing explicit approval, are:

| Option | Effect | Notes |
|---|---|---|
| **A. Remove the stale seed row** (a single, scoped `DELETE` on `USERS` for that one disposable account, in the disposable database only) | The next approved run seeds its own identity and the full acceptance run, including the privileged cases, can execute | The only option that restores a complete run; the row is a disposable test account, never a shared or legacy one |
| **B. Keep the row and skip the privileged cases** | The non-privileged acceptance cases can run; the administrator activation, the SuperAdmin-only creation and the legacy-account checks stay **SKIP** | The stored password hash cannot be recovered, and the seeding refuses to touch the account |
| **C. Drop and recreate the disposable database** | A clean seed identity | **Forbidden by the standing rules** (no drop, no reset, no remigrate) |

The row is identified read-only before any action, and the statement is scoped to that exact
disposable account — never a shared, legacy or production account.

## 4. The operations that are allowed (each one separately approved)

| # | Operation | Target | Reversible |
|---|---|---|---|
| 1 | Create the database `NIBRAS_F1_Verify` | the instance | yes (drop) |
| 2 | Apply the migrations | `NIBRAS_F1_Verify` | yes (drop) |
| 3 | Run the seeders | `NIBRAS_F1_Verify` | yes (drop) |
| 4 | Run the F1 suites and the adapted suites | `NIBRAS_F1_Verify` | yes (drop) |
| 5 | Read the fingerprint of `NEBRASDB_Step1` (counts and checksums) | read-only | n/a |
| 6 | Drop `NIBRAS_F1_Verify` | the instance | n/a |

## 5. The operations that are forbidden

* touching `NEBRASDB` — the guarded production-like name — in any way, including a
  connection string, a restore or a script;
* restoring, resetting, truncating or migrating `NEBRASDB_Step1`;
* running the F1 suites against `NEBRASDB_Step1` (they create accounts and change the
  verification state of the data they create);
* `docs/tests/scripts/s4_preflight.ps1` (it is protected and its SHA-256 is pinned);
* any seeding or migration against a database whose name is not exactly
  `NIBRAS_F1_Verify`;
* storing a credential, a token or a connection string with a password in the repository, in a
  script or in a log.

## 6. The data-protection checks

**Before the run (read-only, on `NEBRASDB_Step1`):**

```sql
-- the row counts of the tables that matter
SELECT 'USERS' AS T, COUNT_BIG(*) FROM USERS
UNION ALL SELECT 'LANDS', COUNT_BIG(*) FROM LANDS
UNION ALL SELECT 'OFFERS', COUNT_BIG(*) FROM OFFERS
UNION ALL SELECT 'CONTRACTS', COUNT_BIG(*) FROM CONTRACTS
UNION ALL SELECT 'USER_SECURITY_EVENTS', COUNT_BIG(*) FROM USER_SECURITY_EVENTS;

-- a content fingerprint that a row-level change would move
SELECT CHECKSUM_AGG(BINARY_CHECKSUM(ID, EMAIL, EMAIL_VERIFIED, IS_DELETED)) FROM USERS;
```

**After the run:** the same two queries must return **exactly** the same values. A difference
is a hard stop: it means a run wrote into the approved target.

**Additional invariants to read (not to change):**

* `SELECT COUNT(*) FROM USERS WHERE EMAIL_VERIFIED IS NULL;` — the legacy accounts. The
  number may only stay the same or **grow** by the accounts a run creates; it must never
  shrink, and no legacy row may change its flag.
* `SELECT COUNT(*) FROM USERS WHERE EMAIL_VERIFIED = 0;` — the unverified accounts a run
  creates; they are disposable and disappear with the database.

## 7. The required credentials

| Credential | Where it comes from | Used for |
|---|---|---|
| Windows authentication with `dbcreator` on the instance | the operator's own account | creating and dropping the disposable database |
| `Seed:SuperAdminEmail` / `Seed:SuperAdminPassword` | the user secrets (`dotnet user-secrets`) | the SuperAdmin flows (the admin creation, the activation, the legacy account access) |
| `Jwt:Key` | the user secrets | the application start (a new key is fine on the disposable database) |
| `Email__SmtpPassword` | **not needed** — the `FileOutbox` mode is used | the delivery |

None of them may be written into a file in the repository, a script or a log line.

## 8. The suites and their order

1. `f1_email_verification.ps1` — the F1 acceptance (the onboarding, the gate, the token rules,
   the resend, the activation, the audit trail).
2. The adapted suites, in the order of the existing matrix:
   `step3_suite.ps1` · `s4_tests.ps1` · `s4_admin.ps1` · `s4_regression.ps1` ·
   `s4_review.ps1` · `s4_review2.ps1` · `s4_checkpoint_a.ps1` · `s5_offers.ps1` ·
   `s6_contracts.ps1` · `s7_reservations.ps1` · `p2_change_requests.ps1` ·
   `parallel-approval.ps1`.
3. The static checks (`static_contract_checks.ps1`) — they need no database.

The suites that assert a **SKIP** (a special configuration, a privileged credential) must be
run afterwards on a dedicated instance with that configuration; the SKIP lines in the output
are the record of what is still untested.

## 9. The end of the run

* stop the instance;
* read the fingerprint of `NEBRASDB_Step1` again and compare it with the value from §6;
* **delete the per-run outbox directory** — it holds the live verification and activation
  links. Only that directory is removed, never a parent directory and never a path that was not
  created for this run; the helpers themselves never delete anything;
* drop `NIBRAS_F1_Verify`;
* report the suite results, the SKIPs and the fingerprint comparison.

## 10. The STOP conditions

Any one of them stops the run; nothing is retried and nothing is "fixed" in passing:

1. the resolver refuses the target (missing, ambiguous, `NEBRAS`, `NEBRASDB`,
   `NEBRASDB_Step1`, a foreign instance, or a database that is not `NIBRAS_F1_Verify`);
2. the **pre-write gate** fails: the recorded harness process is gone, or the dedicated port is
   owned by another process, or no harness identity exists — the suite would write through an
   instance it does not own;
3. the **harness cannot start** the instance: the dedicated port is occupied, the process exits,
   the endpoint does not answer, or the process that owns the port is not the one the harness
   started;
4. the fingerprint of `NEBRASDB_Step1` differs before or after the run;
5. the outbox path is inside the repository, or the repository check cannot be completed;
6. the delivery mode is not `FileOutbox` (the verification flow cannot be completed), or the
   rate limits are not raised (a 429 would mask the single-use result);
7. a legacy `NULL` account changes;
8. a `ROLE_ID` restore fails (the `step3_suite` mutation) — the disposable database is the
   only acceptable place for that test;
9. the privileged credentials are missing — only the privileged cases are skipped, the rest
   still runs;
10. the concurrency suites that still pass a **session JWT** through a PowerShell job
    (`parallel-approval.ps1`, `s5_offers.ps1`, `s6_contracts.ps1`, `s7_reservations.ps1`) are
    not yet converted to a token-free form — see §11.

## 11. The separate approvals

| # | Approval | What it covers |
|---|---|---|
| A | create `NIBRAS_F1_Verify`, apply the migrations, run the seeders **on it** | the only database that may be created, migrated or seeded |
| B | the environment of the run | `ConnectionStrings__DefaultConnection` (the disposable database), `NIBRAS_EMAIL_OUTBOX` (a per-run directory outside the repository), `NIBRAS_TEST_SUPERADMIN_EMAIL` / `NIBRAS_TEST_SUPERADMIN_PASSWORD` (disposable credentials), the raised rate limits, `Email__DeliveryMode=FileOutbox` |
| C | the F1 acceptance run | `f1_email_verification.ps1` alone, first |
| D | the regression run | the twelve adapted suites, in the order of §8 |
| E | the special configurations | the delivery-failure, expiry and daily-cap SKIPs, each on its own instance |
| F | the commit of the test preparation | the twenty changed files, once the runs are green |

**The conversion of the JWT-carrying job sites (§11.9) is a separate, small change**: the
suites `parallel-approval.ps1`, `s5_offers.ps1`, `s6_contracts.ps1` and `s7_reservations.ps1`
pass a session JWT as a `Start-Job` argument, and Windows PowerShell 5.1 serialises the job
state (the argument included) into a temporary file. The F1 verification tokens no longer
travel that way. Converting the JWT sites means touching the concurrency mechanics of four
suites, whose assertions are exactly what those suites exist for, so it is done as its own
reviewed change — the token is handed to the child process through the environment (which is
inherited, not serialised) instead of through the argument list.
