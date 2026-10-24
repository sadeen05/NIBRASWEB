import { getJson, postJson } from '@/api/client';
import type { LandCriterionDto, CriterionValuesDto } from '@/types/lands';

export interface CriterionVersion extends LandCriterionDto {
  previousCriterionId: number | null;
  updatedByRole: string | null;
  actorSnapshotRecorded: boolean;
  previousSnapshot: CriterionValuesDto | null;
  changes: { field: string; oldValue: number | null; newValue: number | null }[];
}
export interface CreateCriteriaRequest {
  expectedCurrentId: number;
  minAreaDonum: number | null;
  maxSlopePct: number | null;
  maxGridDistanceKm: number | null;
  minSolarIrradiance: number | null;
  minElevationM: number | null;
  reason: string;
}
export const fetchCriteriaHistory = () => getJson<CriterionVersion[]>('/criteria/history');
export const createCriteria = (body: CreateCriteriaRequest) => postJson<CriterionVersion>('/criteria', body);
