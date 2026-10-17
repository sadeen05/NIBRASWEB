# NIBRAS — Security traceability matrix (SEC-01 … SEC-20)

Specification: [`docs/05_SECURITY_REQUIREMENTS.md`](05_SECURITY_REQUIREMENTS.md) — *NIBRAS — Security
Requirements (v1, 9 Oct 2026)*, copied verbatim from the reviewer's document
(`SHA-256 2C8A9DA499A927EB6376F976B883938FE0215CB712384E8065BC27BD7D849617`, LF, 10 839 bytes).

Backend: ASP.NET Core (.NET 10) + EF Core 10 + SQL Server. Frontend: React 19 + Vite 8.

## How to read the status column

| Status | Meaning |
| --- | --- |
| **VERIFIED NOW** | Re-checked in the current session by executing something; the evidence is cited. |
| **VERIFIED (prior step)** | Owned and evidenced by the step suite that implemented it (Step 3–6); not re-run here because re-running it requires database writes that are not currently approved. |
| **STATIC** | Verified by reading the code/configuration. A dynamic confirmation exists but needs an app + database run. |
| **REQUIRES DECISION / BLOCKED** | Needs a ruling or an approval gate; see §3. |

## 1. Matrix

| ID | Requirement (condensed) | Implementation | Evidence | Status |
| --- | --- | --- | --- | --- |
| SEC-01 | Fallback policy = authenticated; anonymous only register + login | `Program.cs:102-107`; `AuthController.cs:22,37` | Live `swagger.json` sweep: 63 operations, 62 → 401, only `POST /api/auth/register` and `POST /api/auth/login` reachable (§2.1) | **VERIFIED NOW** |
| SEC-02 | JWT validation (signature/issuer/audience/lifetime), alg pinned to HS256, ClockSkew ≤ 1 min, key ≥ 32 bytes from user-secrets, header only | `Program.cs:17-36,66-77`; `AuthService.cs:92-98` | Tampered signature / `alg:none` / query-string token / malformed header → 401 (§2.3). Expiry + issuer/audience negatives: `step3_suite.ps1` | **VERIFIED NOW** (4/6 dynamic; expiry & issuer/audience prior-step) |
| SEC-03 | User must exist, not soft-deleted, DB role == role claim, else 401 | `Program.cs:78-100` (`OnTokenValidated`, `IgnoreQueryFilters`, `IsDeleted`, role equality) | `step3_suite.ps1` Phase D (deactivate user → old token 401; DB role change → old token 401) | VERIFIED (prior step) / STATIC |
| SEC-04 | No cookies / no server session; fresh token every login; `Jwt:ExpiryMinutes = 120` | `Program.cs:60-101` (`SaveToken=false`); `appsettings.json` `Jwt.ExpiryMinutes=120`; `AuthService.cs:125-126` | No `AddSession`/cookie scheme anywhere; no `Set-Cookie` on any of the 63 responses (§2.1); expiry read from config | **VERIFIED NOW** (cookies/session) + STATIC (fresh-token-per-login needs a real login) |
| SEC-05 | Login hardening: BCrypt ≥ 11, generic message + dummy-hash timing, password ≥ 10, rate limiter on login+register via `RateLimit:AuthPerMinute` → 429 | `AuthService.cs:13,74-83,52`; `PasswordPolicy.cs`; `Program.cs:39-52`; `appsettings.json:17` | `step3_ratelimit.ps1` (6th attempt → 429 with limit 5); BCrypt `workFactor: 12` | VERIFIED (prior step) — **deviation on the demo default, see §3.1** |
| SEC-06 | Every non-public endpoint declares its roles **and** the service enforces ownership/participation via `ICurrentUser` | Controllers (`[Authorize]` class-level on Contracts/Criteria/DocumentTypes/Lands/Offers/Roles; role-scoped on Users/GridCapacityReservations; per-action on Grids/Regions); services: `LandService.cs:71,417,436`, `OfferService.cs:69,78`, `ContractService.cs:57,300`, `UserService.cs:234-261` | No endpoint is reachable anonymously (§2.1); ownership 403/404 asserted by the step suites | **VERIFIED NOW** (authz surface) + prior (ownership) |
| SEC-07 | No actor/privilege field from the client; entities never bound | `RegisterRequest.RoleId [Range(1,2)]` is the only permitted exception; all other request DTOs carry no `userId`/`adminId`/`IsApproved`/`Status` | **Fixed in this session:** `DTOs/AdminReviewRequest.cs` (declared `AdminId`) removed — dead code, 0 references. Sweep is now clean; mass-assignment negatives in the step suites | **VERIFIED NOW** |
| SEC-08 | Register → Landlord/Investor only; Admin created by SuperAdmin only; no SuperAdmin creation/promotion via API; investor approval only Admin/SuperAdmin | `AuthService.cs:27-30`; `UserService.cs:129-134,234-244,246-262`; `UsersController.cs:11,44-48,54-64` | `step3_suite.ps1`, `s4_tests.ps1` (role matrix, one negative per row) | VERIFIED (prior step) / STATIC |
| SEC-09 | IDOR: a second user with the same role gets 403 or 404, consistently | Ownership checks in `LandService`/`OfferService`/`ContractService` via `ICurrentUser` | Step-4/5/6 suites (landlord B on A's land/documents; investor B on A's offers/contracts) | VERIFIED (prior step) / not re-run |
| SEC-10 | Only Admin sees other users' email/phone and user lists; party views expose `FullName` only; no `PasswordHash` in any response DTO | `UsersController.cs:11` (Admin/SuperAdmin only); `ContractDto` exposes names only; no `PasswordHash` in `DTOs/` | `PasswordHash` grep over `DTOs/` and `Controllers/` → 0 hits | STATIC |
| SEC-11 | LINQ or parameterized SQL only; filters bind to enums/whitelist | 7 `FromSqlInterpolated` uses, every one parameterized (`{id}`, `{offer.Id}`, `{offer.LandId}`, `{land.GridId}`) | Sweep: no `FromSqlRaw`/`ExecuteSqlRaw`/`SqlCommand`; only hit is the literal CSP string in `Program.cs:182` (§2.2). `s7_status_filter.ps1`: `?status=' OR 1=1--` → 400 | **VERIFIED NOW** |
| SEC-12 | No `Process`, `Assembly.Load*`, scripting/Roslyn, reflection on user strings, `unsafe`, `stackalloc`, P/Invoke, `Marshal` | none | Sweep returns 0 real hits — the single match is the word `unsafe` inside `script-src 'self' 'unsafe-inline'` (§2.2) | **VERIFIED NOW** |
| SEC-13 | No outbound HTTP; `FilePath` is stored text only (never opened/fetched/redirected); max 500; reject `://`, leading `\\`, `..`, control chars | `LandDocumentDtos.cs:10` (`[Required, NoControlCharacters, MaxLength(500), SafeRelativePath]`); `ValidationAttributes.cs:53-69`; `LandService.cs:324` | No `HttpClient`/`WebClient`; no `File`/`Directory`/`StreamReader` use. Negative payloads (`http://169.254.169.254/`, `file:///c:/windows/win.ini`, `\\server\share`, `..\..\x`) → 400 in `s4_regression.ps1`/`s4_tests.ps1` | **VERIFIED NOW** (static) + prior (negatives) |
| SEC-14 | `MaxLength` = DB column, `Range` on numbers, collections capped, Kestrel `MaxRequestBodySize` ≤ 1 MB, `pageSize` ≤ 50 | `Program.cs:12-15` (1 048 576); `UserService.cs:32-33`; DTO attributes | `verify-request-size.ps1` (2 MiB body → 413, fixed Content-Length); boundary cases in `s4_regression.ps1` | STATIC + prior |
| SEC-15 | API headers on every response; HTTPS redirect + HSTS outside Development; Swagger UI Development only; CORS not enabled | `Program.cs:170-189` (headers + CSP), `:192-196` (Swagger, dev only), `:198-201` (HSTS, non-dev), `:203` (HTTPS redirect); no `AddCors` | Live assertions on a 401: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; Swagger UI 200 in Development; HSTS absent in Development (§2.4) | **VERIFIED NOW** (HSTS/redirect are Production-only paths: STATIC only, §3.2) |
| SEC-16 | Frontend: no `dangerouslySetInnerHTML`/`innerHTML`/`eval`/`new Function`/`document.write`; user text rendered as text, never into `href`/`src`; strict production CSP | **Fixed in this session:** `frontend/vite.config.ts` (`nibras-production-csp`) injects the CSP meta on `vite build` only; regression check `docs/tests/verify-frontend-csp.ps1` | Sweep 0 hits. Built page carries the policy byte-for-byte with 0 inline `<script>`; the production bundle loads and renders the full login layout with 26 runtime style tags and **no** CSP violation reports (§2.5) | **VERIFIED NOW** |
| SEC-17 | Reject control characters (U+0000–U+001F, except tab/newline in free text); never reflect raw input in errors | `ValidationAttributes.cs:7-39` (`NoControlCharacters`, `NoControlCharactersExceptTabAndNewline`) applied across request DTOs | `step3_suite.ps1` / `s4_tests.ps1` negative cases | STATIC + prior |
| SEC-18 | Unhandled exception → generic 500 + correlation id; no stack trace/SQL/exception text to clients; logs carry no secrets | `ExceptionHandler.cs:36-71` (`traceId` always set; `500` detail is the fixed string); `Program.cs:190` | Every error body from the 63-operation sweep scanned for internal-detail markers → 0 leaks (§2.1). Static: the `500` branch never uses `exception.Message`; `_logger.LogError` is server-side only | **VERIFIED NOW** (no leakage) + STATIC (500 path) |
| SEC-19 | No secrets in the repository; JWT key, seed passwords and credentialled connection strings live in user-secrets/environment | `Program.cs:17-21` fails fast when `Jwt:Key` is missing; `appsettings.json` ships no usable connection string (empty `DefaultConnection` placeholder; no credential) | Two tracked-file secret sweeps → 0 hits (assignments, private keys, `ghp_`/`sk-`/`AKIA`/JWT prefixes); §2.6 | **VERIFIED NOW** |
| SEC-20 | Step-7 dependency check: `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev` | — | `dotnet`: *"no vulnerable packages given the current sources"*; `npm`: *"found 0 vulnerabilities"* (exit 0) (§2.7) | **VERIFIED NOW** |

## 2. Evidence collected in this session

### 2.1 SEC-01 / SEC-15 / SEC-02 — live, read-only application sweep

Method: the app was started in **Development** with **both startup seeders forced to a
provable no-op** (`Seed__SuperAdminEmail` whitespace → `SuperAdminSeeder` returns before
touching the context; `Seed__DemoData` unset → `DemoDataSeeder` skips). Every operation was
unauthenticated, so the authorization layer rejects before model binding or any query runs.

Result: **20 PASS / 0 FAIL**, `USERS` counted **154 before and 154 after** (no writes).
The app log confirmed `SuperAdmin seeding skipped because required configuration is missing`
and contained no `INSERT INTO [USERS]`.

- 63 operations enumerated from the live `swagger.json`; 62 returned **401**.
- The only non-401 operations were `POST /api/auth/register` and `POST /api/auth/login`
  (sent an empty `{}` body → **400** validation, no write).
- `GET /api/lands/{id}` with `{id}` substituted, `PUT`, `DELETE`, `PATCH` — all 401.
- **SEC-04:** none of the 63 responses carried a `Set-Cookie` header.
- **SEC-18:** every error body was scanned for `SqlException`, `System.Data`,
  `Microsoft.Data`, `at NIBRAS`, `StackTrace` and `Exception:` — 0 leaks.
- The four security headers were also asserted on a 400 validation response.

### 2.2 SEC-11 / SEC-12 sweeps

```
git grep -n -E 'FromSqlRaw|ExecuteSqlRaw|SqlCommand|Process\.Start|System\.Diagnostics\.Process|Assembly\.Load|CSharpScript|stackalloc|unsafe|DllImport|Marshal\.' -- '*.cs'
```

One hit: `Program.cs:182` — the literal `script-src 'self' 'unsafe-inline'` inside the
Swagger-UI CSP string. It is a string constant, not `unsafe` code. **Justified; 0 real findings.**

```
git grep -n -E 'FromSqlInterpolated|FromSqlRaw|ExecuteSql' -- '*.cs'
```

Seven hits, all `FromSqlInterpolated` with EF parameters (the `UPDLOCK, HOLDLOCK` row locks
and one `ExecuteSqlInterpolatedAsync`). No `FromSqlRaw`, no concatenation.

### 2.3 SEC-02 — token source

| Request | Result |
| --- | --- |
| `Authorization: Bearer <tampered signature>` | 401 |
| `Authorization: Bearer <alg:none>` | 401 |
| `GET /api/lands?access_token=<token>` | 401 |
| `Authorization: Bearer` (malformed) | 401 |

### 2.4 SEC-15 — headers

Observed on a 401 response, exactly as required:

```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
Referrer-Policy: no-referrer
Content-Security-Policy: default-src 'none'; frame-ancestors 'none'
```

`Strict-Transport-Security` is correctly **absent** in Development. Swagger UI returned 200
in Development only (`UseSwagger`/`UseSwaggerUI` are inside `IsDevelopment()`).

### 2.5 SEC-16 — production CSP (new)

`docs/tests/verify-frontend-csp.ps1` exits **0** on the current build and **1** when the meta
tag is removed from the build output (negative test performed). Loading the built page in a
browser confirmed the CSP decodes to the required policy, the app mounts, 26 runtime
`<style>` elements are injected (which is why `style-src` needs `'unsafe-inline'`), the
external stylesheet loads, and the only console message is the browser advisory that
`frame-ancestors` is ignored when delivered via `<meta>` (the policy string is kept for
parity with the specification; the API responses additionally send it as a real header).

### 2.6 SEC-19 — secrets

Two sweeps over tracked files: secret-shaped assignments
(`password|secret|api[_-]?key|bearer|connectionstring|jwt[_:]?key` = `"…"`) → **0 hits**;
private-key blocks and known token prefixes (`ghp_`, `sk-`, `AKIA`, `eyJ…`) → **0 hits**.
`appsettings.json` now ships an empty `ConnectionStrings:DefaultConnection` placeholder: the
repository holds no usable connection string and no server name. The real target is supplied by the
environment or the deployment configuration (`ConnectionStrings__DefaultConnection`), and
`DatabaseTargetGuard` refuses the production-like database name at runtime unless it is explicitly
approved through deployment configuration.

### 2.7 SEC-20 — dependencies

```
dotnet list package --vulnerable --include-transitive   → no vulnerable packages
npm audit --omit=dev                                    → found 0 vulnerabilities (exit 0)
```

### 2.8 API contract consistency (Amendment A1)

`docs/tests/verify-swagger-export.ps1` compares the committed `docs/api/swagger.json`
with the specification served by the current build: **63 operations and 47 schemas on both
sides, no difference.** The export is therefore not stale.

### 2.9 Frontend build baseline

`npm run typecheck` → exit 0; `npm run lint` → exit 0; `npm run build` → exit 0
(only the pre-existing bundle-size advisory).

## 3. Open items, deviations and risks

### 3.1 SEC-05 — demo rate-limit default (REQUIRES DECISION)

The specification says the limiter is *"configurable through `RateLimit:AuthPerMinute`
(default 30 for the demo … a production deployment must lower it, for example to 5-10)"*.
`appsettings.json` ships **10**, i.e. already inside the recommended production band and
stricter than the documented demo default. `docs/tests/security-step3.http` records that
value as intentional. **Not changed:** raising it to 30 would weaken a working control.
Needs a reviewer ruling on whether the shipped value must be 30.

### 3.2 Dynamic coverage that needs an approved write scope (BLOCKED)

The following required checks need an app run that **creates data** (registers users, creates
lands/offers/contracts) or a privilege change, which is a new database write scope:

- `docs/tests/security-step3.http`, `docs/tests/security-step4.http`, `docs/tests/step-3.http` … `step-6.http`
- `step3_suite.ps1`, `s4_tests.ps1`, `s4_admin.ps1`, `s4_regression.ps1`, `s5_offers.ps1`,
  `s6_contracts.ps1`, `parallel-approval.ps1`, `step3_ratelimit.ps1`
- SEC-03 (deactivate user / change role), SEC-09 (second-user IDOR), SEC-16's manual
  `<img src=x onerror=alert(1)>` entry in village/notes/reasons/messages/company name
- **SEC-15 HSTS + HTTPS redirect**: code-verified only. A Production-mode launch is needed to
  observe them, but `Jwt:Key` lives in user-secrets (Development-only), so a Production run
  would require supplying the signing key through the environment — deliberately not done.

### 3.3 Residual risks recorded

- The concurrency fixes in `4d651b8` rely on SQL Server honouring `WITH (UPDLOCK, HOLDLOCK)`
  inside EF's composed derived table; this is evidenced empirically (SQL trace + passing
  suites) rather than proven statically.
- `GET /api/criteria/current`, `GetAllAsync` aggregations and similar endpoints are unbounded
  by design (pre-existing); `pageSize` is capped at 50 where paging exists.
- No automated CI: the `.http` suites are executed manually (accepted risk G7 in the spec).
- `DatabaseTargetGuard` runs after `builder.Build()` (`Program.cs:156`), so EF's design-time host
  creation, which aborts the entry point inside `Build()` via `HostAbortedException`, never reaches
  it. `dotnet ef migrations has-pending-model-changes` diffs the compiled model snapshot against the
  design-time model and opens no connection (EF Core 10.0.10 `Migrator.HasPendingModelChanges`), so
  it is unaffected; a connecting design-time command (`dotnet ef database update`) must instead be
  given an explicit, verified `ConnectionStrings__DefaultConnection` target, because an empty or
  absent value is passed through unvalidated to `new SqlConnection(...)`.

## 4. Standalone verification scripts

Checked into `docs/tests/`; each exits non-zero when the property it guards regresses.

| Script | Guards | Needs |
| --- | --- | --- |
| `verify-frontend-csp.ps1` | SEC-16 production CSP present and byte-exact; no inline `<script>` in the built page | `npm` + `frontend/` (`-SkipBuild` reuses `dist/`) |
| `verify-swagger-export.ps1` | `docs/api/swagger.json` matches the live API surface (Amendment A1) | starts its own app, or `-BaseUrl` |
| `verify-request-size.ps1` | SEC-14 `MaxRequestBodySize` (2 MiB body → 413) | an already-running app |

The three startup seeders never run in these checks: the app-based ones are started with
`Seed__SuperAdminEmail` forced to whitespace and `Seed__DemoData` unset, so no write is
possible.

## 5. Commits produced by this review

| Commit | Subject |
| --- | --- |
| `54b0586` | Apply the SEC-16 production Content-Security-Policy to the frontend build |
| `1260393` | Remove the unused AdminReviewRequest DTO that violates SEC-07 |
| `160205c` | Add the security specification and the SEC-01..SEC-20 traceability matrix |
| _(this commit)_ | Add the Swagger-export drift check and record the review evidence |
