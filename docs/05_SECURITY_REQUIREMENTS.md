# NIBRAS — Security Requirements (v1, 9 Oct 2026)

Scope: ASP.NET Core Web API (.NET 10) + EF Core + SQL Server + React SPA, JWT bearer authentication.
Every rule below (SEC-xx) is a **mandatory acceptance criterion**. A step is not ACCEPTED while one of its SEC tests fails. Tests live in `docs/tests/security-step<N>.http` (expected status codes written next to each call).

## 1. Threat map (the CWE list you asked for)

| CWE | Applies to NIBRAS? | Where it could happen | Controls | Step |
|---|---|---|---|---|
| CWE-78 OS command injection, CWE-77 command injection, code injection | Low (no shell or dynamic code today) | someone adds `Process.Start`, scripting, reflection on user strings; user-supplied `FilePath` later passed to a tool | SEC-12, SEC-13 | 4, 7 |
| CWE-918 SSRF | Low (no outbound HTTP) | any future "fetch this URL"; `LandDocument.FilePath` treated as a URL | SEC-13 | 4, 7 |
| CWE-306 missing authentication, CWE-287 improper authentication | **High** (today: no auth at all) | every endpoint; weak login; stale tokens | SEC-01, 02, 03, 05 | 3 |
| CWE-862 missing authorization, CWE-863 incorrect authorization | **High** | IDOR on lands, offers, contracts; role checks missing | SEC-06, 09, 10 | 3–6 |
| CWE-269 improper privilege management | **High** | register with admin role; mass assignment of `IsApproved`/`RoleId`; stale role in token | SEC-03, 07, 08 | 3–6 |
| CWE-501 trust boundary violation | **High** (today: actor ids come from request body, query and route) | `userId`, `adminId`, `landlordId` sent by the client | SEC-07 | 3–6 |
| CWE-384 session fixation | Low (JWT, no server session) | adding cookies/sessions later; token accepted from query string | SEC-04 | 3 |
| CWE-89 SQL injection | Low if LINQ only | raw SQL, string-built filters/sorting | SEC-11 | 4, 7 |
| CWE-120 buffer overflow | Not applicable to safe C# | `unsafe`, `stackalloc`, P/Invoke; practical equivalent = unbounded input | SEC-12, SEC-14 | 2–7 |
| CWE-79 XSS | **Medium** (stored text rendered by the React app) | village, notes, reasons, messages, company name; `FilePath` rendered as a link | SEC-15, 16, 17 | 3, FE-0..6 |

## 2. Rules

### Authentication and session (CWE-306, 287, 384)
- **SEC-01** Fallback authorization policy = authenticated user. Anonymous only: register and login. *Test:* call every path of `swagger.json` without a token → 401 except those two.
- **SEC-02** JWT validation: signature, issuer, audience and lifetime; algorithm pinned to HS256; ClockSkew ≤ 1 minute; key ≥ 32 bytes from user-secrets (startup fails if missing); token accepted ONLY from the `Authorization` header. *Test:* tampered token → 401; expired → 401; `alg:none` → 401; token in query string → 401.
- **SEC-03** On every authenticated request the user must exist, not be soft-deleted, and the DB role must equal the role claim; otherwise 401. *Test:* deactivate a user → old token 401; change a role in the DB → old token 401.
- **SEC-04** No cookies and no server session (`AddSession`, cookie auth forbidden); a fresh token is issued at every login; `Jwt:ExpiryMinutes = 120`.
- **SEC-05** Login hardening: BCrypt work factor ≥ 11; identical generic message and similar timing for unknown email and wrong password (verify against a dummy hash); password minimum length 10; built-in ASP.NET Core rate limiter on login and register, limit configurable through `RateLimit:AuthPerMinute` (default 30 for the demo, because the dev proxy makes all users share one IP (a production deployment must lower it, for example to 5-10, with the environment variable `RateLimit__AuthPerMinute`); the dedicated test runs with 5) → 429. *Test:* with the limit at 5, the 6th attempt in a minute → 429; both failures return the same body.

### Authorization, privilege, trust boundary (CWE-862, 863, 269, 501)
- **SEC-06** Every non-public endpoint declares its roles AND, for user-owned resources, the **service** checks ownership/participation using `ICurrentUser` from the token (never only in the controller).
- **SEC-07** No actor or privilege field comes from the client. Request DTOs never contain `userId`, `adminId`, `landlordId`, `investorId`, `RoleId` (except register: Landlord/Investor only), `IsApproved`, `IsDeleted`, `Status`, `DataVerifiedByAdmin`, `VerifiedAgainstCriterionId`, signature timestamps. Entities are never bound from requests. *Test:* send extra JSON fields (`"isApproved":true`, `"roleId":3`, `"status":3`) → ignored; verify in the DB.
- **SEC-08** Privilege rules: register creates only Landlord/Investor; Admin accounts are created by SuperAdmin only; nobody creates or promotes a SuperAdmin through the API; investor approval only by Admin/SuperAdmin. *Test:* the role matrix in `docs/01` §7, one negative call per row.
- **SEC-09** IDOR: for every endpoint taking a resource id, a second user with the same role gets 403 or 404 (state which, keep it consistent). *Test:* landlord B on landlord A's land/documents/offers/contracts; investor B on investor A's offers/contracts.
- **SEC-10** Personal data: only Admin sees other users' email/phone and user lists; party views show `FullName` only; no response DTO contains `PasswordHash`.

### Injection (CWE-89, 78, 77, 918)
- **SEC-11** SQL: LINQ or parameterized SQL only. No `FromSqlRaw`/`ExecuteSqlRaw`/`SqlCommand` with concatenated or interpolated user data. Query filters (`status`, sort) bind to enums or a whitelist. *Test:* `?status=' OR 1=1--` → 400; sweep finds no raw SQL.
- **SEC-12** No OS or dynamic code: `System.Diagnostics.Process`, `Assembly.Load*`, scripting/Roslyn, `Activator`/reflection on user strings, `unsafe`, `stackalloc`, P/Invoke, `Marshal` are forbidden. *Test:* sweep returns zero hits.
- **SEC-13** SSRF and path handling: the backend makes no outbound HTTP requests (or only to fixed hosts). `LandDocument.FilePath` is stored text only: never opened, fetched, redirected or used to read files. Validation: max 500 chars; reject `://`, leading `\\`, any `..` segment, control characters. *Test:* `http://169.254.169.254/`, `file:///c:/windows/win.ini`, `\\server\share`, `..\..\x` → 400.

### Bounded input (CWE-120 equivalent)
- **SEC-14** Every request string has `MaxLength` equal to its DB column; numbers have `Range`; collections capped; Kestrel `MaxRequestBodySize` ≤ 1 MB; `pageSize` ≤ 50. *Test:* 2 MB body → 413; 10,000-character name → 400; `pageSize=10000` → 400 or clamped.

### XSS and content (CWE-79)
- **SEC-15** API headers on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`; HTTPS redirection and HSTS outside Development; Swagger UI only in Development; CORS not enabled (the frontend uses a same-origin proxy).
- **SEC-16** Frontend: no `dangerouslySetInnerHTML`, `innerHTML`, `eval`, `new Function`, `document.write`; user text is always rendered as text; never put user text into `href`/`src` (`FilePath` is shown as plain text, never as a link); production CSP: `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'` (applied to the production build only, so `npm run dev` keeps working). The token lives in `localStorage` (accepted risk, mitigated by SEC-16, 120-minute expiry, no third-party scripts). *Test:* enter `<img src=x onerror=alert(1)>` in village, notes, reasons, offer messages and company name → displayed as text, nothing executes.
- **SEC-17** Reject control characters (U+0000–U+001F except tab and newline in free text) in request strings; never reflect raw input in error messages.

### Cross-cutting
- **SEC-18** Unhandled exceptions → generic 500 with a correlation id; no stack trace, SQL or exception text to clients; details only in server logs; logs never contain passwords or tokens.
- **SEC-19** No secrets in the repository: JWT key, seed passwords, connection strings with credentials live in user-secrets or environment variables.
- **SEC-20** Dependency check in Step 7: `dotnet list package --vulnerable --include-transitive` and `npm audit --omit=dev`; report everything, fix critical/high only when the fix is a safe upgrade.

## 3. Security tasks per step

| Step | Security tasks |
|---|---|
| Step 1 | none beyond SEC-19 (no secrets in commits) |
| Step 2 | bounds audit (SEC-14): `LandDocument.FilePath` max 500, `ReviewNote` max 500, `User.PasswordHash` ≥ 100 |
| Step 3 | SEC-01..05, 07/08 (user DTOs), 10, 14 (body limit), 15, 18; remove the `User` global query filter (3.10) |
| Step 4 | SEC-06/07/09 for lands, documents, criteria; SEC-11 (status filter), 13 (FilePath), 14, 17 |
| Step 5 | SEC-06/07/09 for offers; SEC-14/17 on messages and reasons |
| Step 6 | SEC-06/07/09 for contracts (only the contract's own parties sign; no timestamps from the client) |
| Step 7 | sweeps SEC-11, 12, 13, 19, 20; re-run all security `.http` files; verify SEC-15 headers |
| FE-0..FE-6 | SEC-16 in every step; FE-6 runs `npm audit --omit=dev` |

## 4. Final sweep (Step 7), PowerShell

```
Select-String -Path . -Recurse -Include *.cs -Pattern "FromSqlRaw|ExecuteSqlRaw|SqlCommand|Process\.Start|System\.Diagnostics\.Process|Assembly\.Load|CSharpScript|stackalloc|unsafe|DllImport|Marshal\."
Select-String -Path .\frontend\src -Recurse -Include *.ts,*.tsx -Pattern "dangerouslySetInnerHTML|innerHTML|eval\(|new Function|document\.write"
```
Expected result: no hits (every hit must be justified in the report).

## 5. Plan gaps found and decisions

| # | Gap | Decision |
|---|---|---|
| G1 | The plan had no security requirements | this document, mandatory per step (Amendment B) |
| G2 | JWT lifetime was 480 minutes | 120 minutes |
| G3 | Stale tokens (deleted user or changed role) stayed valid | SEC-03 |
| G4 | No bounded input, no body limit, no headers, no error-leak check | SEC-14, 15, 18 |
| G5 | `FilePath` is user-controlled text with no validation | SEC-13 |
| G6 | Frontend XSS rules were missing | SEC-16 |
| G7 | No automated tests | accepted risk: `.http` suites with expected status codes replace them |
| G8 | No audit log for security events | accepted: history tables and `ApprovedById/At` exist; no new audit table |
| G9 | Demo packaging was undefined | runs locally; README + `.http` + seeder (Step 7); no deployment |
| G10 | Real file upload | out of scope (it would add upload, path and content-type risks) |

## 6. Accepted risks

- Token in `localStorage` (XSS exposure) instead of an httpOnly cookie, because a cookie would need CSRF protection and more time; mitigations in SEC-16.
- No automated test suite, no penetration test; manual `.http` suites only.
- SQL Server integrated security on a developer machine; production hardening is out of scope.
