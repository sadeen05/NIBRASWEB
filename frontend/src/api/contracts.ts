import { getJson, postJson } from '@/api/client';
import type {
  ContractDto,
  ContractListResponse,
  ContractRevisionAdminDecisionRequest,
  ContractRevisionConsentRequest,
  ContractRevisionDto,
  ContractRevisionSuperAdminDecisionRequest,
  ContractReviewDto,
  ContractVersionDto,
  CreateContractRequest,
  CreateContractRevisionRequest,
  ReviewContractRequest,
  TerminateContractRequest,
} from '@/types/contracts';

/**
 * Verified Contracts endpoints only (Phase 5C). There is no cancel / force
 * terminate / PDF / download endpoint, so none is exposed.
 */

export interface ContractListParams {
  pageNumber: number;
  pageSize: number;
  /** Exact `ContractStatus` name (case-insensitive server-side). */
  status?: string;
}

export function fetchContracts(params: ContractListParams): Promise<ContractListResponse> {
  return getJson<ContractListResponse>('/Contracts', {
    params: {
      pageNumber: params.pageNumber,
      pageSize: params.pageSize,
      status: params.status,
    },
  });
}

export function fetchContract(id: number): Promise<ContractDto> {
  return getJson<ContractDto>(`/Contracts/${id}`);
}

export function fetchContractReviews(id: number): Promise<ContractReviewDto[]> {
  return getJson<ContractReviewDto[]>(`/Contracts/${id}/reviews`);
}

export function fetchContractVersions(id: number): Promise<ContractVersionDto[]> {
  return getJson<ContractVersionDto[]>(`/Contracts/${id}/versions`);
}

export function fetchContractRevisions(id: number): Promise<ContractRevisionDto[]> {
  return getJson<ContractRevisionDto[]>(`/Contracts/${id}/revisions`);
}

/** `POST /api/Contracts/{id}/revisions` — contract party only; pre-signing. */
export function requestContractRevision(
  id: number,
  body: CreateContractRevisionRequest,
): Promise<ContractRevisionDto> {
  return postJson<ContractRevisionDto>(`/Contracts/${id}/revisions`, body);
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/consent` — contract party only. */
export function consentToContractRevision(
  id: number,
  revisionId: number,
  body: ContractRevisionConsentRequest,
): Promise<ContractRevisionDto> {
  return postJson<ContractRevisionDto>(`/Contracts/${id}/revisions/${revisionId}/consent`, body);
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/admin-decision` — Admin/SuperAdmin. */
export function decideContractRevisionAsAdmin(
  id: number,
  revisionId: number,
  body: ContractRevisionAdminDecisionRequest,
): Promise<ContractRevisionDto> {
  return postJson<ContractRevisionDto>(
    `/Contracts/${id}/revisions/${revisionId}/admin-decision`,
    body,
  );
}

/** `POST /api/Contracts/{id}/revisions/{revisionId}/superadmin-decision` — SuperAdmin only. */
export function decideContractRevisionAsSuperAdmin(
  id: number,
  revisionId: number,
  body: ContractRevisionSuperAdminDecisionRequest,
): Promise<ContractRevisionDto> {
  return postJson<ContractRevisionDto>(
    `/Contracts/${id}/revisions/${revisionId}/superadmin-decision`,
    body,
  );
}

/** `POST /api/Contracts` — admin only; the offer must be Accepted. */
export function createContract(body: CreateContractRequest): Promise<ContractDto> {
  return postJson<ContractDto>('/Contracts', body);
}

/** `POST /api/Contracts/{id}/sign` — contract parties only. */
export function signContract(id: number): Promise<ContractDto> {
  return postJson<ContractDto>(`/Contracts/${id}/sign`);
}

/** `POST /api/Contracts/{id}/review` — admin only. */
export function reviewContract(id: number, body: ReviewContractRequest): Promise<ContractDto> {
  return postJson<ContractDto>(`/Contracts/${id}/review`, body);
}

/** `POST /api/Contracts/{id}/terminate` — admin only, Active contracts. */
export function terminateContract(id: number, body: TerminateContractRequest): Promise<ContractDto> {
  return postJson<ContractDto>(`/Contracts/${id}/terminate`, body);
}
