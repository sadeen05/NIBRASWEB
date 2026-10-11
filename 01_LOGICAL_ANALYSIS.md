# NIBRAS — Logical Analysis (v1, 2026-10-03)

Source of truth for code: repo `sadeen05/NIBRASWEB`, base commit `07ecb39`, working branch `wip/enums-refactor`.
Marks: **[V]** = read directly in code/migrations. **[A]** = design decision or assumption (changeable, see `02_BUILD_PLAN.md` §6).
Deadline: **18 Oct 2026**.

---

> **AMENDMENT A (9 Oct 2026): overrides any conflicting text below.**
> - A1. A React frontend is required. The API must stay stable and its Swagger JSON is exported after every backend step.
> - A2. REMOVED from the MVP (do not build): `Land.OwnershipType`, `Land.LandUseType`, `DocumentType.RequiredWhen` (replaced by a bool `DocumentType.IsRequiredForVerification`), `LandCriterion.LandUseChangeFeePerM2`, `LandCriterion.EnvironmentalReviewThresholdMw`, reservation `ExpiresAt` and any expiry logic (BR-G4, HoldDays), `ProjectApproval` (optional extra only if time remains after Step 7), the cancellation request/respond/dispute flow, force-terminate, all background services, `DELETE /api/lands/{id}`.
> - A3. Contracts MVP = create, sign, review (approve/reject), terminate (Admin). BR-K5 and the force-terminate rows are out.
> - A4. Verification (BR-V2) requires: the latest version of every document type with `IsRequiredForVerification = true` is Approved, AND eligibility passes, AND a current criteria version exists.
> - A5. A reservation is released when its contract is Rejected or Terminated (the admin release endpoint comes in Step 7).

## 1. Purpose and scope

NIBRAS is a platform where Jordanian landowners list land for solar projects, admins verify the land, and verified investors negotiate and sign partnership contracts. The platform also tracks the official approvals a project needs after signing.

**In scope for 18 Oct:** user accounts + JWT security, land registration and verification (documents + versioned criteria), offers and negotiation, grid-capacity soft hold, contracts (sign, review, cancel, terminate), project-approval tracking, Swagger + demo data.
**Out of scope until after delivery:** ROI/tariff calculator, dashboards, real file upload (FilePath stays a string), notifications, payments, frontend (unless required, see plan §2).

## 2. Actors

| Role (RoleNames) | Id | Can do |
|---|---|---|
| Landlord | 1 | register own lands, upload documents, respond to offers, sign contracts |
| Investor | 2 | after admin approval: browse Verified lands, make offers, negotiate, sign |
| Admin | 3 | verify lands/documents/investors, approve offers, create/review contracts, manage criteria, update project approvals |
| SuperAdmin | 4 | everything Admin can + force-terminate contracts |

System user Id = -1 (seeded) is used by background jobs. [V]

## 3. Current state: verified defects

| ID | Defect | Evidence [V] | Fixed in |
|---|---|---|---|
| D-01 | `LandStatusHistory` has two status columns; migration `20260801212633_DropOfferStatusTable` added NOT NULL `LandStatusId` with FK, but services write only `StatusId` → insert fails FK (derived from reading, not executed) | Models/LandStatusHistory.cs, that migration, LandService.cs | Step 1 |
| D-02 | Eligibility checks 3 of 5 criteria (area, slope, grid distance). HEAD checked all 5 | LandService.CheckEligibilityAsync / ValidateRegisterStandards | Step 4 |
| D-03 | Verification cannot succeed: needs approved `TitleDeed` document and a `LandCriterion` row; their controllers/services were deleted and nothing is seeded | LandService.VerifyAsync; deleted files vs HEAD | Steps 2, 4 |
| D-04 | `Contract.Status` default is `Active` (DbContext `HasDefaultValue`, migration default 2). EF warning 20601 confirms: no sentinel configured, so a value of 0 silently uses the DB default (Active) | NebrasdbContext.cs:68, DropOfferStatusTable migration, EF warning 20601 | Step 1 (remove the default entirely) |
| D-05 | No authentication: 0 `[Authorize]`, no `AddAuthentication`; `PasswordHash = "default_pass"` | Program.cs, UserService.cs | Step 3 |
| D-06 | Identity comes from request (userId/adminId/landlordId in body, query or route, e.g. `GET /api/offers/land/{landId}/user/{requestingUserId}`) | Controllers | Steps 4–6 |
| D-07 | `GridCapacityReservationsController` is open CRUD and bypasses the workflow | Controller | Step 7 |
| D-08 | `OfferService.AdminApproveAsync`: capacity check + reservation insert not in one transaction | OfferService.cs | Step 5 |
| D-09 | No DB constraint for "one `IsCurrent` OfferVersion per offer" | NebrasdbContext.cs | Step 1 |
| D-10 | `POST /api/offers/{id}/close` is public; `MarkOfferClosedAsync` only checks that a contract with that id exists | OffersController, OfferService | Step 5 |
| D-11 | `LandService.UpdateAsync`: no owner check, no status check, Region change does not update Grid, editing a verified land keeps it verified | LandService.cs | Step 4 |
| D-12 | `LandService.GetAllAsync` returns all non-deleted lands regardless of status | LandService.cs | Step 4 |
| D-13 | `CreateAsync` rejects a land when the region's grid has no spare capacity (a land reserves nothing); grid is not validated against region | LandService.cs | Step 4 |
| D-14 | Enum members `Reopened`, `AutoClosed` never used; `RelatedToRejectedOfferId` never set; status `Closed` blocks new offers on a land forever | OfferService.cs, Enums | Step 5 |
| D-15 | `OfferVersion.ExpectedAnnualRevenue/EffectiveCostPerKw/PaybackPeriodMonths` are not computed (ROI service deleted; it exists only in HEAD). `EffectiveCostPerKw` is actually per kWh | OfferService.CreateAsync, HEAD OfferVersionService | After delivery |
| D-16 | No validation attributes on `CreateLandRequest`, `CreateOfferRequest`, `UpdateLandRequest` | DTOs | Steps 4–7 |
| D-17 | HEAD `LandCriterionService.UpdateAsync` edits rows in place and does not set `UpdatedAt` — do NOT restore as is | `git show 07ecb39:Service/LandCriterionService.cs` | Step 4 |
| D-18 | Global query filter `!IsDeleted` on `User` (`NebrasdbContext.cs:474`). EF warning 10622 lists 9 dependent entities: Contract, ContractReview, DeletionRequest, Land, LandCriterion, LandStatusHistory, Offer, OfferNegotiationHistory, OfferVersion. Rows that depend on a soft-deleted user can silently disappear from joins (including lands and offers) | EF warnings printed by `has-pending-model-changes` (Step 1 session) | Step 3 (task 3.10) |
| D-19 | Project does not build in the developer's environment: `Controllers\OffersController.cs(116,1): error CS0116`. The ZIP copy of that file looks syntactically valid, so the local file differs; cause unknown | Step 0 report | Step 0.5 |

Not verified (agent must inspect before touching): tail of `GridCapacityReservationService.UpdateAsync`; bodies of `CounterOfferAsync/AcceptAsync/RejectAsync/WithdrawAsync`.

## 4. Target domain model (changes to the working tree)

| Entity | Change |
|---|---|
| `LandStatus` | Table → **enum** `LandStatus`: Draft=1, PendingVerification=2, Verified=3, Rejected=4, UnderContract=5. Keep numeric values so existing data stays valid. Remove `LandStatuses` table, `LandStatusNames`. |
| `LandStatusHistory` | ONE status column `Status` (enum) mapped to `STATUS_ID`; drop `LandStatusId` column + FK. |
| `Land` | + `Village`, `Basin`, `ParcelNumber` (strings), + `OwnershipType` (Sole=1, Shared=2), + `LandUseType` (Agricultural=1, NonAgricultural=2, Unknown=3). New unique index (RegionId, Village, Basin, ParcelNumber) for non-deleted lands. Keep `LandNumber` as is. [A] field names to be validated against a real title deed. |
| `LandCriterion` | Immutable **versions**. Thresholds become nullable (MinAreaDonum, MaxSlopePct, MaxGridDistanceKm, MinSolarIrradiance, MinElevationM). + `Reason` (required), + `LandUseChangeFeePerM2?`, + `EnvironmentalReviewThresholdMw?`. `UpdatedById/UpdatedAt` are kept and now mean "version created by/at". |
| `DocumentType` | + `RequiredWhen` enum: Always=1, WhenShared=2, WhenAgricultural=3, Optional=4. Seed 4 rows (names pending validation): `TitleDeed`(Always), `EncumbranceStatement`(Always), `PartnersConsent`(WhenShared), `LandUseChangeApproval`(WhenAgricultural). |
| `LandDocument` | + `ReviewedById?`, `ReviewedAt?`, `ReviewNote?`. `Status` stays a string with constants class (`Pending/Approved/Rejected`) — no data migration. |
| `User` | + `IsApproved` (bool), `ApprovedById?`, `ApprovedAt?`, `CompanyName?`, `CompanyRegistrationNumber?`. Real password hash. |
| `OfferVersion` | Filtered unique index: one `IsCurrent = 1` per `OfferId`. |
| `GridCapacityReservation` | + `ExpiresAt?`, `ReleasedAt?`, `ReleaseReason?`. Release = set `ReleasedAt` (no delete). Counts toward capacity only while `ReleasedAt IS NULL` and (`ExpiresAt IS NULL` or in the future). |
| `Contract` | Default status removed (set explicitly to `PendingSignatures`). + `CriterionId?` FK → LandCriterion. |
| `ContractReview` | `Decision` stays a string with constants class (values read from HEAD `ContractService`). Append-only. |
| `ProjectApproval` (new) | `Id, ContractId, ApprovalType, Status, ReferenceNumber?, SubmittedAt?, DecidedAt?, Notes?, UpdatedById?, UpdatedAt`. Types: GenerationLicense=1, GridConnection=2, BuildingPermit=3, EnvironmentalClearance=4. Status: NotStarted=1, Submitted=2, Approved=3, Rejected=4. |

## 5. Business rules (each one must be testable)

**Land**
- BR-L1 Only the owner (token user) creates/edits/submits/deletes their land.
- BR-L2 Editable only in Draft or Rejected. A Rejected land returns to Draft through explicit `revise`. Any edit when Verified/UnderContract/PendingVerification → 409.
- BR-L3 Submit only from Draft; requires Village, Basin, ParcelNumber, Area, Slope, DistanceToGrid, Irradiance, Elevation, and at least one `TitleDeed` upload.
- BR-L4 `GridId` must belong to `RegionId`; changing region requires a new grid in the same request.
- BR-L5 Parcel unique per (Region, Village, Basin, ParcelNumber) among non-deleted lands.
- BR-L6 Creating a land does not check grid capacity.

**Verification**
- BR-V1 Only Admin verifies/rejects, only from PendingVerification.
- BR-V2 Verify requires: required documents' latest version Approved (Always; WhenShared if Shared; WhenAgricultural if Agricultural) AND eligibility passes against the current criteria version AND a current version exists.
- BR-V3 On verify: Status=Verified, `DataVerifiedByAdmin=true`, `VerifiedAgainstCriterionId` = current version, history row with admin id.
- BR-V4 Reject requires a reason; history row.
- BR-V5 Eligibility is ONE function returning per-criterion results `{criterion, required, actual, passed|notEnforced}`. A null threshold is `notEnforced` (not a failure). Used by create, submit, verify and `GET eligibility`.

**Documents**
- BR-D1 Owner uploads; version increments per (land, type); newest version supersedes. Admin review sets Approved/Rejected + note; review never changes `FilePath` or `DocumentTypeId`.
- BR-D2 Uploads allowed only in Draft, Rejected, PendingVerification.

**Criteria**
- BR-C1 Versions are immutable: no PUT/DELETE. Admin `POST` creates a new row; `Reason` required; creator from token; current = newest.
- BR-C2 A new version never changes the status of existing lands or contracts (they keep `VerifiedAgainstCriterionId` / `CriterionId`).
- BR-C3 Thresholds are nullable; null = not enforced and shown as such.

**Users**
- BR-U1 Public register allows Landlord or Investor only. Landlord `IsApproved=true`; Investor `IsApproved=false`. Admin/SuperAdmin are created by the dev seeder or by SuperAdmin (Admin role only); nobody creates SuperAdmin via API.
- BR-U2 Investor needs `IsApproved` to browse lands and create offers. Admin approves.
- BR-U3 Soft-deleted users cannot log in.
- BR-U4 Investor approval requires CompanyName + CompanyRegistrationNumber (licensing needs a Jordan-registered company, see §9).

**Offers**
- BR-O1 Create: approved Investor; land is Verified; investor is not the land owner; capacity > 0; amount > 0; valid `ConnectionMechanism`.
- BR-O2 One active (non-terminal) offer per investor per land. Terminal = RejectedBy*, WithdrawnByInvestor, Closed. The old "Closed blocks the land forever" rule is replaced by the land status check.
- BR-O3 Only the party whose turn it is can counter/accept/reject. Investor may withdraw before PendingAdminApproval.
- BR-O4 Each counter creates `OfferVersion` N+1, previous `IsCurrent=false`, exactly one current (DB-enforced).
- BR-O5 Accept → PendingAdminApproval, `AcceptedVersionId` = current version.
- BR-O6 Admin approve runs in ONE serializable transaction: offer is PendingAdminApproval; land is Verified; sum(active reservations) + RequiredCapacityMw ≤ Grid.CapacityMw; create reservation (`ReservationType="Offer"`, `ExpiresAt = now + HoldDays`); offer → Accepted; land → UnderContract (+history); all other active offers on the land → Closed (+history `CompetitorClosed`).
- BR-O7 Admin reject → RejectedByAdmin with reason.
- BR-O8 Every action writes `OfferNegotiationHistory` (existing action types).
- BR-O9 Offers visible only to the two parties and Admin.

**Contracts**
- BR-K1 Admin creates a contract only from an Accepted offer with no live contract. Sets `OfferVersionId=AcceptedVersionId`, copies Land/Investor/Landlord, `CriterionId = land.VerifiedAgainstCriterionId`, `Status=PendingSignatures`, links the existing reservation (`ContractId`), offer → Closed (`ClosedByContract`). Land stays UnderContract.
- BR-K2 Each party signs once, only while PendingSignatures, and only as themselves.
- BR-K3 Admin review only when PendingSignatures AND both signed. Approve → Active (+ create 4 `ProjectApproval` rows NotStarted). Reject (reason required) → Rejected, release reservation, land → Verified.
- BR-K4 Review does NOT re-evaluate eligibility against newer criteria and does NOT create a reservation.
- BR-K5 Cancellation (port HEAD logic exactly; report any ambiguity before changing): Active only; a party requests with reason (investor may carry a penalty); effective date = request + `NoticePeriodDays`; other party agrees or disputes (`DisputeFlagged`); a background job terminates at the effective date when not disputed (acts as system user -1).
- BR-K6 Admin `terminate` / SuperAdmin `force-terminate` (justification, optional compensation) → Terminated, release reservation, land → Verified, `ContractReview` row.
- BR-K7 `ContractReview` is an append-only log.

**Reservations and approvals**
- BR-G1 A reservation is a soft hold (the platform cannot guarantee real grid capacity, see §9).
- BR-G2 Released on: contract Rejected/Terminated, admin release, expiry.
- BR-G3 Only workflow code creates reservations; the controller is read-only + release for Admin.
- BR-G4 When the `GridConnection` approval becomes Approved, set `ExpiresAt = NULL` (hold becomes firm).
- BR-P1 Only Admin updates `ProjectApproval`; every update stamps `UpdatedById/At`. Parties read only.

**Security**
- BR-S1 Identity only from the JWT. No actor id (userId/adminId/landlordId/investorId) in any request DTO, query or route.
- BR-S2 Passwords hashed with BCrypt. JWT key and seed credentials in `dotnet user-secrets`/env vars. No secrets in the repo (it is public).
- BR-S3 Global fallback policy = authenticated; `[AllowAnonymous]` only on register/login.
- BR-S4 Status codes: 400 validation, 401 no token, 403 wrong role/not owner, 404 not found, 409 illegal state. Reuse the existing `ExceptionHandler.cs`.

## 6. State machines (target)

**Land**

| From | Event | Actor | To |
|---|---|---|---|
| — | create | Landlord | Draft |
| Draft | submit | Owner | PendingVerification |
| PendingVerification | verify | Admin | Verified |
| PendingVerification | reject (reason) | Admin | Rejected |
| Rejected | revise | Owner | Draft |
| Verified | offer approved | System | UnderContract |
| UnderContract | contract Rejected/Terminated | System | Verified |

**Offer** (as implemented in `OfferService`; verify before changing)

| From | Event | Actor | To |
|---|---|---|---|
| — | create | Investor | PendingLandlordResponse |
| PendingLandlordResponse | counter | Landlord | PendingInvestorResponse |
| PendingInvestorResponse | counter | Investor | PendingLandlordResponse |
| either pending | accept | party in turn | PendingAdminApproval |
| either pending | reject | party in turn | RejectedByLandlord / RejectedByInvestor |
| either pending | withdraw | Investor | WithdrawnByInvestor |
| PendingAdminApproval | approve | Admin | Accepted |
| PendingAdminApproval | reject | Admin | RejectedByAdmin |
| Accepted | contract created | System | Closed |
| any active | another offer approved on same land | System | Closed |

**Contract**

| From | Event | Actor | To |
|---|---|---|---|
| — | create (from Accepted offer) | Admin | PendingSignatures |
| PendingSignatures | sign | each party | (stays until both signed) |
| PendingSignatures | review approve | Admin | Active |
| PendingSignatures | review reject | Admin | Rejected |
| Active | cancellation request | a party | Active (notice period starts) |
| Active | respond agree / dispute | other party | Active (agree: ends at effective date; dispute: flagged) |
| Active | effective date reached, not disputed | System job | Terminated |
| Active | terminate | Admin | Terminated |
| Active | force-terminate | SuperAdmin | Terminated |

Rule: each state machine lives in ONE service method set; controllers never set status directly.

## 7. Authorization matrix

| Operation | Who | Ownership/extra check |
|---|---|---|
| Register / login | anonymous | role limited to Landlord/Investor |
| Create / edit / submit / revise land, upload docs | Landlord | token user owns land; status rules BR-L2 |
| Browse lands | Investor (approved) → Verified only; Landlord → own; Admin → all | — |
| Review document, verify, reject land | Admin | — |
| Manage criteria / document types / regions / grids | Admin | token user recorded |
| Approve investor | Admin | — |
| Create offer | approved Investor | land Verified, not own land |
| Counter / accept / reject | party in turn | party of this offer |
| Withdraw | Investor | owner of offer |
| Admin approve / reject offer | Admin | — |
| Read offer or history | party or Admin | — |
| Create contract, review, terminate | Admin | — |
| Sign | party | must be that party |
| Cancellation request / respond | parties | party of this contract |
| Force-terminate | SuperAdmin | — |
| Update project approval | Admin | — |
| Reservations | Admin read + release | — |

## 8. Target API

Auth: `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`.
Users: `GET /api/users` (Admin), `GET /api/users/{id}` (Admin), `POST /api/users/{id}/approve` (Admin), `DELETE /api/users/{id}` (Admin, soft).
Reference data: `GET /api/regions|grids|roles|document-types` (auth); writes on regions/grids/document-types are Admin; roles read-only.
Criteria: `GET /api/criteria/current` (auth), `GET /api/criteria/history` (Admin), `POST /api/criteria` (Admin). No PUT/DELETE.
Lands: `GET /api/lands` (role-filtered, paged), `GET /api/lands/{id}`, `POST /api/lands`, `PUT /api/lands/{id}`, `DELETE /api/lands/{id}` (Draft only, soft), `POST /api/lands/{id}/submit`, `POST /api/lands/{id}/revise`, `POST|GET /api/lands/{id}/documents`, `PUT /api/lands/{id}/documents/{docId}/review` (Admin), `GET /api/lands/{id}/eligibility`, `GET /api/lands/{id}/history`, `POST /api/lands/{id}/verify` (Admin), `POST /api/lands/{id}/reject` (Admin).
Offers: keep existing routes, remove actor ids from routes/bodies: `GET /api/offers/{id}`, `GET /api/offers/land/{landId}`, `GET /api/offers/my`, `GET /api/offers/pending-admin-approval` (Admin), `POST /api/offers`, `POST /api/offers/{id}/counter-offer|accept|reject|withdraw|admin-approve|admin-reject`, `GET /api/offers/{id}/negotiation-history`. **Remove** `POST /api/offers/{id}/close` (internal only).
Contracts (restored from HEAD): `GET /api/contracts`, `GET /api/contracts/{id}`, `POST /api/contracts` (Admin), `POST /{id}/sign`, `POST /{id}/review` (Admin), `POST /{id}/cancellation/request`, `POST /{id}/cancellation/respond`, `POST /{id}/terminate` (Admin), `POST /{id}/force-terminate` (SuperAdmin), `GET /{id}/reviews`, `GET /{id}/approvals`; `PUT /api/project-approvals/{id}` (Admin). No generic PUT/DELETE on contracts.
Reservations: `GET /api/grid-capacity-reservations` (Admin), `POST /api/grid-capacity-reservations/{id}/release` (Admin).

## 9. Jordan regulatory context (research notes, 2026-10-03)

Sources were law-firm articles, trade press and one government service page; they are **not** the legal texts. Verify against Law 12/2024, Bylaw 58/2024 and the Department of Lands and Survey (DLS) before presenting anything as legal fact.

- Project approvals involve several bodies: generation licence (EMRC), grid connection (NEPCO / distribution company), local-authority permits, environmental clearance (Ministry of Environment). The licence requires a company registered in Jordan.
- Since 2024 a joint committee (Ministry of Energy, EMRC, NEPCO and the three distribution companies) issues connection approvals and manages available grid capacity → the platform's reservation is only an indicative soft hold.
- Bylaw 58/2024 lists four connection mechanisms (wheeling, net billing, zero-to-grid, buy-all/sell-all). Our enum `ConnectionMechanism` has BuyAllSellAll, SelfConsumptionNetBilling, ZeroExport, FullNetting. **`FullNetting` does not match "wheeling"** — check the bylaw text; do not rename before that.
- A 2026 draft amendment is still under consultation (not in force).
- Private land owned or leased by the investor has priority in direct proposals; a fee for changing land-use type was reported in 2023 (check current value).
- Shared ownership (mushaa) needs all partners' consent; the Jordan Valley Authority asks for a partner-consent letter certified by the mukhtar.
- Foreign investors: approval rules for owning land differ between sources; leasing is common. Needs a lawyer.
- Solar resource is high everywhere (5–7 kWh/m²/day) → weak discriminator between lands. Below-sea-level areas (Jordan Valley, Dead Sea) are sunny, so `MinElevationM` is left unset in the seed.
- **Not found / needs an engineer or the authority:** slope limit, minimum area, acceptable grid distance, agricultural land-use change procedure, environmental classification thresholds, exact parcel-identification fields.
- Criteria values in the seed are **illustrative placeholders**, not regulation values.

## 10. Conventions

- Controllers are thin: validate DTO → call service → map result. No business logic in controllers.
- Never return entities. Request DTOs carry validation attributes; response DTOs expose names (e.g. status as string) instead of raw ids where useful.
- Pagination: `pageNumber` (default 1), `pageSize` (default 20, max 50) on list endpoints.
- Each step creates exactly one EF migration with a descriptive name; run `dotnet ef migrations has-pending-model-changes` at the end of every step (must report no pending changes).
- Keep existing naming (`UPPER_SNAKE_CASE` columns). Do not refactor unrelated code. Do not add NuGet packages other than those named in a step.
