# SuperAdmin bootstrap and operational health endpoints

Operator documentation for the one-time SuperAdmin bootstrap command and the
`/health/live` and `/health/ready` probes. Nothing in this document replaces the
release gates in `PRODUCTION_READINESS_CHECKLIST.md`; running the bootstrap
against a real deployment still requires its own written approval.

## 1. One-time SuperAdmin bootstrap

### What it is

`Service/SuperAdminBootstrap.cs` implements a **maintenance command**, not a
startup path and not an API route. The process is started by an operator with
`--bootstrap-superadmin`; the host never listens, never runs background
services and never seeds. The mechanism is disabled by construction after its
first success: every later invocation sees the login-capable SuperAdmin and
refuses.

### Configuration

| Key | Meaning | Where it must come from |
| --- | --- | --- |
| `Bootstrap:SuperAdminEmail` | The exact login address of the first SuperAdmin | secret store / environment variable at invocation time only |
| `Bootstrap:SuperAdminPassword` | The initial high-entropy password | secret store / environment variable at invocation time only |
| (no key) | The phone number is the reserved constant `9999999999` — identical to the Development seed identity | — |

Rules:

- A **normal** start — Development included — must not carry these keys. Outside
  Development the host refuses to boot when they are present
  (`ProductionConfigurationGuard.Validate`).
- A **Development** start of the bootstrap invocation is refused outright with
  exit code 5; Development provisions through its own seed configuration.
- The password follows the registration password rules (10–128 characters, the
  supported UTF-8 byte budget, no control characters) and is stored as a BCrypt
  work-factor-12 hash. It is never printed or logged.
- The invoked command requires `--confirm <email>` whose value repeats the exact
  configured address; a mismatch refuses before any database access.

### Invocation

```text
# Environment/secret store (never a repository file or shell history):
Bootstrap__SuperAdminEmail = <operator chosen address>
Bootstrap__SuperAdminPassword = <high entropy password from the secret manager>

# Command (Windows example):
dotnet NibrasWeb.dll --bootstrap-superadmin --confirm <the same address>
```

All other startup guards (`DatabaseTargetGuard`, `AllowedHosts`, the seed-refusal
rules and the JWT key validation) run exactly as on any other start; the
bootstrap never bypasses them.

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Success: exactly one SuperAdmin account was created and the audit event committed. The mechanism is now disabled for this database. |
| 1 | Database error while creating the account; nothing was committed. |
| 2 | Configuration invalid: credentials missing/blank/invalid, password rule failure, or a missing/mismatched `--confirm` value. No database access happened. |
| 3 | Disabled: a login-capable SuperAdmin already exists in the target database (including the account a previous successful run created). Nothing changed. |
| 4 | Target in use: the configured address or the reserved phone is owned by another account, or a concurrent writer won the race. Nothing changed. |
| 5 | Refused because the process was started with a Development environment. |

### Safety properties

- **Never automatic.** Only the explicit process argument selects the mode; the
  host never listens and no background service runs.
- **Transactional uniqueness.** The account and its
  `SuperAdminBootstrapped` audit event are written in one transaction. The
  SuperAdmin `ROLE` row is pinned with `UPDLOCK, HOLDLOCK`; two concurrent
  invocations are serialized at the database and the loser fails closed with
  exit code 3. The `USERS` unique indexes on address and phone remain the last
  resort.
- **No existing SuperAdmin is reused or changed.** The pre-check refuses when
  any non-deleted account with a real (non-placeholder) password hash holds the
  SuperAdmin role. The model-seeded placeholder `system@nibras.internal`
  (hash `N/A`) can never authenticate and never blocks the bootstrap.
- **No secrets printed.** Logs name the keys and the outcome categories only.

### After a successful run

1. Remove the bootstrap configuration from the environment immediately; the
   next normal start refuses to boot while it is still carried.
2. Verify read-only: exactly one approved SuperAdmin with the expected address,
   and one `SuperAdminBootstrapped` audit event.
3. Sign in once through the normal login endpoint, verify the SuperAdmin-only
   surface, then rotate the password through the approved procedure.
4. Record the operator, approval reference and timestamp.

## 2. Health endpoints

Two anonymous, minimal endpoints are mapped in `Program.cs`:

| Endpoint | Purpose | Answer |
| --- | --- | --- |
| `GET /health/live` | Liveness. Runs **no** dependency check: the process answering is the whole signal. | `200` with the body `Healthy` while the process runs |
| `GET /health/ready` | Readiness. Runs exactly the checks tagged `ready` (currently the bounded database check in `Service/DatabaseReadinessCheck.cs`). | `200` `Healthy` or `503` `Unhealthy` |

Properties:

- **Liveness never depends on the database or SMTP.** A stalled dependency
  turns the instance *not ready*, never *not alive* — a restart is the wrong
  reaction to a database outage.
- **The readiness check is bounded.** The database round trip must answer
  inside `Health:DatabaseTimeoutSeconds` (default 10, clamped to at least 1).
  The measured cold-start connection cost of roughly three seconds is inside
  the default; a stalled database answers 503 inside the window instead of
  holding the probe open.
- **No detail leaks.** The default health writer emits only the status text.
  The response never contains a connection string, server name, exception text
  or stack trace; the log records the failure category only.
- **No side effects.** The probe sends one `SELECT 1;` and never sends mail.
- **Authorization is not weakened.** Both endpoints are explicitly anonymous
  (`AllowAnonymous`) because a proxy must be able to reach them without
  credentials; every other endpoint still requires the authenticated fallback
  policy and its roles.
- **Proxy-friendly.** They are plain `GET` routes with stable status codes:
  200/503, `Content-Type: text/plain`. Wire the load balancer to pull an
  instance whose `ready` probe answers 503 and to use `live` for restart
  decisions only.

Configuration:

```json
"Health": { "DatabaseTimeoutSeconds": 10 }
```

## 3. Test evidence

- `tests/NibrasWeb.F1.Preparation` (`--static`) covers the bootstrap decision
  logic, the invocation parsing, the configuration-guard rules and the readiness
  outcomes with in-memory fakes (positive, negative, repeated-run and
  concurrent invocations; no second privileged account can ever be created).
- The bootstrap refusals were additionally exercised against the disposable
  database (`NIBRAS_F1_Verify`): an existing SuperAdmin refuses with exit code 3
  and no account, no audit row and no secret in the logs.
- The two endpoints were exercised against a live server: healthy database →
  `live` 200 and `ready` 200; unreachable database → `ready` 503 twice while
  `live` stayed 200, with no infrastructure detail in any response.

## 4. Bootstrap acceptance-test design (SQL, proposed - NOT approved and NOT executed)

The first-success path of the bootstrap cannot be exercised against a database that already
holds a login-capable SuperAdmin, because the mechanism is disabled by design the moment one
exists (exit code 3). The disposable acceptance target `NIBRAS_F1_Verify` holds several such
accounts and none of them may be deleted, changed or impersonated, so a genuine first-success
SQL acceptance test requires an **isolated, throw-away database** on the approved instance.
This section is the design of that test; it is **not** part of the current DB authorization and
must not be run without separate written approval for the temporary database's creation and
removal.

### Isolation contract (what makes it safe)

1. The test creates exactly one database named `NIBRAS_F1_Bootstrap_<utcstamp>` on the approved
   instance, from an empty schema (`dotnet ef database update` against a fresh database name).
   No `RESTORE`, no `SELECT INTO` and no copy of any existing database is used, so no existing
   row - SuperAdmin included - is ever read, copied or modified.
2. Every bootstrap invocation under test uses a connection string pointing at that temporary
   database only. The shared targets (`NIBRAS_F1_Verify`, and by rule `NEBRAS`, `NEBRASDB`,
   `NEBRASDB_Step1` and production) are never used as a bootstrap target.
3. Before and after the whole run the test records `COUNT_BIG(*)` and a checksum over
   `USERS(ID,EMAIL,PHONE,ROLE_ID,IS_DELETED)` and `USER_SECURITY_EVENTS(ID,USER_ID,EVENT_TYPE)`
   on `NIBRAS_F1_Verify`; both must be byte-identical, which is the positive proof that the
   existing SuperAdmin accounts were untouched.
4. After the assertions the temporary database is dropped with `DROP DATABASE` in the test's
   `finally` block (or manually if the run is interrupted). Nothing else is dropped.

### Cases

| # | Case | Setup | Expected | Assertions |
| - | ---- | ----- | -------- | ---------- |
| 1 | First success | Empty migrated DB, valid email/password, `--confirm` equal to the address | exit 0 | Exactly one `USERS` row with the SuperAdmin `ROLE_ID`; `EMAIL` = target; `PHONE` = `9999999999`; `IS_DELETED=0`; `IS_APPROVED=1`; `EMAIL_VERIFIED=1`; `PASSWORD_HASH` starts `$2` (BCrypt) and is **not** the password; `BCrypt.Verify(password, hash)` is true; exactly one `USER_SECURITY_EVENTS` row with `EVENT_TYPE='SuperAdminBootstrapped'`, `ACTOR_ID` = the new user, `DETAILS='bootstrap'`; no other `USERS`/audit rows were inserted |
| 2 | Idempotence | Repeat case 1 | exit 3 | Row counts and the audit row are unchanged from case 1 (no second privileged account, no second event) |
| 3 | Concurrency, distinct addresses | Two processes in parallel, same reserved phone, different emails | one exit 0, the loser in {3, 4} | Exactly one new SuperAdmin and exactly one `SuperAdminBootstrapped` event; no partial row and no orphan event from the loser; the loser's refusals are controlled (no unhandled exception text in its output) |
| 4 | Concurrency, same address | Two processes in parallel, identical email/phone | one exit 0, the loser in {3, 4} | Exactly one account for that address; the `USERS` email/phone unique indexes remain the final authority the loser hits |
| 5 | Credentials never persisted or echoed | Case 1 with a known password `P` | exit 0 | `P` appears in neither stdout nor stderr of the invocation; no column contains `P`; the stored value is a BCrypt hash |
| 6 | Target address in use | Pre-insert a non-SuperAdmin account owning the target email (in the temporary DB) | exit 4 | No SuperAdmin row and no audit event were added |
| 7 | Reserved phone in use | Pre-insert a non-SuperAdmin account owning `9999999999` | exit 4 | No SuperAdmin row and no audit event were added |
| 8 | Placeholder system account does not disable the mechanism | Migrated DB only (model-seeded `system@nibras.internal`, hash `N/A`) | exit 0 | The placeholder is untouched; the real SuperAdmin is created alongside it |
| 9 | Configuration invalid | Missing/blank email or password; password outside 10-128 / UTF-8 budget / control characters; missing or mismatched `--confirm` | exit 2 | No database access: `USERS`/audit counts identical to the setup snapshot |
| 10 | Development refused | `DOTNET_ENVIRONMENT=Development` with valid credentials | exit 5 | No database access, no account |
| 11 | Normal start carrying bootstrap keys outside Development | Serving start with `Bootstrap:SuperAdminEmail/Password` present | process refuses to boot | No account, no listener; the refusal is the existing `ProductionConfigurationGuard` behaviour |
| 12 | Rollback on cancellation | Cancel the invocation token after `SaveChangesAsync` but before `CommitAsync` | exit 1 (`database_error` category) | No `USERS` row and no audit row exist afterwards; the transaction rolled back |

### Notes and residual acceptance gaps

- Cases 1-8 are the only way to obtain **first-success** SQL evidence; the current disposable
  target can only ever produce cases 2 (exit 3 against its existing SuperAdmins), 6, 7, 9, 10 and
  11. Until the isolated database is approved, the first-success path stays covered by the
  in-memory fakes in `tests/NibrasWeb.F1.Preparation` (`SuperAdminBootstrapMocks.cs`), which
  emulate the exact SQL shapes (ROLE `UPDLOCK` subquery, `EXISTS` probes, identity assignment,
  serialized transactions) but are not a database.
- Case 12 documents a behaviour worth knowing rather than changing: a cancelled invocation is
  reported through the generic `database_error` category (exit 1) instead of a dedicated
  cancellation code. The transaction rolls back, no state is written, and the operator can
  simply rerun; no code change is proposed.
- The temporary database must be created and dropped by the operator (or by the test with an
  explicitly approved, separately privileged connection). The acceptance runner itself has no
  rights to create databases, which keeps the isolation contract enforceable by permissions.

