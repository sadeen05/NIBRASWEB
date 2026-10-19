import { getJson, postJson, putJson } from '@/api/client';
import type {
  AssessLandRequest,
  CreateLandChangeRequestRequest,
  CreateLandRequest,
  LandAssessmentDto,
  LandChangeRequestDto,
  LandDocumentDto,
  LandDto,
  LandEligibilityResponse,
  LandListResponse,
  LandStatus,
  LandStatusHistoryDto,
  ReviewLandChangeRequestRequest,
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

/**
 * `POST /api/Lands/{id}/verify` — Admin/SuperAdmin. PendingVerification -> Verified.
 * A land in PendingSuperAdminReview (resubmitted after a rejection) can only be
 * verified by a SuperAdmin.
 */
export function verifyLand(id: number): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/verify`);
}

/**
 * `POST /api/Lands/{id}/reject` — Admin/SuperAdmin. Requires a reason.
 * A land in PendingSuperAdminReview can only be rejected by a SuperAdmin.
 */
export function rejectLand(id: number, reason: string): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Lands/${id}/reject`, { reason });
}

/**
 * `PUT /api/Lands/{id}/assessment` — SuperAdmin only. Records the region, grid
 * and technical measurements (land data). Omitted fields keep their stored value.
 */
export function assessLand(id: number, body: AssessLandRequest): Promise<LandDto> {
  return putJson<LandDto>(`/Lands/${id}/assessment`, body);
}

/** `GET /api/Lands/{id}/assessments` — the land owner or Admin/SuperAdmin. */
export function fetchAssessments(id: number): Promise<LandAssessmentDto[]> {
  return getJson<LandAssessmentDto[]>(`/Lands/${id}/assessments`);
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

/**
 * `POST /api/land-change-requests` — the land owner only. Creates a change request
 * for an approved land; the land is hidden from investors until a SuperAdmin decides.
 */
export function submitChangeRequest(
  body: CreateLandChangeRequestRequest,
): Promise<LandChangeRequestDto> {
  return postJson<LandChangeRequestDto>('/land-change-requests', body);
}

/** `GET /api/land-change-requests?landId=` — the land owner or Admin/SuperAdmin. */
export function fetchChangeRequests(landId: number): Promise<LandChangeRequestDto[]> {
  return getJson<LandChangeRequestDto[]>('/land-change-requests', { params: { landId } });
}

/** `GET /api/land-change-requests/pending` — SuperAdmin only. */
export function fetchPendingChangeRequests(): Promise<LandChangeRequestDto[]> {
  return getJson<LandChangeRequestDto[]>('/land-change-requests/pending');
}

/** `POST /api/land-change-requests/{id}/decision` — SuperAdmin only. */
export function decideChangeRequest(
  id: number,
  body: ReviewLandChangeRequestRequest,
): Promise<LandChangeRequestDto> {
  return postJson<LandChangeRequestDto>(`/land-change-requests/${id}/decision`, body);
}
