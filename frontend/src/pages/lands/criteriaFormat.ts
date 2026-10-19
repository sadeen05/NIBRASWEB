/**
 * Shared presentation helpers for the land eligibility criteria.
 *
 * The backend keeps evaluating eligibility exactly as before; this module only
 * turns the raw criterion keys it returns ("area", "slope", …) and the raw
 * thresholds of `GET /api/criteria/current` into human, localized copy so a
 * landlord can see what is required and why a save was rejected.
 */

import { formatDecimal } from '@/pages/lands/landFormat';

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/** Criterion keys as returned by `LandService.EvaluateEligibility`. */
export const CRITERION_KEYS = [
  'area',
  'slope',
  'gridDistance',
  'solarIrradiance',
  'elevation',
] as const;

export type CriterionKey = (typeof CRITERION_KEYS)[number];

/**
 * Field labels carry the unit ("Area (donum)", "Slope (%)"), which is what makes
 * a threshold understandable; `lands.criteria.*` holds the unit-less short form.
 */
const CRITERION_FIELD_LABEL_KEYS: Record<string, string> = {
  area: 'lands.form.areaDonum',
  slope: 'lands.form.slopePercentage',
  gridDistance: 'lands.form.distanceToGridKm',
  solarIrradiance: 'lands.form.solarIrradiance',
  elevation: 'lands.form.elevationM',
};

/**
 * `area`, `solarIrradiance` and `elevation` are minimums; `slope` and
 * `gridDistance` are maximums. Mirrors `LandService.EvaluateCriterion`'s
 * `isMinimum` flag — it is a presentation hint only, never a validation rule.
 */
const CRITERION_IS_MINIMUM: Record<string, boolean> = {
  area: true,
  slope: false,
  gridDistance: false,
  solarIrradiance: true,
  elevation: true,
};

/** Short, unit-less criterion name; falls back to the server key verbatim. */
export function criterionName(t: TranslateFn, criterion: string): string {
  const key = `lands.criteria.${criterion}`;
  const translated = t(key);
  return translated === key ? criterion : translated;
}

/** Criterion name including its unit, e.g. "Slope (%)". */
export function criterionLabelWithUnit(t: TranslateFn, criterion: string): string {
  const fieldKey = CRITERION_FIELD_LABEL_KEYS[criterion];
  if (fieldKey) {
    const translated = t(fieldKey);
    if (translated !== fieldKey) {
      return translated;
    }
  }
  return criterionName(t, criterion);
}

/** `true` when the criterion is a minimum ("at least"), `false` for a maximum. */
export function criterionIsMinimum(criterion: string): boolean {
  return CRITERION_IS_MINIMUM[criterion] ?? true;
}

/**
 * Localized threshold of one criterion including its direction, using the number
 * the server reports: "at most 8", "at least 20", or "not currently enforced" when
 * the criterion has no threshold configured. A null threshold is never rendered as
 * a zero.
 */
export function criterionThresholdText(
  t: TranslateFn,
  locale: string,
  criterion: string,
  required: number | null,
): string {
  if (required === null || !Number.isFinite(required)) {
    return t('lands.criteriaCard.notEnforced');
  }
  const value = formatDecimal(required, locale);
  return criterionIsMinimum(criterion)
    ? t('lands.criteriaCard.atLeast', { value })
    : t('lands.criteriaCard.atMost', { value });
}

/** Localized direction wording: "minimum required" / "maximum allowed". */
export function criterionDirectionText(t: TranslateFn, criterion: string): string {
  return criterionIsMinimum(criterion)
    ? t('lands.criteria.minimum')
    : t('lands.criteria.maximum');
}

/**
 * One criterion with the requirement that applies. With a threshold available it
 * carries the number ("Slope (%) — at most 8"); without it, only the direction
 * ("Slope (%) — maximum allowed"), because the backend's rejection message names
 * the failing criteria but not their limits.
 */
export function criterionRequirementText(t: TranslateFn, criterion: string): string {
  return `${criterionLabelWithUnit(t, criterion)} — ${criterionDirectionText(t, criterion)}`;
}

/** Prefix of `LandService.EnsureEligible`'s dynamic message (see `parseEligibilityFailure`). */
export const ELIGIBILITY_PREFIX = 'Land does not meet eligibility criteria:';

/**
 * Extracts the criterion keys from the backend rejection message
 * "Land does not meet eligibility criteria: area, slope." Returns `null` when
 * the message is not an eligibility failure.
 */
export function parseEligibilityFailure(message: string): string[] | null {
  if (!message.startsWith(ELIGIBILITY_PREFIX)) {
    return null;
  }
  const keys = message
    .slice(ELIGIBILITY_PREFIX.length)
    .replace(/\.\s*$/, '')
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
  return keys.length > 0 ? keys : null;
}

/**
 * Localized, actionable replacement for the raw eligibility message, e.g.
 * "The land does not meet the eligibility criteria: Slope (%) — maximum allowed."
 * Returns `null` when the message is not an eligibility failure.
 */
export function describeEligibilityFailure(t: TranslateFn, message: string): string | null {
  const keys = parseEligibilityFailure(message);
  if (!keys) {
    return null;
  }
  return `${t('lands.errors.eligibilityPrefix')}${keys
    .map((key) => criterionRequirementText(t, key))
    .join(', ')}.`;
}

/** Prefix of `LandService.EnsurePinnedCriteriaStillSatisfiedAsync`'s dynamic message. */
export const PINNED_CRITERIA_PREFIX =
  'The land no longer meets the criteria version it was approved against:';

/**
 * Localized, actionable replacement for the pinned-criteria failure, e.g.
 * "The land no longer meets the criteria it was approved against: Slope (%) — maximum allowed."
 * Used when a document is being re-approved on an approved land: the land must still satisfy
 * the criteria version it was verified against before its evidence can become valid again.
 * Returns `null` when the message is not a pinned-criteria failure.
 */
export function describePinnedCriteriaFailure(t: TranslateFn, message: string): string | null {
  if (!message.startsWith(PINNED_CRITERIA_PREFIX)) {
    return null;
  }
  const keys = message
    .slice(PINNED_CRITERIA_PREFIX.length)
    .replace(/\.\s*$/, '')
    .split(',')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
  if (keys.length === 0) {
    return null;
  }
  return `${t('lands.errors.pinnedCriteriaPrefix')}${keys
    .map((key) => criterionRequirementText(t, key))
    .join(', ')}.`;
}

/** The current criterion's thresholds, as returned by `GET /api/criteria/current`. */
export interface LandCriterionThresholds {
  minAreaDonum: number | null;
  maxSlopePct: number | null;
  maxGridDistanceKm: number | null;
  minSolarIrradiance: number | null;
  minElevationM: number | null;
}

/**
 * Best-effort eligibility message for a page that already holds the current
 * criterion: the failing measurements are named *and* given their actual limit
 * ("Slope (%) — at most 8"). Falls back to the direction-only wording when the
 * thresholds are unavailable, so a save failure is always explained.
 */
export function describeEligibilityFailureWithThresholds(
  t: TranslateFn,
  locale: string,
  message: string,
  criterion: LandCriterionThresholds | null,
): string | null {
  const keys = parseEligibilityFailure(message);
  if (!keys) {
    return null;
  }
  if (!criterion) {
    return describeEligibilityFailure(t, message);
  }

  const requirements = new Map(
    criterionRequirementRows(t, locale, criterion).map((row) => [row.key as string, row]),
  );
  const parts = keys.map((key) => {
    const row = requirements.get(key);
    return row
      ? `${criterionLabelWithUnit(t, key)} — ${row.requirement}`
      : criterionRequirementText(t, key);
  });
  return `${t('lands.errors.eligibilityPrefix')}${parts.join('. ')}.`;
}

/** A single row of the "current criteria" summary shown next to the land form. */
export interface CriterionRequirementRow {
  key: CriterionKey;
  label: string;
  /** Localized "at least 20" / "at most 8", or the not-enforced wording. */
  requirement: string;
  enforced: boolean;
}

/**
 * Thresholds of the current criterion, in the order the backend evaluates them.
 * A `null` threshold means the criterion is not enforced (`NotEnforced`), which
 * is reported explicitly instead of being rendered as a zero.
 */
export function criterionRequirementRows(
  t: TranslateFn,
  locale: string,
  criterion: LandCriterionThresholds | null,
): CriterionRequirementRow[] {
  const thresholds: Record<CriterionKey, number | null> = {
    area: criterion?.minAreaDonum ?? null,
    slope: criterion?.maxSlopePct ?? null,
    gridDistance: criterion?.maxGridDistanceKm ?? null,
    solarIrradiance: criterion?.minSolarIrradiance ?? null,
    elevation: criterion?.minElevationM ?? null,
  };

  return CRITERION_KEYS.map((key) => {
    const threshold = thresholds[key];
    const enforced = typeof threshold === 'number' && Number.isFinite(threshold);
    const value = enforced ? formatDecimal(threshold, locale) : '';
    const requirement = !enforced
      ? t('lands.criteriaCard.notEnforced')
      : criterionIsMinimum(key)
        ? t('lands.criteriaCard.atLeast', { value })
        : t('lands.criteriaCard.atMost', { value });
    return { key, label: criterionLabelWithUnit(t, key), requirement, enforced };
  });
}
