import { getJson } from '@/api/client';
import type {
  DocumentTypeDto,
  GovernorateDto,
  GridDto,
  LandCriterionDto,
  RegionDto,
} from '@/types/lands';

/**
 * Reference data used by the land form. All endpoints require an authenticated
 * caller (the backend's global fallback policy), and none of them paginate —
 * they return bare arrays / a single object.
 */

export function fetchRegions(): Promise<RegionDto[]> {
  return getJson<RegionDto[]>('/Regions');
}

/** Returns every grid; the assessment form filters by the selected region. */
export function fetchGrids(): Promise<GridDto[]> {
  return getJson<GridDto[]>('/Grids');
}

/** The fixed twelve Jordanian governorates. */
export function fetchGovernorates(): Promise<GovernorateDto[]> {
  return getJson<GovernorateDto[]>('/Governorates');
}

export function fetchDocumentTypes(): Promise<DocumentTypeDto[]> {
  return getJson<DocumentTypeDto[]>('/document-types');
}

export function fetchCurrentCriterion(): Promise<LandCriterionDto> {
  return getJson<LandCriterionDto>('/criteria/current');
}
