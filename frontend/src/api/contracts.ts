import { getJson, postJson } from '@/api/client';
import type {
  ContractDto,
  ContractListResponse,
  ContractReviewDto,
  CreateContractRequest,
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
