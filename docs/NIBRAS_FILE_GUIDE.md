# NIBRAS - File Guide and Delivery Map

**Generated:** 24 October 2026  
**Repository:** `D:\tasks\try\NibrasWeb`  
**Authoritative database:** `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`  
**Status:** cleanup and presentation packaging completed. Seven approved obsolete/historical
items were archived outside the repository and removed. Documentation links were repaired, a
root README was added, and the delivery guide was verified against the current source tree. No
database, migration, security-guard, application-behavior or Git-history change was made.

**Baseline update (24 October 2026):** Gate A, Gate B, the Mailtrap SMTP check and the
post-Gate-B backup are complete. `NIBRAS_cleanvirsion` now holds the post-Gate-B baseline:
3 users (1 non-operational placeholder, 1 operational SuperAdmin, 1 operational Admin) and
0 business records. See section 9.

---

## 1. Current cleanliness verdict

NIBRAS is **structurally coherent and presentation-ready as a source delivery**. The remaining
limits are operational or deployment-policy items, not unresolved application-source cleanup.

What is healthy:

- The backend is one ASP.NET Core web project (`NibrasWeb.csproj`) plus the independent
  clean-database runner (`tools/Nibras.CleanDbMigrator`).
- The frontend is a Vite/React application with a single router and a lazy-loaded page map.
- The 21 EF Core migrations through `20261023173135_HardenLandLocationUniqueness` are present
  and applied to the authoritative database.
- Seven approved obsolete/historical items were archived outside the repository and removed:
  the root parallel-approval wrapper, the v1 logical analysis, two F1 historical reports, one
  one-off SQL file and the two Phase 1 preparation projects.
- Broken Markdown links caused by those removals were repaired; a root `README.md` now gives
  clean setup, migration, startup, verification and packaging instructions.
- The guide and file maps below were rechecked against the current controllers, services,
  models, DTOs, enums, migrations, frontend routes and package dependencies.
- Backend Release build, F1 test build, frontend typecheck/lint/build, F1 `--static`,
  approval-time and dev-API mocks, and the static contract checks all pass after cleanup.
- Previous MVP validation completed with 162 reconciled PASS and 0 FAIL. That result is
  historical and was not rerun for this documentation pass.
- No demonstrably unused frontend component was found. The import scan reached every
  component/page through `router.tsx`, `lazyPages.ts`, `AppShell`, or a direct parent import.

Remaining operational or policy items:

- The Git worktree is intentionally dirty; all pre-existing uncommitted work and the four
  pre-existing worktree deletions are preserved. No reset, clean, stash, stage, commit or push
  is part of this work.
- Local build/runtime output exists under `bin`, `obj`, `.vs`, `frontend/node_modules`,
  `frontend/dist`, and `App_Data`; none of it is delivered.
- Six non-authoritative application databases remain on the SQL Server instance. One of them,
  `NIBRAS_F1_Verify`, is still referenced by retained tests.
- `NIBRAS_cleanvirsion` is the post-Gate-B clean baseline: 3 users (the non-operational
  placeholder, exactly one SuperAdmin and exactly one Admin) and zero business records. The
  verified copy-only backup of that state is preserved next to the earlier baseline.
- The protected `docs/tests/scripts/s4_preflight.ps1`, old manual `.http`/`.sql` collections and
  non-manifest supplementary probes remain in the controlled source tree but are excluded from
  the presentation package.

Release position: functional for a controlled Admin UAT on the post-Gate-B baseline; **not
production-ready**. Mailtrap SMTP delivery was validated during Gate B; the final browser
walkthrough sends no mail.

---

## 2. Clean delivery tree

The presentation package contains only application source, the 21 migrations, required
configuration templates, the useful test harness, and essential documentation.

```text
NibrasWeb/
|-- README.md                   setup, migration, startup, checks, package notes
|-- .gitignore
|-- NibrasWeb.csproj
|-- NibrasWeb.slnx
|-- Program.cs
|-- appsettings.json
|-- CleanDatabase.cs
|-- CleanDatabaseTargetGuard.cs
|-- DatabaseTargetGuard.cs
|-- ExceptionHandler.cs
|-- ExceptionTypes.cs
|-- Properties/
|   `-- launchSettings.json
|-- Controllers/                 13 API controllers
|-- DTOs/                        36 request/response contracts
|-- Enums/                       11 status/reason/role constants
|-- Models/                      27 EF entities, DbContext and audit partial
|-- Service/                     67 services, interfaces, options and helpers
|-- Migrations/                  21 migrations + generated designers + model snapshot
|-- frontend/
|   |-- package.json
|   |-- package-lock.json
|   |-- index.html
|   |-- vite.config.ts
|   |-- vite.https.config.ts
|   |-- devApiTarget.ts
|   |-- eslint.config.js
|   |-- tsconfig*.json
|   |-- public/
|   `-- src/                     React application source only
|-- tools/
|   `-- Nibras.CleanDbMigrator/   clean-baseline migration runner (source + README)
|-- tests/
|   `-- NibrasWeb.F1.Preparation/ useful static/SQL/fake-SMTP acceptance harness
`-- docs/
    |-- NIBRAS_FILE_GUIDE.md
    |-- 05_SECURITY_REQUIREMENTS.md
    |-- CLEAN_DATABASE_PLAN.md
    |-- CODEX_HANDOFF.md
    |-- DOCUMENT_STORAGE.md
    |-- EMAIL_VERIFICATION.md
    |-- PRODUCTION_BOOTSTRAP_AND_HEALTH.md
    |-- PRODUCTION_READINESS_CHECKLIST.md
    |-- SECURITY_TRACEABILITY.md
    |-- api/swagger.json
    `-- tests/                   maintained regression harness and test documentation
```

Excluded from the presentation ZIP:

- `.git/`, `.vs/`, `bin/`, `obj/`, `frontend/node_modules/`, `frontend/dist/`, build outputs;
- `NibrasWeb.csproj.user`, local secrets, user secrets, environment files and credentials;
- `App_Data/` uploads and runtime document storage;
- SQL databases, backups, old delivery ZIPs, raw test logs, screenshots, generated evidence and
  receipt files;
- the seven archived cleanup items (they remain outside the repository, not in the package);
- old manual `.http`/`.sql` collections and `step-2.sql`;
- non-manifest supplementary probes `s4_probe.ps1`, `s4_page.ps1` and `s4_overflow.ps1`.

`docs/tests/scripts/s4_preflight.ps1` is protected. It must never be run or modified. It is
retained in the controlled source tree and excluded from the ZIP rather than deleted. The full
`run_regression.ps1` manifest pins it, so complete manifest runs must use the controlled source
repository; the package README states this limitation.

---

## 3. Backend file-by-file map

### 3.1 Root and build files

| File | Purpose | Delivery |
| --- | --- | --- |
| `NibrasWeb.csproj` | ASP.NET Core web project; .NET 10, EF Core SQL Server, JWT, Swagger, BCrypt, Mapster. Excludes `tests/**` and `tools/**` from web compilation. | Required |
| `NibrasWeb.slnx` | Solution containing the web project and `tools/Nibras.CleanDbMigrator`. | Required |
| `Program.cs` | Host composition: configuration, Kestrel limit, JWT, rate limits, DI, EF DbContext, authorization fallback, email delivery, health checks, middleware, Development seeders, bootstrap command. | Required |
| `appsettings.json` | Safe configuration template: empty connection string, JWT issuer/audience/expiry, rate limits, document storage, email, reverse proxy, hold days, allowed hosts. No signing key or credential. | Required |
| `Properties/launchSettings.json` | Development launch profiles on `http://localhost:5181` and HTTPS. | Required for local run |
| `DatabaseTargetGuard.cs` | Fail-closed database target parsing and forbidden production-like names. Used by the host and seeders. | Required/security |
| `CleanDatabase.cs` | Constants for the one authoritative clean database. | Required by tools/tests |
| `CleanDatabaseTargetGuard.cs` | Strict guard that admits only `SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion`. | Required by tools/tests |
| `ExceptionHandler.cs` | `GlobalExceptionHandler`: converts domain exceptions into problem-details responses and hides unexpected detail. | Required |
| `ExceptionTypes.cs` | `NotFoundException`, `ForbiddenException`, `BusinessRuleException`, `ValidationFailedException`, `EmailNotVerifiedException`. | Required |
| `.gitignore` | Ignores build output, local user files, uploads and other non-source artifacts. | Repository hygiene |
| `README.md` | Presentation entry point: stack overview, layout, configuration, migration, backend/frontend startup, offline checks, package exclusions and security notes. | Required |

### 3.2 Controllers

Controllers are thin HTTP adapters. They validate access, call one service, and return a DTO
or problem-details response. Authorization is repeated at the endpoint or controller level;
the UI guards are not a security boundary.

| Controller | Route surface | Main responsibility |
| --- | --- | --- |
| `AuthController.cs` | `/api/auth` | Register, login, `me`, admin activation, email verification, address change, confirmation and resend. Anonymous endpoints are explicitly marked; the rest require a JWT. |
| `UsersController.cs` | `/api/users` | Admin/SuperAdmin user browsing, creation, approval/rejection, update, deactivation, admin invitation, SuperAdmin verification issue/resend. |
| `RolesController.cs` | `/api/roles` | Read-only role lookup. |
| `RegionsController.cs` | `/api/regions` | Region lookup; Admin/SuperAdmin maintenance. |
| `GovernoratesController.cs` | `/api/governorates` | Read-only Jordanian governorate lookup. |
| `GridsController.cs` | `/api/grids` | Grid lookup and Admin/SuperAdmin grid maintenance. |
| `CriteriaController.cs` | `/api/criteria` | Current/history land criteria; Admin/SuperAdmin creation and SuperAdmin-only version policy. |
| `DocumentTypesController.cs` | `/api/document-types` | Document-type lookup and Admin/SuperAdmin maintenance. |
| `LandsController.cs` | `/api/lands` | Land create/edit/list/detail, submit, revise, verify, reject, eligibility, history, documents upload/download/review/metadata, technical assessment and assessment history. |
| `LandChangeRequestsController.cs` | `/api/land-change-requests` | Landlord change-request submission, owner/admin listing, pending list and SuperAdmin decision. |
| `OffersController.cs` | `/api/offers` | Offer create, counter-offer, accept, reject, withdraw, admin approval/rejection, versions, negotiation history and pending-admin list. |
| `ContractsController.cs` | `/api/contracts` | Contract create, list/detail, sign, review, terminate, reviews, versions, revision request/consent/Admin decision/SuperAdmin decision. |
| `GridCapacityReservationsController.cs` | `/api/GridCapacityReservations` | Admin/SuperAdmin reservation listing and manual release. Reservations are created by workflow code only. |

### 3.3 Services

| File | Purpose |
| --- | --- |
| `IAuthService.cs` / `AuthService.cs` | Register/login, password verification, JWT issue, current-user projection and account-level auth rules. |
| `IUserService.cs` / `UserService.cs` | User CRUD, approval state, role changes, deactivation, admin invitation and verification resend. |
| `IRoleService.cs` / `RoleService.cs` | Role lookup and mapping. |
| `IRegionService.cs` / `RegionService.cs` | Region lookup and maintenance. |
| `IGovernorateService.cs` / `GovernorateService.cs` | Read-only governorate reference data. |
| `IGridService.cs` / `GridService.cs` | Grid lookup and maintenance. |
| `ICriteriaService.cs` / `CriteriaService.cs` | Versioned land criteria; current/history and policy creation. |
| `IDocumentTypeService.cs` / `DocumentTypeService.cs` | Document-type reference data and required-for-verification flags. |
| `ILandService.cs` / `LandService.cs` | Land aggregate: create/edit, submit/revise, verify/reject, assessment, document evidence, eligibility, history and visibility rules. |
| `IOfferService.cs` / `OfferService.cs` | Offer lifecycle, versioning, negotiation history, admin approval, grid capacity reservation and competing-offer closure. |
| `IContractService.cs` / `ContractService.cs` | Contract creation from accepted offer, signatures, review, termination, versions, revisions, consents and capacity release. |
| `IGridCapacityReservationService.cs` / `GridCapacityReservationService.cs` | Reservation read access and Admin release rules. |
| `ICurrentUser.cs` / `CurrentUser.cs` | Reads authenticated identity and role from `HttpContext`. |
| `IUserSecurityEventService.cs` / `UserSecurityEventService.cs` | Append-only security audit trail. |
| `LandEvidence.cs` | Shared document-evidence rule used by land visibility and offer gates. |
| `PasswordPolicy.cs` / `PasswordVerifier.cs` | Password policy and non-throwing BCrypt verification. |
| `DatabaseErrors.cs` | Maps known SQL Server errors to controlled domain responses. |
| `DatabaseReadinessCheck.cs` / `HealthOptions.cs` | Bounded database readiness check and health configuration. |
| `AdminInvitationService.cs` / `IAdminInvitationService.cs` | Issues and delivers the one-time Admin activation invitation. |
| `RegistrationVerificationService.cs` / `IRegistrationVerificationService.cs` | Issues registration verification for Landlord/Investor accounts. |
| `EmailVerificationService.cs` / `IEmailVerificationService.cs` | Issues, validates and consumes verification tokens; cancellation behavior. |
| `EmailChangeService.cs` / `IEmailChangeService.cs` | Authenticated address-change confirmation flow. |
| `EmailVerificationResult.cs` | Domain result enum for verification operations. |
| `VerificationDelivery.cs` | Delivery contract and cancellation/cleanup semantics. |
| `VerificationDeliveryQueue.cs` | Bounded in-memory verification-mail queue. |
| `VerificationDeliveryWorker.cs` | Managed background worker that delivers queued verification mail. |
| `VerificationLinkBuilder.cs` | Builds public activation/verification/address-change links with tokens in the URL fragment. |
| `VerificationTokenCodec.cs` | Creates and hashes one-time verification tokens. |
| `IEmailSender.cs` / `DisabledEmailSender.cs` / `FileOutboxEmailSender.cs` / `SmtpEmailSender.cs` | Email delivery abstraction and its disabled, development-outbox and SMTP implementations. |
| `EmailOptions.cs` / `EmailOptionsValidator.cs` | Email configuration and startup validation. |
| `EmailOutboxLocation.cs` | Safe location rules for the Development outbox. |
| `DocumentStorageOptions.cs` / `DocumentStorageOptionsValidator.cs` | Private document-root configuration and startup validation. |
| `DocumentStoragePath.cs` | Root resolution and containment checks. |
| `DocumentStorageService.cs` / `IDocumentStorage.cs` | Private file write/read, size/signature checks and stored-document metadata. |
| `JwtConfiguration.cs` | Reads and validates signing key, issuer, audience and expiry at startup. |
| `AuthClaimTypes.cs` | Custom JWT claim names, including the verification epoch. |
| `ProductionConfigurationGuard.cs` | Refuses permissive production configuration and Development-only seed credentials outside Development. |
| `ReverseProxyOptions.cs` | Opt-in forwarded-header trust configuration and validation. |
| `SuperAdminBootstrap.cs` | One-time out-of-band SuperAdmin bootstrap command; never an automatic path. |
| `SuperAdminSeeder.cs` | Development-only SuperAdmin seed. |
| `DemoDataSeeder.cs` | Development-only, opt-in demo data; guarded against the clean database. |

### 3.4 EF models

| File | Purpose |
| --- | --- |
| `NebrasdbContext.cs` | EF Core context, DbSets, mappings, relationships, indexes and query filters. |
| `NebrasdbContext.Audit.cs` | Partial context enforcing append-only criteria/security/assessment rows and actor snapshots. |
| `User.cs` | Account, role, verification/approval state, address-change state and relationships. |
| `Role.cs` / `RoleNames.cs` | Role entity and the four fixed role names. |
| `Region.cs`, `Governorate.cs`, `Grid.cs` | Geographic and grid reference entities. |
| `Land.cs` | Land aggregate root, location, assessment, status, visibility, deletion flag and relationships. |
| `LandAssessment.cs` | Immutable admin technical assessment history. |
| `LandCriterion.cs` | Versioned eligibility criteria. |
| `LandDocument.cs` | Document metadata, private storage key and review status. |
| `LandStatusHistory.cs` | Append-only land status transition record. |
| `LandChangeRequest.cs` | Immutable old/new snapshots and decision record for approved-land corrections. |
| `Offer.cs` | Offer aggregate, current amount/capacity, status and links to versions/contract/reservation. |
| `OfferVersion.cs` | Immutable offer version and current-version flag. |
| `OfferNegotiationHistory.cs` | Append-only offer negotiation/action history. |
| `Contract.cs` | Contract aggregate, signatures, review/termination fields, status and links. |
| `ContractReview.cs` | Append-only contract review/decision audit. |
| `ContractVersion.cs` | Immutable contract draft/event timeline. |
| `ContractRevision.cs` | Pre-signing revision request, party consents and Admin/SuperAdmin decisions. |
| `ContractRevisionConsent.cs` | One party's append-only consent to a revision. |
| `GridCapacityReservation.cs` | Grid hold created by contract workflow; release/expiry state. |
| `EmailVerificationToken.cs` | One-time registration/address-change/admin-activation token. |
| `UserSecurityEvent.cs` | Append-only account security event with actor snapshots. |
| `DocumentType.cs` | Required document types and verification flags. |
| `DeletionRequest.cs` | Legacy mapped table/relationship with no current controller or service use. Retained because it is part of the existing EF model, snapshot and migration history. Do not remove without a separately approved schema decision. |

### 3.5 DTOs

| File(s) | Purpose |
| --- | --- |
| `AuthRequests.cs` | Register, login, activation, verification, address-change and resend request/response shapes. |
| `UserDto.cs`, `CreateUserRequest.cs`, `UpdateUserRequest.cs`, `RejectInvestorApprovalRequest.cs` | User list/detail/approval and create/update contracts. |
| `LandDto.cs`, `CreateLandRequest.cs`, `UpdateLandRequest.cs`, `AssessLandRequest.cs`, `VerifyLandRequest.cs` | Land create/edit/assess/verify contracts. |
| `LandAssessmentDto.cs`, `LandCriterionDtos.cs`, `LandStatusHistoryDto.cs` | Assessment, criteria and history projections. |
| `LandDocumentDtos.cs` | Document metadata/upload/review/download contracts. |
| `LandChangeRequestDtos.cs` | Land change-request submit/list/decision contracts. |
| `OfferDto.cs`, `CreateOfferRequest.cs`, `CounterOfferRequest.cs`, `RejectOfferRequest.cs` | Offer create/counter/reject and projection contracts. |
| `OfferVersionDto.cs`, `OfferNegotiationHistoryDto.cs` | Offer history projections. |
| `ContractDtos.cs` | Contract create/detail/review/sign/terminate/version/revision contracts. |
| `GridDto.cs`, `CreateGridRequest.cs`, `UpdateGridRequest.cs` | Grid reference and maintenance contracts. |
| `RegionDto.cs`, `CreateRegionRequest.cs`, `UpdateRegionRequest.cs` | Region reference and maintenance contracts. |
| `GovernorateDto.cs` | Governorate projection. |
| `RoleDto.cs`, `CreateRoleRequest.cs`, `UpdateRoleRequest.cs` | Role projection and maintenance contracts. |
| `DocumentTypeDtos.cs` | Document-type projection and maintenance contracts. |
| `GridCapacityReservationDto.cs`, `ReleaseGridCapacityReservationRequest.cs` | Reservation projection and manual release request. |
| `ValidationAttributes.cs` | Shared request validation attributes. |

### 3.6 Enums and status constants

| File | Values and use |
| --- | --- |
| `LandStatus.cs` | `Draft`, `PendingVerification`, `Verified`, `Rejected`, `UnderContract`, `PendingSuperAdminReview`. |
| `OfferStatus.cs` | `PendingLandlordResponse`, `PendingInvestorResponse`, `PendingAdminApproval`, `Accepted`, three rejection states, `WithdrawnByInvestor`, `Closed`. |
| `OfferNegotiationHistory.cs` | Offer action types stored in negotiation history. |
| `ContractStatus.cs` | `PendingSignatures`, `Active`, `Terminated`, `Rejected`, `Cancelled`. |
| `ContractReviewDecisions.cs` | Approved/rejected/terminated and revision decision strings. |
| `ContractRevisionStatuses.cs` | Revision states plus consent decisions; `IsOpen` helper. |
| `ChangeRequestStatuses.cs` | Pending, Approved, Rejected. |
| `DocumentStatuses.cs` | Pending, Approved, Rejected. |
| `EmailVerificationPurposes.cs` | Registration, EmailChange, AdminActivation and role rule. |
| `UserSecurityEventTypes.cs` | Closed list of auditable account-security event types. |
| `ConnectionMechanism.cs` | Buy all/sell all, self-consumption net billing, zero export, full netting. |

### 3.7 Migrations

All 21 migrations are required in order. The `.Designer.cs` files and
`NebrasdbContextModelSnapshot.cs` are generated EF artifacts and must stay with their
migrations.

| Order | Migration | Purpose |
| --- | --- | --- |
| 1 | `20260721113459_InitialCreate` | Initial schema: users, roles, lands, regions, grids, documents, offers, contracts, status/reference tables. |
| 2 | `20260727183836_AddCancellationAndTariffSchema` | Cancellation/tariff-era schema and reference values. |
| 3 | `20260801212633_DropOfferStatusTable` | Removes status lookup tables in favor of code enums. |
| 4 | `20260801220718_AddOfferSystem` | Adds/renames the offer and version model. |
| 5 | `20260801231103_DropOfferFinancialColumns` | Removes obsolete financial columns from offer versions. |
| 6 | `20260801231519_DropAssessmentColumns` | Removes obsolete offer assessment columns. |
| 7 | `20260801231939_DropRegulatoryTariffSettings` | Drops the legacy regulatory-tariff table. |
| 8 | `20260801235633_AddIsCurrentColumnToOfferVersion` | Adds `IS_CURRENT` and marks the latest version per offer. |
| 9 | `20261010075624_Step1_FixBreakages` | Removes legacy status FKs/tables and repairs step-1 schema breakages. |
| 10 | `20261010170736_Step2_NewSchemaAndSeeds` | New schema fields, approval/status data and seed corrections. |
| 11 | `20261011221505_Step5_OffersAdjustments` | Adds `EXPIRES_AT` to grid capacity reservations. |
| 12 | `20261015140150_Step5_OffersActiveInvestorLandUnique` | Unique active-offer-per-investor-per-land index. |
| 13 | `20261017140348_Phase1_GovernoratesAndLandAssessment` | Governorates, nullable technical assessment fields, location uniqueness changes. |
| 14 | `20261018090000_Phase2_LandCorrectionAndChangeRequests` | Hidden-from-investors flag, immutable land change requests, correction flow. |
| 15 | `20261019165813_EmailVerification` | Email verification/address-change state and tokens. |
| 16 | `20261019174547_EmailVerificationUniqueness` | Pending-email and live-token uniqueness indexes. |
| 17 | `20261021163426_AddLandDocumentStorageMetadata` | Private document content type/size/name/storage metadata. |
| 18 | `20261021224950_AddGovernanceHistorySnapshots` | Governance/actor snapshot history. |
| 19 | `20261022183848_AddContractVersionHistory` | Immutable contract versions/event timeline. |
| 20 | `20261022191140_AddContractRevisionWorkflow` | Pre-signing contract revisions, consents and decisions. |
| 21 | `20261023173135_HardenLandLocationUniqueness` | Final unique location indexes for non-deleted, assessed lands. |

### 3.8 Backend runtime flow

```text
React page
  -> frontend/src/api/<module>.ts
  -> axios apiClient (JWT bearer, timeout, normalized errors)
  -> /api/<controller>
  -> Controller (route, [Authorize], request DTO)
  -> Service interface / implementation (business rules, transactions)
  -> NebrasdbContext (LINQ, query filters, SaveChanges audit hook)
  -> SQL Server (SADEEN\SQLEXPRESS / NIBRAS_cleanvirsion)
  -> DTO / ProblemDetails
  -> React Query cache -> page
```

Important boundaries:

- The UI route guards are navigation aids only; every API endpoint enforces its own auth.
- JWT validation re-reads the user and role from the database on every authenticated request.
  A deleted user, changed role, unverified email, changed email or stale verification epoch
  invalidates the token.
- The default authorization fallback policy requires an authenticated user, so a new endpoint
  is protected unless it is explicitly marked anonymous.
- Domain exceptions are converted centrally; unexpected exceptions become a generic 500 with
  a trace id.
- Writes that cross aggregates (offer/contract/grid capacity) use service-level transactions
  and database constraints; the UI is never the concurrency authority.

---

## 4. Frontend file-by-file map

### 4.1 Application shell, routing and auth

| File | Purpose |
| --- | --- |
| `frontend/src/main.tsx` | React entry point, font subsets, i18n initialization, global CSS, StrictMode render. |
| `frontend/src/app/App.tsx` | Query client, Ant Design theme/provider, auth provider, router provider. |
| `frontend/src/app/router.tsx` | Public and authenticated route tree; role groups for Landlord, Investor, Admin/SuperAdmin and SuperAdmin. |
| `frontend/src/app/lazyPages.ts` | Lazy imports for every route-level page. This is the reference list for page reachability. |
| `frontend/src/auth/AuthProvider.tsx` | Login/logout, session restore/revalidation, token provider, 401 handling and expiry timer. |
| `frontend/src/auth/context.ts` | Auth React context and status types. |
| `frontend/src/auth/useAuth.ts` | Auth hook. |
| `frontend/src/auth/guards.tsx` | `RequireAuth` and `RequireRole` navigation guards. Not a backend security boundary. |
| `frontend/src/auth/routeAccess.ts` | Post-login/403 destination rules that mirror the router's role groups. |
| `frontend/src/auth/session.ts` | Safe session persistence, expiry check and conversion from auth response. |

### 4.2 API modules

| File | API area |
| --- | --- |
| `frontend/src/api/client.ts` | Axios instance, base URL, JWT injection, timeout, 401 handler, JSON/form/blob helpers. |
| `frontend/src/api/auth.ts` | Register, login, `me`, activation, verification, resend, address change. |
| `frontend/src/api/users.ts` | User list/detail, approval, creation, deletion, mail resend, roles. |
| `frontend/src/api/lands.ts` | Land list/detail/create/update, submit/revise/verify/reject, assessment, documents, eligibility, history, change requests. |
| `frontend/src/api/offers.ts` | Offer list/detail/create/counter/accept/reject/withdraw/admin decisions/history. |
| `frontend/src/api/contracts.ts` | Contract list/detail/create/sign/review/terminate, reviews, versions, revisions and decisions. |
| `frontend/src/api/criteria.ts` | Criteria history/create. |
| `frontend/src/api/reference.ts` | Regions, grids, governorates, document types, current criterion. |
| `frontend/src/api/errors.ts` | API error normalization and type guards. |
| `frontend/src/api/knownMessages.ts` | Stable backend error-code to translation mapping. |

### 4.3 Components, layouts, i18n and theme

| File/directory | Purpose |
| --- | --- |
| `frontend/src/layouts/AppShell.tsx` | Authenticated application shell/navigation and role-aware menu. |
| `frontend/src/layouts/PublicLayout.tsx` | Public auth/activation/error layout. |
| `frontend/src/components/AppLogo.tsx` | Shared brand mark. |
| `frontend/src/components/LanguageToggle.tsx` | Arabic/English switch and direction handling. |
| `frontend/src/components/FullPageState.tsx` | Loading/error/empty full-page state. |
| `frontend/src/components/RouteFallback.tsx` | Lazy-route suspense fallback. |
| `frontend/src/components/LandStatusTag.tsx` | Land status presentation. |
| `frontend/src/components/OfferStatusTag.tsx` | Offer status presentation. |
| `frontend/src/components/ContractStatusTag.tsx` | Contract status presentation. |
| `frontend/src/components/VerificationResendForm.tsx` | Shared resend-verification form. |
| `frontend/src/i18n/index.ts` | i18next setup and language/direction handling. |
| `frontend/src/i18n/locales/en.ts`, `ar.ts` | English and Arabic UI strings. |
| `frontend/src/styles/global.css` | Global layout, RTL and visual styles. |
| `frontend/src/theme/AppTheme.tsx`, `antd.ts`, `tokens.ts` | Ant Design theme, design tokens and RTL-aware provider. |

### 4.4 Pages

| Page | User/role | Purpose |
| --- | --- | --- |
| `pages/auth/LoginPage.tsx` | Public | Sign in and redirect to an allowed route. |
| `pages/auth/RegisterPage.tsx` | Public | Landlord/Investor registration and verification notice. |
| `pages/auth/VerifyEmailPage.tsx` | Public | Registration/address verification from URL fragment token. |
| `pages/auth/ActivateAdminPage.tsx` | Public | Admin activation and self-set password. |
| `pages/auth/ConfirmEmailChangePage.tsx` | Public | Confirm an authenticated address change. |
| `pages/auth/AuthAside.tsx`, `verificationToken.ts` | Public auth | Shared auth visual panel and fragment-token helper. |
| `pages/app/DashboardPlaceholder.tsx` | Authenticated | Overview landing page. |
| `pages/app/AccountPage.tsx` | Authenticated | Profile, password/address and verification state. |
| `pages/lands/LandListPage.tsx` | Landlord | Own land queue. |
| `pages/lands/LandFormPage.tsx` | Landlord | Create/edit land. |
| `pages/lands/LandDetailPage.tsx` | Landlord/Admin | Land detail, status, evidence and actions. |
| `pages/lands/LandDocumentsPanel.tsx` | Landlord/Admin | Upload, download and review land documents. |
| `pages/lands/LandChangeRequestPanel.tsx` | Landlord/Admin | Submit and review approved-land change requests. |
| `pages/lands/useLandEvidence.ts`, `landFormat.ts`, `landMessages.ts`, `criteriaFormat.ts`, `gridFormat.ts` | Shared | Land evidence state, formatting, messages, criteria display and grid-name resolution from the reference list. |
| `pages/investor/DiscoverLandsPage.tsx` | Investor | Browse verified/visible lands. |
| `pages/investor/DiscoverLandDetailPage.tsx` | Investor | Investor land detail and offer entry. |
| `pages/offers/MyOffersPage.tsx` | Investor | Own offers and status filters. |
| `pages/offers/LandOffersPage.tsx` | Landlord/Admin | Offers on one land. |
| `pages/offers/OfferDetailPage.tsx` | Parties/Admin | Negotiation, versions, accept/reject/admin decision. |
| `pages/contracts/ContractsListPage.tsx` | Parties/Admin | Contract list. |
| `pages/contracts/ContractDetailPage.tsx` | Parties/Admin | Signatures, review, termination, versions, revisions and consents. |
| `pages/admin/AdminLandQueuePage.tsx` | Admin/SuperAdmin | Land verification queue. |
| `pages/admin/AdminLandReviewPage.tsx` | Admin/SuperAdmin | Land detail, assessment, document and verification actions. |
| `pages/admin/AdminOffersPage.tsx` | Admin/SuperAdmin | Offers pending admin approval. |
| `pages/admin/AdminUsersPage.tsx` | Admin/SuperAdmin | User list, approval, invitation and verification actions. |
| `pages/admin/AdminCriteriaPage.tsx` | Admin/SuperAdmin | Criteria history and version creation. |
| `pages/admin/AdminChangeRequestsPage.tsx` | SuperAdmin | Pending land change requests and decisions. |
| `pages/shared/ForbiddenPage.tsx`, `NotFoundPage.tsx` | Public/authenticated | 403 and 404 states. |

### 4.5 Types and utilities

| File | Purpose |
| --- | --- |
| `frontend/src/types/api.ts` | Shared API envelope/list types. |
| `frontend/src/types/auth.ts` | Auth user/session/role types. |
| `frontend/src/types/lands.ts` | Land, assessment, document, change-request types. |
| `frontend/src/types/offers.ts` | Offer/version/history types. |
| `frontend/src/types/contracts.ts` | Contract/review/version/revision types. |
| `frontend/src/types/users.ts` | User/role/approval types. |
| `frontend/src/utils/approvalTime.ts` | UTC/display-time conversion for approval screens. |
| `frontend/src/utils/saveBlob.ts` | Browser download helper. |
| `frontend/src/vite-env.d.ts` | Vite environment typing. |

### 4.6 Frontend build configuration

| File | Purpose |
| --- | --- |
| `frontend/package.json` | Node >=22.12, React 19, Ant Design 6, React Query, Axios, i18next, Vite 8, TypeScript 5.9. Scripts: `dev`, `build`, `preview`, `lint`, `typecheck`. |
| `frontend/package-lock.json` | Deterministic dependency lock. |
| `frontend/index.html` | Vite HTML shell. |
| `frontend/vite.config.ts` | React plugin, `@` alias, dev proxy to `http://localhost:5181`, production CSP injection. |
| `frontend/vite.https.config.ts` | Local-only HTTPS wrapper for the same dev server; the certificate comes from `NIBRAS_HTTPS_PFX_PATH`/`NIBRAS_HTTPS_PFX_PASSPHRASE` and no key is stored in the repository. |
| `frontend/devApiTarget.ts` | Development proxy target rules; loopback-only `f1-acceptance` test mode. |
| `frontend/tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json` | TypeScript project configuration. |
| `frontend/eslint.config.js` | ESLint flat configuration. |
| `frontend/public/favicon.svg` | Static favicon. |

---

## 5. Tools, tests and documentation

### 5.1 Clean-database tool

| Path | Purpose |
| --- | --- |
| `tools/Nibras.CleanDbMigrator/Program.cs` | Command entry point and exit codes. |
| `tools/Nibras.CleanDbMigrator/MigratorArguments.cs` | Argument parsing and confirmation flags. |
| `tools/Nibras.CleanDbMigrator/CleanDatabaseMigrator.cs` | Guarded create/migrate/verify workflow using EF migrations. |
| `tools/Nibras.CleanDbMigrator/CleanDatabaseInspector.cs` | Read-only state inspection and verification. |
| `tools/Nibras.CleanDbMigrator/CleanDatabaseState.cs` | Inspected database state model. |
| `tools/Nibras.CleanDbMigrator/CleanDatabaseRefusalException.cs` | Fail-closed refusal type. |
| `tools/Nibras.CleanDbMigrator/README.md` | Exact target, safety rules, usage and policy caveat. |

This tool is in the solution and is the supported way to create or verify the clean baseline. It
must not be confused with the web host; the host never calls it.

### 5.2 Tests

| Path | Classification | Purpose |
| --- | --- | --- |
| `tests/NibrasWeb.F1.Preparation/` | Retain as test tooling | Static checks for production hardening; gated SQL acceptance against `NIBRAS_F1_Verify`; loopback fake-SMTP acceptance; offline EF/approval-time mocks. Not in the web solution. |
| `docs/tests/scripts/run_regression.ps1` | Retain | Manifest-driven regression runner with protected-file and cleanup checks. |
| `docs/tests/scripts/regression_manifest.psd1` | Retain | 12-suite order, accepted database and protected hashes. |
| `docs/tests/scripts/s4_app.ps1`, `s4_tests.ps1`, `s4_admin.ps1`, `s4_review.ps1`, `s4_review2.ps1`, `s4_regression.ps1`, `s4_c4_acceptance.ps1`, `s4_checkpoint_a.ps1`, `s5_offers.ps1`, `s6_contracts.ps1`, `s7_reservations.ps1`, `s7_status_filter.ps1`, `p2_change_requests.ps1`, `parallel-approval.ps1` | Retain | Manifest-driven functional/security regression suites. `s4_checkpoint_a.ps1` is deliberately evidence-only, not a green gate. |
| `docs/tests/scripts/step3_suite.ps1`, `step3_ratelimit.ps1` | Retain | Step-3 auth/rate-limit suites. |
| `docs/tests/scripts/test_harness.ps1`, `test_fixtures.ps1`, `test_target.ps1`, `verification_helpers.ps1`, `static_contract_checks.ps1` | Retain | Harness, fixtures, target guard, cleanup and static contract helpers. |
| `docs/tests/scripts/f1_*.ps1`, `cleanup_mock_checks.ps1`, `verification_request_mock_checks.ps1`, `migration_8_to_21_offline_checks.ps1` | Retain as F1/offline test tooling | F1 acceptance and offline safety checks; tied to the F1 increment and, for SQL modes, `NIBRAS_F1_Verify`. |
| `docs/tests/scripts/s4_preflight.ps1` | **PROTECTED** | Pre-Step-4 one-off artifact. Never run and never modify. Retain in the controlled source tree; exclude from the presentation ZIP. |
| `docs/tests/scripts/s4_probe.ps1`, `s4_page.ps1`, `s4_overflow.ps1` | Supplementary probes | Not in the regression manifest. Retain in source for reference; exclude from the presentation ZIP. |
| `docs/tests/verify-request-size.ps1`, `verify-swagger-export.ps1`, `verify-frontend-csp.ps1` | Retain | Static/export checks. |
| `docs/tests/PHASE2_MATRIX.md` | Retain test documentation | Change-request case matrix used by `p2_change_requests.ps1`. |
| `docs/tests/DISPOSABLE_DB_PREFLIGHT.md` | Retain test documentation | Disposable-database safety plan for the F1 harness. |
| `docs/tests/FORENSIC_INVENTORY_QUERIES.sql` | Test utility | Read-only inventory queries; no data mutation. Included for review. |
| `docs/tests/security-step*.http`, `step-*.http`, `step-2.sql` | Historical manual artifacts | Earlier phase request collections. Retained in source only; exclude from the ZIP. |
| `docs/tests/scripts/README.md` | Retain test documentation | Script inventory, prerequisites and the package limitation for the protected preflight. |

The former F1 historical reports, the one-off governance SQL, the two Phase 1 preparation
projects and the root `docs/tests/parallel-approval.ps1` wrapper are no longer in the tree. They
were archived on 24 October 2026; see section 10.

### 5.3 Documentation

| File | Purpose | Delivery |
| --- | --- | --- |
| `docs/NIBRAS_FILE_GUIDE.md` | This guide. | Include |
| `docs/05_SECURITY_REQUIREMENTS.md` | SEC-01..SEC-20 requirements and CWE threat map. | Include |
| `docs/SECURITY_TRACEABILITY.md` | Requirement-to-control/test traceability. | Include |
| `docs/EMAIL_VERIFICATION.md` | Activation, registration verification, address change, delivery and security notes. | Include |
| `docs/DOCUMENT_STORAGE.md` | Private document storage, validation, serving and hardening. | Include |
| `docs/CLEAN_DATABASE_PLAN.md` | Clean database construction, guards, migration and policy caveat. | Include |
| `docs/PRODUCTION_BOOTSTRAP_AND_HEALTH.md` | SuperAdmin bootstrap and health probes. | Include |
| `docs/PRODUCTION_READINESS_CHECKLIST.md` | Read-only production gates and known blockers. | Include |
| `docs/CODEX_HANDOFF.md` | Current architecture, database state and migration inventory handoff. | Include |
| `docs/api/swagger.json` | Exported API contract. | Include |
| `README.md` | Root setup, migration, startup, verification and package guide. | Include |
| `docs/tests/scripts/README.md` | Regression-script inventory, prerequisites and protected-file/package limits. | Include |

---

## 6. Authentication, roles and access

### 6.1 Roles

| Role | Main capabilities |
| --- | --- |
| `Landlord` | Register/verify, create and edit own lands, upload evidence, submit/revise, request corrections on approved lands, view offers on own lands, accept/reject/counter, sign contracts. |
| `Investor` | Register/verify, browse visible verified lands, make offers, counter, withdraw, accept/reject, sign contracts, track reservations. |
| `Admin` | Review first-submission lands, assess technical data, review documents, verify/reject, create criteria versions, approve/reject accepted offers, review/sign contracts, release capacity, manage users (except SuperAdmin-only operations). |
| `SuperAdmin` | Everything an Admin can do plus final review after a rejected-land resubmission, change-request decisions, criteria policy where SuperAdmin-only, user verification resend, contract-revision escalation, and bootstrap-only operations. |

The backend is the authorization authority. The React `RequireRole` guards only prevent
navigation to a page the role cannot use.

### 6.2 Session rules

- Login returns a JWT with user id, role, email and verification epoch claims.
- The token is validated against the current database user on every request. Deleted users,
  changed roles, unverified email, changed email and stale verification epochs fail closed.
- The fallback authorization policy requires an authenticated user. Anonymous endpoints are
  explicitly marked.
- Auth, activation, verification, resend and email-change endpoints have separate per-IP
  rate-limit windows.
- No credential, token or SMTP password is logged or stored in the repository.

---

## 7. Business lifecycles

### 7.1 Land

```text
Draft
  -> PendingVerification     (landlord submits)
  -> Verified                (Admin assesses and verifies)
  -> Rejected                (Admin rejects with reason)
  -> PendingSuperAdminReview (landlord corrects and resubmits a rejected land)
  -> Verified                (SuperAdmin final review)
  -> UnderContract           (a contract becomes active)
  -> Verified                (contract ends/rejects/releases)
```

Rules:

- Technical assessment (region, grid, slope, distance, irradiance, elevation) is an
  Admin/SuperAdmin action; unassessed lands cannot be verified.
- Required documents must be present and approved before verification.
- A rejected land's resubmission is reviewed by a SuperAdmin, not a regular Admin.
- An approved land with an open change request is hidden from investors while its status stays
  `Verified`; offers/contracts are refused while hidden.
- Location uniqueness is filtered to non-deleted, fully populated, assessed rows.

### 7.2 Offers

```text
PendingLandlordResponse
  -> PendingInvestorResponse (counter)
  -> PendingAdminApproval   (landlord accepts)
  -> Accepted               (Admin approves)
  -> Closed / Rejected / Withdrawn
```

- Every proposal is an immutable `OfferVersion`; `IS_CURRENT` marks the latest.
- Negotiation actions are append-only history.
- Only one active offer per investor/land is allowed by a filtered unique index.
- Admin approval reserves grid capacity and can close competing offers.
- The parallel-approval suite proves two overlapping approvals cannot oversubscribe the grid.

### 7.3 Contracts

```text
Accepted offer -> Contract (PendingSignatures)
  -> Active          (party signatures + Admin review)
  -> Terminated      (approved termination)
  -> Rejected        (review rejection, land returns to Verified)
  -> Cancelled       (SuperAdmin cancellation after escalated revision)
```

- Contract versions are immutable; the event timeline is append-only.
- Pre-signing revisions require party consent, Admin review and, when escalated, SuperAdmin
  decision. Only one open revision is allowed per contract.
- Rejection/termination/release returns capacity and restores land state through the same
  established path.
- Historical reviews, versions and revision consents remain queryable.

### 7.4 Change requests and documents

- A landlord can request a correction on an approved land; old/new values are immutable
  snapshots with reason, evidence and decision.
- Investors cannot see or act on the land while the request is pending.
- Documents are private files with random names outside `wwwroot`. Uploads validate
  signature/size; downloads require authentication and authorization. The `Development`
  metadata-only path is retained only for old suites; production requires a real private root.

### 7.5 Audit and history

- `LAND_STATUS_HISTORY` records land transitions.
- `OFFER_NEGOTIATION_HISTORY` records negotiation actions.
- `CONTRACT_REVIEWS` records review/termination/revision decisions.
- `CONTRACT_VERSIONS` records immutable contract snapshots/events.
- `CONTRACT_REVISIONS` and `CONTRACT_REVISION_CONSENTS` record revision workflow and consent.
- `USER_SECURITY_EVENTS` records account lifecycle/security events with actor snapshots.
- `LAND_ASSESSMENTS` records immutable technical assessments.
- The context's `EnsureAppendOnly` hook rejects updates/deletes of criteria, assessments and
  security events.

---

## 8. Build and run requirements

### 8.1 Prerequisites

- .NET SDK 10.0.x.
- SQL Server Express instance `SADEEN\SQLEXPRESS` (or the deployment's approved equivalent).
- Node.js >= 22.12 and npm for the frontend.
- `sqlcmd` only for the PowerShell test harness.
- A base64 JWT signing key of at least 32 bytes, stored outside the repository.

### 8.2 Required configuration

Use User Secrets or environment variables; do not commit values.

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Runtime database target. `appsettings.json` ships an empty placeholder. |
| `Jwt:Key` | Base64 signing key, >=32 decoded bytes. No value in `appsettings.json`. |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | JWT validation settings; issuer/audience/expiry are already templated. |
| `Seed:SuperAdminEmail`, `Seed:SuperAdminPassword` | Development-only SuperAdmin seed; refused in production-like environments. |
| `Documents:StorageRoot` | Private document root; mandatory and fully qualified outside Development. |
| `Email:*` | Delivery mode, SMTP settings, frontend base URL and token lifetimes. Production requires SMTP; Development may use disabled/outbox. |
| `ReverseProxy:*` | Only needed when a trusted proxy is used; disabled by default. |
| `AllowedHosts` | Must be explicit outside Development. |

### 8.3 Backend

```powershell
dotnet restore
dotnet build NibrasWeb.slnx -c Release
dotnet run --project NibrasWeb.csproj
```

Default development URL: `http://localhost:5181`. Swagger is available only in Development.
The root `README.md` documents the protected-database approval and the non-Development demo
configuration.

### 8.4 Frontend

```powershell
cd frontend
npm ci
npm run dev
```

The Vite dev server proxies `/api` to `http://localhost:5181`. Quality and production build:

```powershell
npm run typecheck
npm run lint
npm run build
```

The production build injects a strict CSP; `dist/` is generated output and is not source.

### 8.5 Clean database

The supported tool is:

```powershell
$env:NIBRAS_CLEAN_DB_CONNECTION = 'Server=SADEEN\SQLEXPRESS;Database=NIBRAS_cleanvirsion;Integrated Security=True;TrustServerCertificate=True;'
dotnet run --project tools/Nibras.CleanDbMigrator       # dry run
dotnet run --project tools/Nibras.CleanDbMigrator -- --apply
```

The tool refuses any other target, refuses a non-empty/partial target without separate
approval, applies the 21 migrations and verifies history. It never seeds. The `--apply` step
requires explicit approval for the target state.

---

## 9. Database baseline and retirement

This section is a read-only snapshot from the 23 October 2026 database audit, updated on
24 October 2026 after the separately approved legacy-database retirement. The cleanup and
packaging work did not restore, migrate, seed or modify the retained databases; the legacy drops
were the separate approved retirement operation.

### 9.1 Current databases

| DB_ID | Database | Created | Migrations | Business data (approx.) | Disposition |
| ---: | --- | --- | ---: | --- | --- |
| 10 | `NIBRAS_cleanvirsion` | 2026-10-23 22:00:34 | 21 | 3 users (placeholder, 1 SuperAdmin, 1 Admin), 0 business rows | **KEEP**; post-Gate-B clean baseline |
| 11 | `NIBRAS_restoreverify_b6_20261023_01` | 2026-10-23 23:00:09 | 21 | 1 placeholder user, 0 business rows | Keep until baseline restore is verified; then retirement candidate |
| 8 | `NIBRAS_F1_Verify` | 2026-10-20 21:53:47 | 20 | 765 users, 370 lands, 96 offers, 51 contracts | **DO NOT DROP** while retained F1 tests target it |

The legacy databases `NEBRASDB` (5), `NEBRASDB_Step1` (6), `NEBRASDB_Step3_Test` (7) and
`NEBRASDB_UpgradeRehearsal_20261023` (9) were dropped on 24 October 2026 under the separate
retirement approval. Their copy-only backups and the retirement log remain as evidence.

The three remaining user databases are owned by `SADEEN\sadoon`, are `ONLINE`, and use the
`SIMPLE` recovery model. System databases (`master`, `tempdb`, `model`, `msdb`) are retained.

No user database has an active application session, and there are no database snapshots. The
only active sessions observed were on `master`.

### 9.2 Preserved backups

These files are outside the repository and remain as recovery/evidence. SQL Server's backup
directory cannot be enumerated by the current Windows user, but `msdb.dbo.backupset` records the
paths and sizes. The baseline backup and the pre-restore safety backup must not be deleted or
moved until the restore and account acceptance are complete.

| Backup | Size | Type | Purpose |
| --- | ---: | --- | --- |
| `NIBRAS_cleanvirsion_full_copyonly_20261023T194235Z_191388c9202241099aad75074ab2a437.bak` | 4,743,168 | Copy-only full | **Clean baseline before synthetic TEST data** |
| `NIBRAS_cleanvirsion_post_gateb_copyonly_20261024T023815Z_be4a0f67.bak` | 5,398,528 | Copy-only full with checksums (backup set 12, GUID `23FECAB5-3380-446C-A1E8-1ACAEB48C69F`) | **Post-Gate-B baseline**: 21 migrations, 3 users, 0 business rows; `RESTORE VERIFYONLY` valid |
| `NEBRASDB_COPY_ONLY_20261023.bak` | 6,250,496 | Copy-only full | Recovery evidence for the dropped `NEBRASDB` |
| `NEBRASDB_Step1_pre_phase2.bak` | 9,265,152 | Copy-only full | Recovery evidence for the dropped `NEBRASDB_Step1` |
| `NEBRASDB_Step1_FULL_20261017_175104.bak` | 8,740,864 | Full | Older Step1 recovery evidence |
| `NEBRASDB.bak` | 6,443,008 | Full (latest of two history entries) | Older NEBRASDB recovery evidence |

The legacy databases were dropped on 24 October 2026; their backups are retained as evidence,
not as live recovery targets. `NIBRAS_restoreverify_b6_20261023_01` remains until the restored
target passes the same checks. Before any future drop of `NIBRAS_F1_Verify`, take a copy-only
backup if its F1 evidence must be retained.

### 9.3 Restoring the clean baseline and provisioning accounts

The authoritative, detailed procedure is **`docs/CLEAN_DATABASE_PLAN.md` §11**. Do not maintain a
second divergent procedure here. Summary only:

1. **Gate A — restore:** confirm the baseline backup and `msdb` history; confirm 0 sessions; take
   a fresh copy-only safety backup of the current `NIBRAS_cleanvirsion`; restore baseline #6 with
   `REPLACE, RECOVERY, CHECKSUM`; verify 21 migrations, 24 application tables / 263 columns /
   59 foreign keys / 98 indexes, 4 roles / 12 governorates / 2 document types / 1 land criterion,
   one non-operational placeholder user and zero business rows.
2. **Gate B — accounts (PASSED on 24 October 2026):** bootstrap exactly one SuperAdmin with
   `--bootstrap-superadmin`, then create exactly one Admin through `POST /api/users` with
   `roleId = 3`, then have the Admin set their own password through the single-use HTTPS
   activation link. Verified end state: exactly one operational SuperAdmin and exactly one
   operational Admin; no operational Landlord/Investor; no test lands, documents, offers or
   contracts.
3. Keep `NIBRAS_restoreverify_b6_20261023_01` until the restored database passes the same checks;
   then retire it only under separate approval.
4. Do not start the host in Development against the clean baseline. Development seeders add
   SuperAdmin/demo data; the clean-database guards refuse seeding on the protected target.
5. No direct SQL account creation or role manipulation, no demo seeder and no security bypass.

The `NIBRAS_cleanvirsion` currently in service **is** the clean baseline: after Gate A it held
the single non-operational placeholder user, and Gate B added exactly one operational SuperAdmin
and one operational Admin. It holds 3 users and zero business rows.

---

## 10. Cleanup record and unresolved candidates

### 10.1 Completed cleanup (24 October 2026)

Seven approved items were copied to an external evidence archive, verified, and removed from the
repository. No additional source deletion was performed.

| Path | Type | Reason | Disposition |
| --- | --- | --- | --- |
| `docs/tests/parallel-approval.ps1` | File | Obsolete root delegating wrapper; the maintained suite is under `docs/tests/scripts/`. | Archived and removed |
| `01_LOGICAL_ANALYSIS.md` | File | v1 analysis that contradicted the implemented React frontend and superseded models. | Archived and removed |
| `docs/tests/F1_CLOSURE_REPORT_2026-10-22.md` | File | Historical F1 closure evidence. | Archived and removed |
| `docs/tests/F1_RELEASE_HANDOFF_2026-10-22.md` | File | Historical F1 release evidence. | Archived and removed |
| `docs/tests/governance-additive-migration.sql` | File | One-off SQL with no current runner, project or migration reference. | Archived and removed |
| `tests/Nibras.Phase1.ApiHost/` | Directory (4 files) | Preparation-only host not in the solution and not referenced by a current runner. | Archived and removed |
| `tests/Nibras.Phase1.Tooling/` | Directory (18 files) | Preparation-only tooling with a future/unapproved `--execute` mode; superseded by the F1 harness. | Archived and removed |

Archive verification: 210 files, 51,888,836 bytes and zero SHA-256 mismatches. The archive is
outside the repository and is not part of the presentation ZIP. The previous
`C:\Users\sadoon\NibrasTestData\NIBRAS_Final.zip` remains unchanged.

### 10.2 Classification used for remaining files

| Class | Meaning | Current examples |
| --- | --- | --- |
| A - Required | Used by the application, build, migrations or runtime configuration. | Main project, controllers, services, models, DTOs, enums, migrations, frontend source, clean-database tooling, core docs. |
| B - Test | Maintained tests useful for regression. | `tests/NibrasWeb.F1.Preparation`, manifest-driven PowerShell suites, helpers and static checks. |
| C - Historical | Evidence or old phase material; retain outside delivery or exclude from the ZIP. | Old manual `.http`/`.sql` collections; archived F1 reports and Phase 1 projects. |
| D - Obsolete | Demonstrably unused, duplicate or temporary. | None remaining after the approved cleanup. |
| E - Uncertain | Do not delete without a separate decision. | `DeletionRequest` model; F1 scripts tied to `NIBRAS_F1_Verify`; non-manifest Step-4 probes. |
| Protected | Must never run or be modified. | `docs/tests/scripts/s4_preflight.ps1`. |

### 10.3 Unresolved candidates (no deletion authorized)

- `Models/DeletionRequest.cs`: no current service/controller use, but it is a mapped entity in
  the context, snapshot and migration history. Removing it would require a separate model and
  migration decision; **keep**.
- `docs/tests/scripts/s4_preflight.ps1`: protected; never run, copy or modify. Retained in the
  controlled source tree and excluded from the presentation ZIP.
- `docs/tests/scripts/s4_probe.ps1`, `s4_page.ps1`, `s4_overflow.ps1`: non-manifest
  supplementary probes. Retained in source and excluded from the ZIP.
- `docs/tests/security-step*.http`, `step-*.http`, `step-2.sql`: historical manual request
  collections. Retained in source only and excluded from the ZIP.
- F1 PowerShell scripts and `tests/NibrasWeb.F1.Preparation`: valuable regression tooling, but
  SQL/fake-SMTP modes target `NIBRAS_F1_Verify` and require separate approval. Keep; do not run
  gated modes without approval.
- `App_Data/land-documents/...pdf`: a test upload, not source. Excluded from the ZIP; do not
  commit it.
- Old non-authoritative databases and copy-only backups: operational retirement candidates, not
  repository files. No drop or restore is part of this delivery.

No additional file was deleted during documentation and packaging.

### 10.4 Historical audit preservation

- The seven removed paths are recorded in section 10.1 with their disposition.
- Broken Markdown links to the former F1 handoff in `DOCUMENT_STORAGE.md`,
  `EMAIL_VERIFICATION.md` and `SECURITY_TRACEABILITY.md` were replaced with a clear historical
  note that points to this cleanup record.
- The `CODEX_HANDOFF.md` Git section now points to the current `git status` rather than a stale
  exhaustive snapshot, while preserving the four pre-existing deletions and the approved cleanup.
- The old root parallel-approval wrapper is documented as archived; the only supported path is
  `docs/tests/scripts/parallel-approval.ps1`.
- No Git history was rewritten. All pre-existing uncommitted work is preserved.

---

## 11. Presentation ZIP design and verification

### 11.1 Artifact

| Item | Value |
| --- | --- |
| New presentation ZIP | `C:\Users\sadoon\NibrasTestData\NIBRAS_Final_Clean.zip` |
| Previous ZIP (preserved, not overwritten) | `C:\Users\sadoon\NibrasTestData\NIBRAS_Final.zip` |
| Previous ZIP SHA-256 | `814B24A287EEC6F459BE33AF5BD69A27FA760C02005E8C1FF398F21B51653FBE` |
| Package type | Source-only delivery; no build output, database, backup, upload or evidence |

### 11.2 Included

- Backend source, `NibrasWeb.csproj`, `NibrasWeb.slnx`, `Program.cs`, configuration template,
  properties and all security guards.
- All 21 migrations, generated designers and the model snapshot.
- Frontend source, `package.json`, lockfile, Vite/TypeScript/ESLint configuration and public
  assets, without `node_modules` or `dist`.
- `tools/Nibras.CleanDbMigrator` source and README.
- `tests/NibrasWeb.F1.Preparation` source and README, including the offline static/mock checks.
- Maintained PowerShell regression harness and test documentation, except the protected
  preflight and the non-manifest supplementary probes.
- Core architecture, security, email, storage, database, readiness and delivery documents.
- Root `README.md`, `.gitignore` files and `docs/api/swagger.json`.
- `DELIVERY_MANIFEST.txt` with relative path, byte size and SHA-256 for every included file.

### 11.3 Excluded

- Git metadata, IDE state, `bin/`, `obj/`, `node_modules/`, `dist/` and all build output.
- `NibrasWeb.csproj.user`, user secrets, environment files, credentials and private keys.
- `App_Data/` uploads, runtime document storage and test PDFs.
- SQL databases, backups, old delivery ZIPs, raw logs, screenshots, receipts and generated
  evidence.
- The seven archived cleanup items.
- Old manual `.http`/`.sql` collections and `step-2.sql`.
- Protected `docs/tests/scripts/s4_preflight.ps1`.
- Non-manifest probes `s4_probe.ps1`, `s4_page.ps1` and `s4_overflow.ps1`.

### 11.4 Verification

- The ZIP is expanded to a clean temporary directory and must open without error.
- Every entry in `DELIVERY_MANIFEST.txt` is re-hashed; path, size and SHA-256 must match.
- Required files are checked explicitly, including all 21 migrations, guards, frontend entry
  points, README, Swagger and regression manifest.
- Excluded paths and file types are checked to confirm they are absent.
- Included text/configuration files are scanned for live secrets, private keys, connection
  passwords and cloud tokens. Test-only fixture literals are reported separately as
  non-production values.
- The previous ZIP's SHA-256 is rechecked to confirm it was not overwritten.
- The final ZIP SHA-256 is written to a sidecar `.sha256` file and reported with the delivery.

The full `run_regression.ps1` manifest pins the protected `s4_preflight.ps1`. Because that file is
excluded by policy, complete manifest runs are performed in the controlled source repository;
individual suites and offline checks remain available for review from the package.

---
## 12. Recommended practical demonstration order

1. **Start from a known state.** Show the clean baseline checks (21 migrations, 3 users =
   1 placeholder + 1 SuperAdmin + 1 Admin, 0 lands/offers/contracts) or explicitly state that
   the demo database is a disposable seeded copy.
2. **Sign in as SuperAdmin/Admin.** Show the role-aware shell, account page, user list and
   reference data (roles, governorates, regions, grids, criteria, document types).
3. **Register a Landlord.** Show registration, verification notice, token-link verification and
   login gating.
4. **Create and submit a land.** Show required fields, location, document upload, validation and
   `Draft -> PendingVerification`.
5. **Assess and verify as Admin.** Show technical assessment, document review, eligibility,
   `PendingVerification -> Verified`, and audit/history.
6. **Register/approve an Investor.** Show approval, visible verified land discovery and hidden
   land behavior.
7. **Make an offer.** Show offer creation, version, counter-offer and negotiation history.
8. **Accept and obtain admin approval.** Show grid capacity reservation and competing-offer
   closure.
9. **Create and sign a contract.** Show party signatures, Admin review, `PendingSignatures ->
   Active`, contract versions and reviews.
10. **Demonstrate a pre-signing revision.** Show party consent, Admin decision, escalation and
    SuperAdmin decision, and the immutable version/review history.
11. **Demonstrate a land change request.** Show submission on an approved land, investor hiding,
    evidence correction and SuperAdmin decision.
12. **Show the audit trail and status history.** End with land/offer/contract/security histories
    and the clean-database/health checks.

Keep external SMTP out of the live demo unless the operator has approved credentials; use the
Development outbox or `Email:DeliveryMode=Disabled` for a controlled walkthrough. State clearly
that fake-SMTP/outbox success is not production delivery evidence.

---

## 13. Known limits and open decisions

- Mailtrap SMTP delivery was validated during Gate B; the final browser walkthrough sends no
  mail, so sandbox delivery is not production delivery evidence.
- `NIBRAS_cleanvirsion` is the post-Gate-B clean baseline (3 users, 0 business records). A full
  Landlord/Investor/land/offer/contract demonstration needs a disposable seeded copy or a
  separate authorization to create demo records.
- `NIBRAS_F1_Verify` is still a dependency of the retained F1 test harness and cannot be dropped
  until those tests are retired or retargeted.
- The `LAND_CRITERIA` seed is an illustrative placeholder, not approved production policy.
- The legacy `DeletionRequest` entity remains mapped for schema fidelity; do not remove it
  without a separate migration decision.
- The Git worktree remains intentionally dirty; no reset, clean, stash, rebase, commit or
  destructive Git operation is part of this cleanup.
- The seven approved cleanup items are archived; no additional source deletion was performed.
- No database retirement, restore, migration, seed or DDL was performed.
- The new source-only presentation ZIP is `NIBRAS_Final_Clean.zip`; the previous
  `NIBRAS_Final.zip` is preserved and was not overwritten.
- Full PowerShell manifest runs require the protected `s4_preflight.ps1` in the controlled source
  repository; the protected file is intentionally not in the presentation ZIP.
