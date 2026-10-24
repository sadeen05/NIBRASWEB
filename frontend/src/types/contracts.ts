/**
 * Contract DTOs, transcribed from the verified backend contract (Phase 5C).
 * `ContractDto.status` is a **string** (`PendingSignatures` / `Active` /
 * `Terminated` / `Rejected` / `Cancelled`).
 */

export const CONTRACT_STATUSES = [
  'PendingSignatures',
  'Active',
  'Terminated',
  'Rejected',
  'Cancelled',
] as const;

export interface ContractDto {
  id: number;
  offerId: number;
  landId: number;
  landLabel: string;
  investorId: number;
  investorName: string;
  landlordId: number;
  landlordName: string;
  status: string;
  offerVersionId: number | null;
  criterionId: number | null;
  createdAt: string | null;
  investorSignedAt: string | null;
  landlordSignedAt: string | null;
  adminReviewedAt: string | null;
  adminSignedAt: string | null;
  noticePeriodDays: number;
  landlordSharePct: number | null;
  durationYears: number | null;
  startDate: string | null;
  installationCost: number | null;
  solarCellCapacityKw: number | null;
  expectedAnnualRevenue: number | null;
  effectiveCostPerKw: number | null;
  paybackPeriodMonths: number | null;
}

export interface ContractReviewDto {
  id: number;
  reviewerId: number;
  decision: string;
  reason: string | null;
  reviewerName: string;
  createdAt: string | null;
}

export interface ContractVersionTermsDto {
  offerVersionId: number | null;
  landId: number;
  investorId: number;
  landlordId: number;
  noticePeriodDays: number;
  landlordSharePct: number | null;
  durationYears: number | null;
  startDate: string | null;
  installationCost: number | null;
  solarCellCapacityKw: number | null;
  expectedAnnualRevenue: number | null;
  effectiveCostPerKw: number | null;
  paybackPeriodMonths: number | null;
}

export interface ContractVersionDto {
  id: number;
  contractId: number;
  versionNumber: number;
  versionKind: string;
  eventType: string | null;
  status: string;
  previousVersionId: number | null;
  targetVersionId: number | null;
  offerId: number;
  offerVersionId: number | null;
  terms: ContractVersionTermsDto | null;
  reason: string | null;
  createdById: number | null;
  createdByName: string | null;
  createdByRole: string | null;
  createdAt: string;
}

export interface ContractListResponse {
  items: ContractDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ContractRevisionConsentDto {
  id: number;
  partyUserId: number;
  partyName: string;
  partyRole: string;
  decision: string;
  reason: string | null;
  createdAt: string;
}

export interface ContractRevisionDto {
  id: number;
  contractId: number;
  status: string;
  baseDraftVersionId: number;
  baseDraftVersionNumber: number;
  proposedTerms: ContractVersionTermsDto | null;
  reason: string;
  proposedById: number;
  proposedByName: string;
  proposedByRole: string;
  proposedAt: string;
  adminReviewedById: number | null;
  adminReviewedByName: string | null;
  adminReviewedByRole: string | null;
  adminReviewedAt: string | null;
  adminDecision: string | null;
  escalationReason: string | null;
  superAdminDecidedById: number | null;
  superAdminDecidedByName: string | null;
  superAdminDecidedByRole: string | null;
  superAdminDecidedAt: string | null;
  superAdminDecision: string | null;
  superAdminReason: string | null;
  newOfferVersionId: number | null;
  newOfferVersionNumber: number | null;
  newDraftVersionId: number | null;
  newDraftVersionNumber: number | null;
  closedAt: string | null;
  consents: ContractRevisionConsentDto[];
}

/** `POST /api/Contracts/{id}/revisions` (contract party only). */
export interface CreateContractRevisionRequest {
  expectedDraftVersionId: number;
  reason: string;
  landlordSharePct: number;
  durationYears?: number | null;
  startDate?: string | null;
  installationCost?: number | null;
  solarCellCapacityKw?: number | null;
  expectedAnnualRevenue?: number | null;
  effectiveCostPerKw?: number | null;
  paybackPeriodMonths?: number | null;
  noticePeriodDays?: number | null;
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/consent` (contract party only). */
export interface ContractRevisionConsentRequest {
  expectedDraftVersionId: number;
  approved: boolean;
  reason?: string | null;
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/admin-decision` (Admin/SuperAdmin). */
export interface ContractRevisionAdminDecisionRequest {
  expectedDraftVersionId: number;
  decision: 'Approve' | 'Escalate';
  reason?: string | null;
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/superadmin-decision` (SuperAdmin only). */
export interface ContractRevisionSuperAdminDecisionRequest {
  expectedDraftVersionId: number;
  decision: 'Continue' | 'Cancel';
  reason: string;
}

/** `POST /api/Contracts`. */
export interface CreateContractRequest {
  offerId: number;
}

/** `POST /api/Contracts/{id}/review`. */
export interface ReviewContractRequest {
  approve: boolean;
  reason?: string | null;
}

/** `POST /api/Contracts/{id}/terminate`. */
export interface TerminateContractRequest {
  reason: string;
}
