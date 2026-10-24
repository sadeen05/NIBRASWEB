/**
 * Land-domain DTOs transcribed from the verified backend contract (Phase 5A /
 * Phase 1). Property names match the camelCase JSON exactly, including the
 * intentionally inconsistent `statusName` / `changeByName` casing on the
 * status-history DTO.
 */

/** Backend `LandStatus` enum names. The status query filter is case-sensitive. */
export type LandStatus =
  | 'Draft'
  | 'PendingVerification'
  | 'Verified'
  | 'Rejected'
  | 'UnderContract'
  /** A rejected land that was corrected and resubmitted; a SuperAdmin reviews it. */
  | 'PendingSuperAdminReview';

export const LAND_STATUSES: readonly LandStatus[] = [
  'Draft',
  'PendingVerification',
  'Verified',
  'Rejected',
  'UnderContract',
  'PendingSuperAdminReview',
];

/** `GET /api/Lands/{id}` and the `items` of `GET /api/Lands`. */
export interface LandDto {
  id: number;
  registeredAt: string | null;
  landlordId: number;
  /** Assigned by an admin technical assessment; `null` until assessed. */
  regionId: number | null;
  regionName: string | null;
  /** Assigned by an admin technical assessment; `null` until assessed. */
  gridId: number | null;
  /** Governorate chosen by the landlord; `null` only for pre-governorate records. */
  governorateId: number | null;
  governorateName: string | null;
  landNumber: string;
  village: string | null;
  basin: string | null;
  parcelNumber: string | null;
  description: string | null;
  areaDonum: number;
  /** `null` means "not yet assessed"; the UI must never render it as zero. */
  slopePercentage: number | null;
  /** `null` means "not yet assessed". */
  distanceToGridKm: number | null;
  /** `null` means "not yet assessed". */
  solarIrradiance: number | null;
  /** `null` means "not yet assessed". */
  elevationM: number | null;
  /** One of {@link LandStatus}; kept as a string so an unknown value still renders. */
  status: string;
  dataVerifiedByAdmin: boolean;
  verifiedAgainstCriterionId: number | null;
  /**
   * `true` while an open change request hides this approved land from investors.
   * The status stays `Verified` during the review.
   */
  isHiddenFromInvestors: boolean;
}

/** `GET /api/Lands` envelope. */
export interface LandListResponse {
  items: LandDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/**
 * Body of `POST /api/Lands` and `PUT /api/Lands/{id}`. Region, grid and the
 * technical measurements are admin-only and are never sent by a landlord.
 */
export interface LandWriteRequest {
  /** Required on create; omitted on edit so legacy lands need no governorate. */
  governorateId?: number;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  description?: string | null;
}

export type CreateLandRequest = LandWriteRequest;
export type UpdateLandRequest = LandWriteRequest;

/**
 * Body of `PUT /api/Lands/{id}/assessment` (SuperAdmin only — land data may not
 * be changed by a regular Admin). Every field is optional: an omitted value
 * keeps its stored value, and Phase 1 has no way to clear a stored value.
 */
export interface AssessLandRequest {
  regionId?: number | null;
  gridId?: number | null;
  slopePercentage?: number | null;
  distanceToGridKm?: number | null;
  solarIrradiance?: number | null;
  elevationM?: number | null;
  note?: string | null;
}

/** `GET /api/Lands/{id}/assessments` item (append-only snapshot). */
export interface LandAssessmentDto {
  id: number;
  landId: number;
  assessedById: number;
  assessedByName: string;
  criterionId: number | null;
  areaDonumSnapshot: number | null;
  criteriaSnapshot: CriterionValuesDto | null;
  assessedAt: string;
  note: string | null;
  regionId: number | null;
  gridId: number | null;
  slopePercentage: number | null;
  distanceToGridKm: number | null;
  solarIrradiance: number | null;
  elevationM: number | null;
}

/**
 * A land document. Real uploads expose the display metadata and `hasFile`; `filePath` is the
 * private storage key and is never shown as a filesystem path. Legacy metadata rows have
 * `hasFile: false`.
 */
export interface LandDocumentDto {
  id: number;
  landId: number;
  documentTypeId: number;
  documentTypeName: string;
  /** Legacy metadata-only reference; null for a real upload (use `hasFile` and download). */
  filePath: string | null;
  version: number;
  status: string;
  uploadedAt: string | null;
  reviewedById: number | null;
  reviewedAt: string | null;
  reviewNote: string | null;
  originalFileName: string | null;
  contentType: string | null;
  fileSizeBytes: number | null;
  sha256: string | null;
  hasFile: boolean;
}

/**
 * The real upload is multipart form data:
 *   documentTypeId: number
 *   file: File (PDF, JPEG or PNG, up to the server limit)
 */
export interface UploadLandDocumentInput {
  documentTypeId: number;
  file: File;
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
  /** `null` when the measurement has not been recorded. */
  actual: number | null;
  /** `NotEnforced` | `NotAssessed` | `Passed` | `Failed`. */
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
  changedById: number;
  reason: string | null;
  changedAt: string;
}

/** `GET /api/criteria/current`. */
export type CriterionValuesDto = Pick<LandCriterionDto, 'id' | 'minAreaDonum' | 'maxSlopePct' | 'maxGridDistanceKm' | 'minSolarIrradiance' | 'minElevationM'>;

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

/** `GET /api/Governorates` item. */
export interface GovernorateDto {
  id: number;
  /** Stable ISO 3166-2:JO suffix, e.g. "AM" for Amman. */
  code: string;
  nameEn: string;
  nameAr: string;
}

/** `GET /api/document-types` item. */
export interface DocumentTypeDto {
  id: number;
  name: string;
  isRequiredForVerification: boolean;
}

/**
 * The landlord-owned fields of a land, as carried by a change request. Region, grid
 * and the technical measurements are never part of a change request.
 */
export interface LandFieldsSnapshot {
  governorateId: number | null;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  description: string | null;
}

/** A supporting document reference on a change request (metadata only). */
export interface ChangeRequestDocument {
  documentTypeId: number;
  filePath: string;
}

/** Body of `POST /api/land-change-requests` (owner-only). */
export interface CreateLandChangeRequestRequest {
  landId: number;
  reason: string;
  governorateId?: number | null;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  description?: string | null;
  supportingDocuments: ChangeRequestDocument[];
}

/** Body of `POST /api/land-change-requests/{id}/decision` (SuperAdmin only). */
export interface ReviewLandChangeRequestRequest {
  approved: boolean;
  decisionReason: string;
}

/** One change request, including its immutable old/new snapshots and the decision. */
export interface LandChangeRequestDto {
  id: number;
  landId: number;
  landNumber: string;
  status: string;
  reason: string;
  requestedAt: string;
  requestedById: number;
  requestedByName: string;
  /**
   * The immutable stored snapshots. `null` means the stored JSON cannot be read: the server
   * reports it as unavailable and never substitutes an empty or repaired snapshot, and the
   * request itself can still be rejected.
   */
  originalFields: LandFieldsSnapshot | null;
  proposedFields: LandFieldsSnapshot | null;
  /**
   * The stored supporting-document references. `null` means the stored JSON cannot be read;
   * an empty list means the request stored no references at all.
   */
  supportingDocuments: ChangeRequestDocument[] | null;
  reviewedById: number | null;
  reviewedByName: string | null;
  reviewedAt: string | null;
  decisionReason: string | null;
}
