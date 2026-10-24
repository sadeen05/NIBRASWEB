# NIBRAS — Security traceability matrix (SEC-01 … SEC-20)

Specification: [`docs/05_SECURITY_REQUIREMENTS.md`](05_SECURITY_REQUIREMENTS.md) — *NIBRAS — Security
Requirements (v1, 9 Oct 2026)*, copied verbatim from the reviewer's document
(`SHA-256 2C8A9DA499A927EB6376F976B883938FE0215CB712384E8065BC27BD7D849617`, LF, 10 839 bytes).

Backend: ASP.NET Core (.NET 10) + EF Core 10 + SQL Server. Frontend: React 19 + Vite 8.

> **F1 increment addendum (21 Oct 2026).** The current isolated verification target is
> `SADEEN\SQLEXPRESS / NIBRAS_F1_Verify`; `NEBRAS`, `NEBRASDB` and `NEBRASDB_Step1` are forbidden.
> Historical evidence cited below that names an older target is kept as history and was **not**
> rerun in this increment. The accepted C4/five-suite evidence remains under
> `%TEMP%\nibras-regression-20261021-*`. The new row statuses for the address-change JWT binding,
> approval attribution and private document store are in the updated SEC-03, SEC-08, SEC-13 and
> SEC-14 rows; the detailed document-store contract is in
> [`DOCUMENT_STORAGE.md`](DOCUMENT_STORAGE.md). A browser account E2E on 22 Oct 2026 covered
> `/activate-admin`, `/verify-email`, `/confirm-email-change`, the address-change flow and the
> document upload/download panel; the historical F1 release evidence is retained outside the
> delivery repository (see the cleanup record in `NIBRAS_FILE_GUIDE.md`). Screenshots were not
> captured because the browser tab was not visible.

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
| SEC-03 | User must exist, not soft-deleted, DB role == role claim, **email == email claim**, **`EMAIL_VERIFIED_AT` epoch == `email_verified_at` claim**, and unverified accounts rejected, else 401 | `Program.cs:139-189` (`OnTokenValidated`, `IgnoreQueryFilters`, `IsDeleted`, role/email/epoch equality); `AuthService.cs:218-226` (claims); `EmailVerificationService.cs:441-529` (confirmation advances the epoch) | `step3_suite.ps1` Phase D (deactivate user → old token 401; DB role change → old token 401). Address-change stale-token path passed the focused suite (a pre-change JWT was rejected with 401). | VERIFIED (prior step) / **VERIFIED (focused suite, 22 Oct 2026)** |
| SEC-04 | No cookies / no server session; fresh token every login; `Jwt:ExpiryMinutes = 120` | `Program.cs:60-101` (`SaveToken=false`); `appsettings.json` `Jwt.ExpiryMinutes=120`; `AuthService.cs:125-126` | No `AddSession`/cookie scheme anywhere; no `Set-Cookie` on any of the 63 responses (§2.1); expiry read from config | **VERIFIED NOW** (cookies/session) + STATIC (fresh-token-per-login needs a real login) |
| SEC-05 | Login hardening: BCrypt ≥ 11, generic message + dummy-hash timing, password ≥ 10, rate limiter on login+register via `RateLimit:AuthPerMinute` → 429 | `AuthService.cs:13,74-83,52`; `PasswordPolicy.cs`; `Program.cs:39-52`; `appsettings.json:17` | `step3_ratelimit.ps1` (6th attempt → 429 with limit 5); BCrypt `workFactor: 12` | VERIFIED (prior step) — **deviation on the demo default, see §3.1** |
| SEC-06 | Every non-public endpoint declares its roles **and** the service enforces ownership/participation via `ICurrentUser` | Controllers (`[Authorize]` class-level on Contracts/Criteria/DocumentTypes/Lands/Offers/Roles; role-scoped on Users/GridCapacityReservations; per-action on Grids/Regions); services: `LandService.cs:1214` (`EnsureOwner`; called at 152, 211, 244, 403, 415, 704), `OfferService.cs:69,78`, `ContractService.cs:57,300`, `UserService.cs:234-261` | No endpoint is reachable anonymously (§2.1); ownership 403/404 asserted by the step suites | **VERIFIED NOW** (authz surface) + prior (ownership) |
| SEC-07 | No actor/privilege field from the client; entities never bound | `RegisterRequest.RoleId [Range(1,2)]` is the only permitted exception; all other request DTOs carry no `userId`/`adminId`/`IsApproved`/`Status` | **Fixed in this session:** `DTOs/AdminReviewRequest.cs` (declared `AdminId`) removed — dead code, 0 references. Sweep is now clean; mass-assignment negatives in the step suites | **VERIFIED NOW** |
| SEC-08 | Register → Landlord/Investor only; Admin created by SuperAdmin only; no SuperAdmin creation/promotion via API; investor approval only Admin/SuperAdmin; **approval requires a verified email and company details, records the acting admin, and appends durable history** | `AuthService.cs:27-30`; `UserService.cs:129-134,234-244,246-262,334-405` (`ApproveAsync`); `UsersController.cs:11,44-48,54-64`; `EmailVerificationService.cs` (verification state) | `step3_suite.ps1`, `s4_tests.ps1` (role matrix, one negative per row); the F1 focused suite covers unverified refusal, eligibility, actor attribution and re-approval, and passed 225/0. | VERIFIED (prior step) / **VERIFIED (focused suite)** |
| SEC-09 | IDOR: a second user with the same role gets 403 or 404, consistently | Ownership checks in `LandService`/`OfferService`/`ContractService` via `ICurrentUser` | Step-4/5/6 suites (landlord B on A's land/documents; investor B on A's offers/contracts) | VERIFIED (prior step) / not re-run |
| SEC-10 | Only Admin sees other users' email/phone and user lists; party views expose `FullName` only; no `PasswordHash` in any response DTO | `UsersController.cs:11` (Admin/SuperAdmin only); `ContractDto` exposes names only; no `PasswordHash` in `DTOs/` | `PasswordHash` grep over `DTOs/` and `Controllers/` → 0 hits | STATIC |
| SEC-11 | LINQ or parameterized SQL only; filters bind to enums/whitelist | 7 `FromSqlInterpolated` uses, every one parameterized (`{id}`, `{offer.Id}`, `{offer.LandId}`, `{land.GridId}`) | Sweep: no `FromSqlRaw`/`ExecuteSqlRaw`/`SqlCommand`; only hit is the literal CSP string in `Program.cs:182` (§2.2). `s7_status_filter.ps1`: `?status=' OR 1=1--` → 400 | **VERIFIED NOW** |
| SEC-12 | No `Process`, `Assembly.Load*`, scripting/Roslyn, reflection on user strings, `unsafe`, `stackalloc`, P/Invoke, `Marshal` | none | Sweep returns 0 real hits — the single match is the word `unsafe` inside `script-src 'self' 'unsafe-inline'` (§2.2) | **VERIFIED NOW** |
| SEC-13 | No outbound HTTP. **F1 exception:** new land documents store real bytes in a private, non-static local root and are opened only by an authenticated download route. The legacy metadata-only `FilePath` is still text only (never opened/fetched/redirected); max 500; reject `://`, leading `\\`, `..`, control chars. | `LandDocumentDtos.cs:10` (`[Required, NoControlCharacters, MaxLength(500), SafeRelativePath]`); `DTOs/ValidationAttributes.cs:53-70`; `Service/LandService.cs:437-603` (file stored before the transaction, deleted on failure; legacy text kept only in Development); `Service/DocumentStorageService.cs` (private root, signature check, SHA-256, path containment) | No `HttpClient`/`WebClient`; document file I/O is confined to `DocumentStorageService`; `App_Data` is Git-ignored and not static-served; legacy payloads (`http://169.254.169.254/`, `file:///c:/windows/win.ini`, `\\server\share`, `..\..\x`) → 400 in `s4_regression.ps1`/`s4_tests.ps1`. Real-store upload, download and cleanup passed the focused suite (225/0), including retained-file size/SHA verification. | **VERIFIED NOW** (no outbound HTTP) + prior (legacy negatives) / **VERIFIED for F1 store (disposable target)** |
| SEC-14 | `MaxLength` = DB column, `Range` on numbers, collections capped, **global Kestrel `MaxRequestBodySize` ≤ 1 MiB**, `pageSize` ≤ 50. **Single-action exception:** the multipart document upload allows 11 MiB (10 MiB file + 1 MiB envelope); no other action is raised. | `Program.cs:18-21` (1 048 576); `Service/DocumentStorageOptions.cs` (`DefaultMaxFileSizeBytes=10 MiB`, `MaxUploadRequestBytes=11 MiB`), `Program.cs:220-226` (startup bound); `Controllers/LandsController.cs:107-113` (`[RequestSizeLimit]` / `[RequestFormLimits]`); `UserService.cs:32-33`; DTO attributes | `verify-request-size.ps1` (2 MiB body → 413, fixed Content-Length) passed on 22 Oct 2026; the focused suite exercised the per-action boundary at its 1 KiB configured cap; the 10 MiB default and 11 MiB envelope remain statically bounded. | STATIC + prior / **VERIFIED globally and at configured cap; STATIC for the 10 MiB default** |
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
`DatabaseTargetGuard` always refuses `NEBRAS` and `NEBRASDB_Step1`, and refuses the
production-like name `NEBRASDB` at runtime unless it is explicitly approved through deployment
configuration.

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

### 2.10 Phase 1 - governorates, admin technical assessment and verification gating

Land registration now collects a governorate (`GOVERNORATES`, twelve seeded rows) plus village,
basin, parcel number, owner-entered land number, area and an optional description. `REGION_ID`,
`GRID_ID` and the four technical measurements are nullable and are written **only** by the
SuperAdmin-only `PUT /api/lands/{id}/assessment` (`[Authorize(Roles=SuperAdmin)]` plus the
service-level `EnsureSuperAdmin`; a regular Admin receives 403), which also appends an
append-only `LAND_ASSESSMENTS` snapshot
(actor taken from the JWT, UTC timestamp, note, effective values with real nulls preserved; no
update or delete route exists). The landlord create/update DTOs carry no admin-only field, so an
owner request can neither write nor erase an assessment (SEC-07). Eligibility gains a
`NotAssessed` result for an enforced criterion whose measurement is NULL; `NotAssessed` blocks
verification and is never a pass, and verification additionally requires an assigned grid whose
region equals the land's assigned region. Offer approval returns 409 when the land has no grid.

Checks run in this session (no database writes): `dotnet build` (0 errors; the 4 CS8618 warnings
are pre-existing), `dotnet ef migrations has-pending-model-changes` ("No changes have been made to
the model since the last migration"), `dotnet ef migrations script` (reviewed; the additive
migrations were subsequently applied to the disposable target, history 20),
`npm run typecheck` / `npm run lint` / `npm run build` (all exit 0), and
`verify-frontend-csp.ps1` (PASS - SEC-16 production CSP unchanged).

RESOLVED in Phase D: `verify-swagger-export.ps1` was run against a live instance and reports PASS -
`docs/api/swagger.json` was regenerated for the Phase-1/2 operations and schemas and now matches the
live API surface - and every DB-writing suite listed in 3.2 was executed against the confirmed test
target (results in 3.2). `verify-request-size.ps1` passed against the live disposable app on port 5199: a 2 MiB fixed-length body returned HTTP 413 with `PAYLOAD_TOO_LARGE`.


### 2.11 Phase 2 - rejection corrections and approved-land change requests

Rule 1/4 introduce a second SuperAdmin-only surface: land data (region, grid, technical
measurements) is assessed only by a SuperAdmin, so a regular Admin reviews submitted evidence but
never edits or re-measures a land.

A rejected land's owner can correct the owner-entered fields and resubmit. The resubmission is
routed to `LandStatus.PendingSuperAdminReview` (6), a status the regular admin queue never filters
on, so a correction can never silently re-enter `PendingVerification`. `LandService.EnsureReviewAuthority`
refuses every non-SuperAdmin document review, verify or reject decision on that status with 403
(the frontend mirrors this in `canDecide`, but the backend re-checks every call).

For an already-approved (`Verified`) land the owner submits a separate change request through
`POST /api/land-change-requests` with a reason and supporting documents. The request is stored
append-only in `LAND_CHANGE_REQUESTS` with the immutable old/new JSON snapshots, the reason, the
documents, the requester/reviewer ids, the timestamps and the final decision. The land is hidden
from investors for the whole review through a new `LANDS.IS_HIDDEN_FROM_INVESTORS` flag: investor
visibility is `Status == Verified && !IsHiddenFromInvestors`, and `OfferService.CreateAsync` refuses
new offers on a hidden land. Only a SuperAdmin decides
(`POST /api/land-change-requests/{id}/decision`). Approval writes the proposed owner-entered fields
and deliberately leaves the pinned `VERIFIED_AGAINST_CRITERION_ID` and `DATA_VERIFIED_BY_ADMIN`
untouched (rule 5), re-validating the proposed area against the frozen criterion version; rejection
keeps the original data and restores visibility only while the land is still `Verified`.

Three guarantees keep the approval and evidence trail sound (F1/F3 of the audit, plus the
re-review follow-up):

- **One pending request per land.** A filtered unique index
  `UX_LAND_CHANGE_REQUESTS_PENDING_LAND` on `(LAND_ID) WHERE [STATUS] = N'Pending'` is the
  database-level guarantee; a losing concurrent submission is mapped to the same 409 as the
  pre-check instead of surfacing as a 500. `LAND_CHANGE_REQUESTS.ROW_VERSION` is a rowversion
  concurrency token, so two concurrent decisions cannot overwrite each other: the loser gets 409
  and reloads, and the land change and its history row roll back with it.
- **Stale proposals are refused.** On approval the land's persisted fields are compared with the
  immutable `OLD_SNAPSHOT`; a mismatch means the land moved on after the request captured its
  snapshot, so the stale proposal is refused (409) instead of overwriting newer data. Rejection is
  always allowed, so a request can never be left stuck.
- **Only affected approvals are invalidated (F1).** When the approved change moves the land's
  identity, location or area, every `Approved` document returns to `Pending` with its review
  (`REVIEWED_BY_ID`, `REVIEWED_AT`, `REVIEW_NOTE`) and version intact - nothing is deleted and no
  approval becomes a rejection. A description-only change keeps every approval. A `Pending`
  document that still carries a `REVIEWED_AT` is exactly the "re-review required" state.

The re-review of an approved land's evidence is SuperAdmin-only and can only act on the **latest
version** of a document type, and only while that land's required evidence is outstanding (an
approval invalidated by an approved change, or a required document a SuperAdmin rejected). A
frozen document is a conflict for every caller, so it is decided before the authority check: a
regular Admin receives 409 (not 403) for a document that cannot be reviewed at all. On approval of
a required document the land must still satisfy the criteria version it was verified against
(`EnsurePinnedCriteriaStillSatisfiedAsync`), so visibility can never be restored onto a land that
fails its pinned criteria; that guard fails closed (L2) - a missing pin or a missing criteria row
refuses the approval (409) instead of substituting the current criteria, and rejection stays
available.

**Investor visibility is derived, not just flagged.** A verified land is presented to investors
only while `Status == Verified && !IS_HIDDEN_FROM_INVESTORS` **and** every document type required
for verification has an `Approved` latest version (`LandEvidence.VerifiedLandsWithOutstandingRequiredDocument`).
Only the latest version of a type is the land's evidence, so a superseded older version never
hides a land on its own - the same rule `VerifyAsync` applies. A required type with **no** uploaded
version at all is outstanding too: `VerifyAsync` requires an approved latest version for every
required type (never a vacuous pass), so a type made required after a land was verified is missing
evidence. This is what closes the dead-end: when a SuperAdmin rejects a required document during
the re-review, the land is already hidden (and stays hidden), and it becomes visible again only
after a corrected version is approved. The same derived rule gates `OfferService.CreateAsync`,
`OfferService.AdminApproveAsync` and `ContractService.CreateAsync`, so a land whose evidence is
outstanding receives no new offer, is never committed under contract, and never has a contract
created from its accepted offer.

**Making a required type optional cannot cancel a specific rejection.** `IS_REQUIRED_FOR_VERIFICATION`
is documented to affect future verifications and the derived evidence, not to resolve a document a
reviewer already refused, so `DocumentTypeService.UpdateAsync` refuses (409) to clear it while an
approved (`Verified`/`UnderContract`) land has, as the latest version of that type, a document a
reviewer rejected **after** the land was approved (`LandEvidence.HasRejectedEvidenceForTypeAsync`).
Otherwise clearing the flag would reveal the land to investors, re-open it for offers and make the
rejected version un-reviewable, silently bypassing the SuperAdmin's recorded decision. With the
guard the land stays hidden and the type stays required until the owner corrects the document and a
SuperAdmin approves it, which is the normal re-review path. Only a re-review rejection blocks, and
that exception is fail-closed: it applies only when the history proves it, i.e. a known review
timestamp older than a known approval timestamp. A rejected latest version with no review timestamp,
or an approved land with no recorded approval time, is indeterminate and is reported as a re-review
rejection rather than silently revealing the land, so an incomplete history can never become a
bypass. The correction flow resolves it either way: a corrected upload becomes the latest version
(no longer `Rejected`), after which the flag change is allowed again.

**Owner-only correction path.** `LandService.UploadDocumentAsync` additionally accepts an upload on
an approved (`Verified`/`UnderContract`) land when the requested document type's latest version is
not approved or is absent (no version uploaded at all). Only those outstanding types are accepted. The upload is a **new version** in
`Pending` (the rejected version and its review are preserved) and on an approved land only a
SuperAdmin can review it, so a landlord can correct evidence but cannot self-approve, supersede
valid evidence, or bypass the SuperAdmin review. A regular Admin cannot use this path
(`EnsureOwner`). No new schema is required for this: the state is fully derived from
`LAND_DOCUMENTS`.

**The correction upload, the document review, the verification and the change-request decision
serialize on the land row.** All four pin the `LANDS` row with `UPDLOCK, HOLDLOCK` inside their
transaction (`LandService.LockLandAsync`, the same convention `OfferService`/`ContractService` use)
and read the land status, the required-document evidence and the document version *after* taking it.
Without that, an upload's correction guard could be satisfied by evidence a concurrent review had
just made complete, and the upload would insert a `Pending` version that supersedes valid evidence -
silently pushing an approved land back into re-review. `VerifyAsync` takes the same lock before it
reads the status, the criteria and the required evidence and then writes the status, the pinned
criterion and the history in one transaction (L3), so a review can neither approve the last
outstanding document after the evidence check read it as outstanding nor reject an approved one
after it read it as approved; the status write is a tracking read of the same pinned row. The
technical assessment takes the lock and re-reads the land status under it as well, because an
assessment can withdraw document approvals while a verification is deciding on them. The decision
leans on the same lock because its batch writes `LANDS`, `LAND_DOCUMENTS` (the invalidation) and
`LAND_CHANGE_REQUESTS` at once: leaving that order to EF while a concurrent review holds the land
lock and then takes a `LAND_DOCUMENTS` lock could deadlock, and taking the lock first also keeps the
invalidation from interleaving with a re-review (whose pinned-criteria re-check then reads the
post-decision values). Invalidating approvals only moves documents towards `Pending`, and clearing
`IS_HIDDEN_FROM_INVESTORS` cannot reveal a land whose evidence is outstanding, so every interleaving
stays conservative.

Checks run for this work (no database writes): `dotnet build -c Release` (0 errors; the 4 CS8618
warnings are pre-existing) re-run after the evidence/lock audit, and `npm run typecheck` /
`npm run lint` / `npm run build` (all exit 0) plus `verify-frontend-csp.ps1 -SkipBuild` (PASS) when
the frontend was last changed.

RESOLVED (Phase D): migration `20261018090000_Phase2_LandCorrectionAndChangeRequests` is **applied**
on the confirmed test target `SADEEN\SQLEXPRESS / NEBRASDB_Step1` (migration history: 14 rows, the
Phase-2 migration last). The change-request endpoints, the hidden-land visibility, the derived
evidence visibility and the owner correction path are therefore **no longer static-only**: they are
exercised by the Phase-2 suite `docs/tests/scripts/p2_change_requests.ps1` (84 checks, all passing)
and by a browser end-to-end run of the landlord and SuperAdmin journeys, while the Step-4 regression
suite (`s4_tests.ps1`, 114 checks) covers the status-6 routing (SuperAdmin token for the resubmission
cycle, negative 403s for a regular Admin). The document-correction lifecycle now has an automated
suite.

### 2.12 Production-readiness hardening (22 Oct 2026)

The safe startup/security defects found in the release handoff were addressed without touching a
database or a production environment:

- **SEC-15 / HTTPS and proxy:** `ForwardedHeaders` is now opt-in. `ReverseProxy:Enabled=false` by
  default; when enabled, at least one explicit `KnownProxies`/`KnownNetworks` entry is mandatory.
  Wildcard, any-address, prefix-0, malformed and empty entries are refused; framework loopback
  defaults are cleared; only `X-Forwarded-For` and `X-Forwarded-Proto` are trusted, before HSTS,
  HTTPS redirection and rate limiting. `KnownProxies` and `KnownNetworks` are explicit, not
  derived from the request.
- **SEC-15 / hosts:** outside Development, empty or wildcard `AllowedHosts` refuses startup. The
  repository default is loopback-only; production must supply real host names. No CORS policy was
  added, preserving same-origin by default.
- **SEC-13 / document storage:** `DocumentStorageOptionsValidator` is startup-validated and
  resolved before seeders or database access. Outside Development an empty, relative or
  `wwwroot`-contained storage root refuses startup; Development keeps its fallback. The storage
  service repeats the containment checks.
- **SEC-08 / privileged provisioning:** outside Development, populated
  `Seed:SuperAdminEmail`/`Seed:SuperAdminPassword` refuses startup. The Development seeder remains
  Development-only. A separate, explicitly authorized one-time bootstrap is **proposed** in
  `docs/PRODUCTION_READINESS_CHECKLIST.md` §7; it is not implemented and no production account was
  created.
- **Evidence:** Release build succeeded; static acceptance **140/0**; real Production-mode host
  starts exited before listening for a missing storage root, an enabled proxy with no trust list,
  wildcard `AllowedHosts`, and populated Development-only SuperAdmin seed credentials. No SQL or
  SMTP mode was run for this hardening pass, and no deployment was performed.

## 3. Open items, deviations and risks

### 3.1 SEC-05 — demo rate-limit default (REQUIRES DECISION)

The specification says the limiter is *"configurable through `RateLimit:AuthPerMinute`
(default 30 for the demo … a production deployment must lower it, for example to 5-10)"*.
`appsettings.json` ships **10**, i.e. already inside the recommended production band and
stricter than the documented demo default. `docs/tests/security-step3.http` records that
value as intentional. **Not changed:** raising it to 30 would weaken a working control.
Needs a reviewer ruling on whether the shipped value must be 30.

### 3.2 Dynamic coverage — executed in Phase D, and what remains open

The write scope these checks needed was approved for Phase D and used against the confirmed test
target `SADEEN\SQLEXPRESS / NEBRASDB_Step1` (app started through `s4_app.ps1`,
`-AuthPerMinute 1000`, disposable stamped fixtures only). All of them pass, with 0 failures:

- `step3_suite.ps1` (PASS=29 FAIL=0), `step3_ratelimit.ps1` (PASS=4 FAIL=0), `s4_tests.ps1` (114),
  `s4_admin.ps1` (44), `s4_regression.ps1` (47), `s4_review.ps1` (14), `s5_offers.ps1` (127),
  `s6_contracts.ps1` (95), `s7_status_filter.ps1` (9), `s7_reservations.ps1` (41),
  `parallel-approval.ps1` (`PARALLEL APPROVAL PASS`: one 200, one 409, grid not oversubscribed) and
  the Phase-2 suite `p2_change_requests.ps1` (84) — plus a browser end-to-end run of the landlord and
  SuperAdmin journeys (submit → queue → reject → resubmit → approve, EN/AR, 403 route).
- SEC-03 (deactivate user / change role → the old token stops working) is exercised by
  `step3_suite.ps1`; SEC-09 (second-user IDOR) by the s4/s5/s6 suites.

Still open — no execution evidence, deliberately not claimed:

- The `.http` request collections (`docs/tests/security-step3.http`, `security-step4.http`,
  `step-3.http` … `step-6.http`) were updated for the governorate + admin-assessment contract but
  remain manual clients and were **not executed**.
- SEC-16's manual `<img src=x onerror=alert(1)>` entry in village/notes/reasons/messages/company
  name is **not executed**; only the automated payload negatives (path traversal, `://`, control
  characters) were run.
- **SEC-15 HSTS + HTTPS redirect**: code-verified only. A Production-mode launch is needed to
  observe them, but `Jwt:Key` lives in user-secrets (Development-only), so a Production run
  would require supplying the signing key through the environment — deliberately not done.

- Phase 1: the suites listed above were updated for the governorate + admin-assessment contract and
  were executed in Phase D (results above). The `.http` files listed above were updated as well and
  remain unexecuted.
- Migration `20261017140348_Phase1_GovernoratesAndLandAssessment` is **applied** on the confirmed
  test target, as is the Phase-2 migration — the migration history holds 14 rows with Phase-2 last.
  Its `Down()` deliberately carries **no** `DEFAULT 0` for the six relaxed columns, so a
  rollback fails loudly (SQL Server rejects `ALTER COLUMN ... NOT NULL` while NULLs remain) instead
  of fabricating zero technical values or concealing NULLs; recovery is forward-fix-first with a
  backup, never an automatic `Down`.

### 3.3 Residual risks recorded

- The concurrency fixes in `4d651b8` rely on SQL Server honouring `WITH (UPDLOCK, HOLDLOCK)`
  inside EF's composed derived table; this is evidenced empirically (SQL trace + passing
  suites) rather than proven statically.
- `DOCUMENT_TYPES.IS_REQUIRED_FOR_VERIFICATION` is editable by a regular Admin (documented
  behaviour, `S4-61a`/`S4-62a`; see §2.11). Because investor visibility is derived from the
  required set, making a type required now hides *every* verified land that has no approved
  version of it until the owner uploads one and a SuperAdmin approves it - which is exactly what
  `VerifyAsync` already enforced at verification time. It never rewrites an existing verification
  snapshot, and making a type optional can no longer reveal a land whose latest version of that
  type a reviewer rejected after approval - that toggle is refused (next bullet). Restricting the
  field to a SuperAdmin would be a product-policy change and is **not** implemented; the permission
  is unchanged.
- Making a required type optional cannot cancel a specific rejection.
  `DocumentTypeService.UpdateAsync` refuses (409) to clear `IS_REQUIRED_FOR_VERIFICATION` while an
  approved (`Verified`/`UnderContract`) land has, as the latest version of that type, a document a
  reviewer rejected **after** the land was approved (`LandEvidence.HasRejectedEvidenceForTypeAsync`;
  see §2.11). Without it the flag change would reveal the land to investors, re-open it for offers
  and make the rejected version un-reviewable - silently bypassing the recorded SuperAdmin
  decision. Making a type required and renaming it remain 200, and no rejection exists in the
  `S4-61a`/`S4-62a` flow, so those expectations are unchanged.
  **The check and the write are one transaction against the re-review (closed in `step-7-hardening`).**
  `UpdateAsync` now pins the `DOCUMENT_TYPES` row with `UPDLOCK, HOLDLOCK` inside its own transaction
  (the `LockLandAsync` convention), and `LandService.ReviewDocumentAsync` takes the same lock on the
  reviewed document's type after its `LANDS` lock (order never inverts: the update never takes a
  `LANDS` lock). The re-review precondition was also tightened from "the land's evidence is
  outstanding for some type" to "this document's type is itself in the outstanding set"
  (`OutstandingRequiredDocumentTypeIdsAsync`), so a review can never write a decision the derived
  evidence would ignore. Both interleavings therefore stay fail-closed: the review commits its
  rejection first and the update refuses on the guard outcome, or the optional change commits first
  and the re-review is refused because the type left the outstanding set. `VerifyAsync`
  (`PendingVerification`) is unaffected in both directions: reading the required set before the
  optional change only makes the evidence check stricter, and a new required type arriving after
  that read leaves the land dynamically hidden (`LandEvidence`) rather than verified-and-exposed.
  **Dynamically verified** in `LandRequirementConcurrencyAcceptance` (`--sql-acceptance`, prefix
  `f1race-*` fixtures on `NIBRAS_F1_Verify`): both deterministic interleavings, a genuinely parallel
  re-review/optional-change race showing exactly one winner and one controlled conflict with the
  unsafe state (type optional + freshly rejected latest version) unreachable, the rollback of a
  refused change (flag and name untouched), and the fail-safe complement (making a type required
  re-derives the land hidden without rewriting its verification snapshot). Accepted residual: a
  correction upload that loses to the optional change can insert a `Pending` version of the now
  optional type; an optional type never gates visibility, so the land's exposure is unchanged and
  any later re-requiring of the type re-derives the land hidden again.

- **Unique-index violations on the account identity are classified, never unknowable (closed in
  `step-7-hardening`).** `UserService.UpdateAsync` (the admin-managed profile path) now pre-checks a
  changed phone against every row - active and soft-deleted, exactly like the address - and answers the
  phone-specific conflict; in the race window the database unique index stays the authority, and the
  catch after the rollback re-reads which of the two indexes was violated, so the caller is told
  exactly which field to correct ("Phone already in use by another user." / "Email already in use by
  another user.") with the previous neutral answer as the fail-closed remainder. No other account, its
  ownership or its existence is revealed. A uniquely refused admin creation
  (`UserService.CreateAsync`) now answers the same merged `DuplicateRegistrationException` the pre-checks
  give instead of surfacing a server error, and self-registration keeps its deliberately merged answer
  (no address/phone distinction - anti-enumeration), which is asserted unchanged.
  **Dynamically verified** in `PhoneConflictAcceptance` (`--sql-acceptance`, disposable `NIBRAS_F1_Verify`):
  the controlled phone conflict (active and soft-deleted owner), the preserved address-specific conflict,
  two accounts racing the same phone number (exactly one winner, one phone-specific conflict, one owner
  after the race), two admin creations of the same identity racing (the loser no longer a 500), and two
  self-registrations racing the same phone (merged neutral conflict, one surviving account).
- Contract creation re-checks the land's evidence (`ContractService.CreateAsync`; see §2.11), so a
  land that stopped having valid evidence after its offer was approved is not committed to a
  contract. Residual: the trigger is a `DOCUMENT_TYPES` write (creating or making a type required),
  which takes no lock on `LANDS` or on the offer, so it can commit between that check and the
  contract insert. No lock in `CreateAsync` can close this - a `LANDS` `UPDLOCK` would only
  serialize against the re-review, which cannot start until such a change has already committed -
  and locking the required set in `DocumentTypeService.UpdateAsync` would serialize every
  document-type edit against every contract creation. Accepted as a bounded, benign interleaving:
  the land is `UnderContract`, so it is never investor-visible and receives no new offers, and the
  correction flow resolves the evidence. Recommendation: no change; revisit only if a hard
  serialization point is required.
- **Stored change-request snapshots are read defensively (L1).** An approval reads
  `OLD_SNAPSHOT`/`NEW_SNAPSHOT` only on the approval path; JSON that cannot be read fails the
  approval closed with a controlled 409 and is never replaced by an empty, current or guessed
  snapshot. JSON that parses but is not a usable proposal - an empty object such as `{}`, a blank
  identity field, or an area outside the range the create request accepts - is treated the same
  way, so a stored snapshot can never blank the land's fields on approval or be shown as if it
  were real data. A rejection deserializes nothing and always stays possible, so no request can be
  stuck and no land can be trapped behind one. The read endpoints return the unreadable snapshot
  (and the stored supporting-document list) as `null`, and the UI reports that it cannot be read
  instead of showing invented values; the stored row is never repaired, rewritten or replaced.
  Recovery for genuinely corrupt stored JSON is an authorized data operation, not an automatic
  repair.
- **The pinned criteria version is mandatory for a positive decision (L2).** A land with no
  `VERIFIED_AGAINST_CRITERION_ID`, or whose pinned `LAND_CRITERIA` row no longer exists, cannot
  approve a change request (`LandService.EnsureFrozenAreaCriterionAsync`) or re-approve a required
  document (`LandService.EnsurePinnedCriteriaStillSatisfiedAsync`): both guards fail closed with a
  controlled 409 instead of substituting the latest criteria, which would move an approval onto
  criteria it was never granted against. Criteria rows are append-only (no update or delete endpoint
  exists), so the state is only reachable through out-of-band data loss. Rejection stays available at
  every call site, so no request is ever stuck; a verified land in that state stays hidden from
  investors while its evidence is outstanding - the safe state, because restoring its visibility is
  exactly the act that must not happen without its criteria basis. Documented recovery: an
  authorized repair that restores the referenced `LAND_CRITERIA` row (or, if the row cannot be
  restored, a SuperAdmin-led data decision). No automatic substitution and no DB repair script.
- **Writers of `LANDS`/`LAND_DOCUMENTS` outside the land lock.** `LandService.UpdateAsync` (owner
  edit, only from `Draft`/`Rejected`) still writes `LANDS` and can withdraw approvals without taking
  the land lock, and the plain status transitions (`Submit`/`Revise`/`Reject`) write `LANDS` the same
  way. Their preconditions are disjoint from verification by status, and the owner edit's
  invalidation only moves approvals towards `Pending`, so the recorded failure mode is fail-safe: an
  owner edit that commits after a verification leaves the land verified but hidden until the
  evidence is re-reviewed. Serializing that last path was outside L3's scope and cannot be runtime
  tested here (no database access), so it remains a statically reviewed residual rather than a
  claimed race-free state.
- **Reference-delete FK violations are now translated into the controlled 409 path (implemented).**
  `DocumentTypeService.DeleteAsync`, `GridService.DeleteAsync`, `RegionService.DeleteAsync` and
  `RoleService.DeleteAsync` now catch the SQL Server foreign-key violation (error 547) and re-throw it
  as the existing `BusinessRuleException`, so a reference conflict answers **409 `INVALID_STATE`**
  instead of a generic 500 (`ExceptionHandler.cs:40`). The filter matches **only** 547
  (`ex.InnerException is SqlException sql && sql.Number == 547`), so every unrelated SQL error and
  every programming error still surfaces as 500 `INTERNAL_ERROR`; the pre-existing unique-violation
  handlers (2601/2627) in `ContractService`/`LandService`/`OfferService` are untouched. An exhaustive
  audit of the codebase confirms these four services are the only FK-to-409 handlers.
  **Dynamically verified for `Region`:** an isolated test with three disposable `TEST-DEL-*` fixtures
  (one region, one grid referencing it, one document type) on `NEBRASDB_Step1` answered **409
  `INVALID_STATE`** ("Cannot delete a region that is in use by grids or lands") while the referencing
  grid existed, and 204 for the unreferenced rows; the run left no residue and every table count and
  business invariant was unchanged. **`Grid`, `DocumentType` and `Role` remain STATIC-ONLY:** a grid
  can only be referenced by a land or a reservation, and because no land or document delete endpoint
  exists the chain cannot be cleaned up (the residue variant was not approved); the document-type
  pre-check always fires before the catch (only a concurrent upload could reach it); and
  `RolesController` exposes no delete route at all. **This is not a claim of full dynamic coverage.**
- **The upload-insert half of the same race is closed in `step-7-hardening` (previously documented
  as open).** `LandService.UploadDocumentCoreAsync` now inserts the `LAND_DOCUMENTS` row inside an
  explicit transaction that re-locks the land row first (`LockLandAsync`), catches the SQL Server
  foreign-key violation (error 547, `DatabaseErrors.IsForeignKeyViolation`) on its
  `SaveChangesAsync` and answers `ValidationFailedException` ("The selected document type no longer
  exists") when the document type was deleted between the lookup and the insert; the transaction
  scope rolls the insert back and the `finally` block removes the file that was already written.
  The successful delete path is unchanged: an upload that commits before the delete makes the
  `DocumentTypeService.DeleteAsync` pre-check see the new row and refuse with the existing 409
  message. The remaining window is only a `Pending` version of a deleted-type document never
  surfacing at all - no state is created.
- The investor list materialises the outstanding-evidence land ids as an `IN`/`NOT IN` list
  (`LandService.ApplyVisibilityAsync`). In the normal lifecycle that set is tiny, but making a new
  document type required across many verified lands grows it; if that operation is ever expected at
  scale, the predicate should move into a server-side `NOT EXISTS`. Unchanged shape from the
  original derived-evidence implementation.
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
| `verify-request-size.ps1` | SEC-14 `MaxRequestBodySize` (2 MiB body → 413) | an already-running app; PASS 22 Oct 2026 |

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
