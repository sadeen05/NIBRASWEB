/**
 * Land-domain DTOs transcribed from the verified backend contract (Phase 5A).
 * Property names match the camelCase JSON exactly, including the intentionally
 * inconsistent `statusName` / `changeByName` casing on the status-history DTO.
 */

/** Backend `LandStatus` enum names. The status query filter is case-sensitive. */
export type LandStatus =
  | 'Draft'
  | 'PendingVerification'
  | 'Verified'
  | 'Rejected'
  | 'UnderContract';

export const LAND_STATUSES: readonly LandStatus[] = [
  'Draft',
  'PendingVerification',
  'Verified',
  'Rejected',
  'UnderContract',
];

/** Backend `LandDocument.Status` values (`DocumentStatuses`). */
export type DocumentStatus = 'Pending' | 'Approved' | 'Rejected';

/** `GET /api/Lands/{id}` and the `items` of `GET /api/Lands`. */
export interface LandDto {
  id: number;
  landlordId: number;
  regionId: number;
  regionName: string;
  gridId: number;
  landNumber: string;
  village: string | null;
  basin: string | null;
  parcelNumber: string | null;
  areaDonum: number;
  slopePercentage: number;
  distanceToGridKm: number;
  solarIrradiance: number;
  elevationM: number;
  /** One of {@link LandStatus}; kept as a string so an unknown value still renders. */
  status: string;
  dataVerifiedByAdmin: boolean;
  verifiedAgainstCriterionId: number | null;
}

/** `GET /api/Lands` envelope. */
export interface LandListResponse {
  items: LandDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Body of `POST /api/Lands` and `PUT /api/Lands/{id}` (identical shape). */
export interface LandWriteRequest {
  regionId: number;
  gridId: number;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  slopePercentage: number;
  distanceToGridKm: number;
  solarIrradiance: number;
  elevationM: number;
}

export type CreateLandRequest = LandWriteRequest;
export type UpdateLandRequest = LandWriteRequest;

/** A land document. `filePath` is a stored path/reference, not a file payload. */
export interface LandDocumentDto {
  id: number;
  landId: number;
  documentTypeId: number;
  documentTypeName: string;
  filePath: string;
  version: number;
  status: string;
  uploadedAt: string | null;
  reviewedById: number | null;
  reviewedAt: string | null;
  reviewNote: string | null;
}

/** Body of `POST /api/Lands/{id}/documents` (metadata only). */
export interface UploadLandDocumentRequest {
  documentTypeId: number;
  filePath: string;
}

/** Body of `PUT /api/Lands/{id}/documents/{documentId}/review` (admin only). */
export interface ReviewLandDocumentRequest {
  approved: boolean;
  note?: string | null;
}

/** One eligibility criterion outcome. */
export interface LandCriterionResult {
  criterion: string;
  required: number | null;
  actual: number;
  /** `Passed` | `Failed` | `NotEnforced`. */
  result: string;
}

/** `GET /api/Lands/{id}/eligibility`. */
export interface LandEligibilityResponse {
  landId: number;
  criterionId: number | null;
  results: LandCriterionResult[];
}

/** `GET /api/Lands/{id}/history` item (note the exact JSON casing). */
export interface LandStatusHistoryDto {
  id: number;
  landId: number;
  statusName: string;
  changeByName: string;
  reason: string | null;
  changedAt: string;
}

/** `GET /api/criteria/current`. */
export interface LandCriterionDto {
  id: number;
  minAreaDonum: number | null;
  maxSlopePct: number | null;
  maxGridDistanceKm: number | null;
  minSolarIrradiance: number | null;
  minElevationM: number | null;
  updatedById: number;
  updatedByName: string;
  updatedAt: string | null;
  reason: string;
}

/** `GET /api/Regions` item. */
export interface RegionDto {
  id: number;
  nameAr: string;
  nameEn: string;
  peakSunHoursPerDay: number;
  wheelingFeePerKwh: number;
  lossPercentage: number;
}

/** `GET /api/Grids` item. */
export interface GridDto {
  id: number;
  regionId: number;
  name: string;
  capacityMw: number;
  status: string | null;
}

/** `GET /api/document-types` item. */
export interface DocumentTypeDto {
  id: number;
  name: string;
  isRequiredForVerification: boolean;
}
