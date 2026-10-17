/**
 * Contract DTOs, transcribed from the verified backend contract (Phase 5C).
 * `ContractDto.status` is a **string** (`PendingSignatures` / `Active` /
 * `Terminated` / `Rejected`).
 */

export const CONTRACT_STATUSES = [
  'PendingSignatures',
  'Active',
  'Terminated',
  'Rejected',
] as const;

export type ContractStatus = (typeof CONTRACT_STATUSES)[number];

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
  decision: string;
  reason: string | null;
  reviewerName: string;
  createdAt: string | null;
}

export interface ContractListResponse {
  items: ContractDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
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
