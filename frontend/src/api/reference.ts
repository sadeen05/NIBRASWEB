import { getJson } from '@/api/client';
import type { DocumentTypeDto, GridDto, LandCriterionDto, RegionDto } from '@/types/lands';

/**
 * Reference data used by the land form. All four endpoints require an
 * authenticated caller (the backend's global fallback policy), and none of them
 * paginate — they return bare arrays / a single object.
 */

export function fetchRegions(): Promise<RegionDto[]> {
  return getJson<RegionDto[]>('/Regions');
}

/** Returns every grid; the form filters by the selected region client-side. */
export function fetchGrids(): Promise<GridDto[]> {
  return getJson<GridDto[]>('/Grids');
}

export function fetchDocumentTypes(): Promise<DocumentTypeDto[]> {
  return getJson<DocumentTypeDto[]>('/document-types');
}

export function fetchCurrentCriterion(): Promise<LandCriterionDto> {
  return getJson<LandCriterionDto>('/criteria/current');
}
