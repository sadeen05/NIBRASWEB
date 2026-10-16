import { getJson, postJson, putJson } from '@/api/client';
import type {
  CreateLandRequest,
  LandDocumentDto,
  LandDto,
  LandEligibilityResponse,
  LandListResponse,
  LandStatus,
  LandStatusHistoryDto,
  ReviewLandDocumentRequest,
  UpdateLandRequest,
  UploadLandDocumentRequest,
} from '@/types/lands';

/**
 * Verified Lands endpoints only (Phase 5A). There is no `/api/Lands/my` and no
 * landlord-specific route: the landlord's own lands are returned by
 * `GET /api/Lands`, which the backend filters by the authenticated owner.
 */

export interface LandListParams {
  pageNumber: number;
  pageSize: number;
  /** Exact, case-sensitive enum name; omit for "all statuses". */
  status?: LandStatus;
}

export function fetchLands(params: LandListParams): Promise<LandListResponse> {
  return getJson<LandListResponse>('/Lands', {
    params: {
      pageNumber: params.pageNumber,
      pageSize: params.pageSize,
      status: params.status,
    },
  });
}

export function fetchLand(id: number): Promise<LandDto> {
  return getJson<LandDto>(`/Lands/${id}`);
}

/** `POST /api/Lands` returns 201 with a `LandDto` body. */
export function createLand(body: CreateLandRequest): Promise<LandDto> {
  return postJson<LandDto>('/Lands', body);
}

/** `PUT /api/Lands/{id}` returns 204 with no body (normalised to `null`). */
export function updateLand(id: number, body: UpdateLandRequest): Promise<null> {
  return putJson<null>(`/Lands/${id}`, body);
}

/** `POST /api/Lands/{id}/submit` -> `{ message }`. Draft -> PendingVerification. */
export function submitLand(id: number): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/submit`);
}

/** `POST /api/Lands/{id}/revise` -> `{ message }`. Rejected -> Draft. */
export function reviseLand(id: number): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/revise`);
}

/** `POST /api/Lands/{id}/verify` — Admin/SuperAdmin only. PendingVerification -> Verified. */
export function verifyLand(id: number): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/verify`);
}

/** `POST /api/Lands/{id}/reject` — Admin/SuperAdmin only. Requires a reason. */
export function rejectLand(id: number, reason: string): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/reject`, { reason });
}

/** `PUT /api/Lands/{id}/documents/{documentId}/review` — Admin/SuperAdmin only. */
export function reviewDocument(
  landId: number,
  documentId: number,
  body: ReviewLandDocumentRequest,
): Promise<LandDocumentDto> {
  return putJson<LandDocumentDto>(`/Lands/${landId}/documents/${documentId}/review`, body);
}

export function fetchEligibility(id: number): Promise<LandEligibilityResponse> {
  return getJson<LandEligibilityResponse>(`/Lands/${id}/eligibility`);
}

export function fetchHistory(id: number): Promise<LandStatusHistoryDto[]> {
  return getJson<LandStatusHistoryDto[]>(`/Lands/${id}/history`);
}

export function fetchDocuments(id: number): Promise<LandDocumentDto[]> {
  return getJson<LandDocumentDto[]>(`/Lands/${id}/documents`);
}

/**
 * Metadata-only document registration. The backend stores a relative path
 * reference; it has no binary upload, download, or delete endpoint.
 */
export function uploadDocument(
  id: number,
  body: UploadLandDocumentRequest,
): Promise<LandDocumentDto> {
  return postJson<LandDocumentDto>(`/Lands/${id}/documents`, body);
}
