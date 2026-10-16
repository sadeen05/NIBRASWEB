import { isApiError } from '@/api/errors';

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/**
 * Exact backend business/validation messages that have localized copy. These
 * are the literal strings thrown by `LandService` (Phase 5A); anything not
 * listed here is shown verbatim so no server detail is lost.
 */
const KNOWN_MESSAGES: Record<string, string> = {
  'A land with this land number already exists in the selected region.':
    'lands.errors.landNumberConflict',
  'A land with the same region, village, basin and parcel number already exists.':
    'lands.errors.locationConflict',
  'Grid must exist and belong to the selected region.': 'lands.errors.gridRegion',
  'Only lands in Draft or Rejected status can be edited.': 'lands.errors.notEditable',
  'Cannot edit a land that has an active contract.': 'lands.errors.activeContract',
  'Changing the region requires a grid in the new region.': 'lands.errors.regionNeedsGrid',
  'Only lands in Draft status can be submitted.': 'lands.errors.notSubmittable',
  'Village, basin and parcel number are required before submission.':
    'lands.errors.submitFieldsRequired',
  'A document of a type required for verification must be uploaded before submission.':
    'lands.errors.requiredDocument',
  'Only rejected lands can be revised.': 'lands.errors.notRevisable',
  'Documents can only be uploaded while the land is Draft or Rejected.':
    'lands.errors.documentsFrozen',
  'Document type does not exist.': 'lands.errors.documentTypeMissing',
  'You are not the owner of this land.': 'lands.errors.notOwner',
};

/** The eligibility failure message is dynamic; only its prefix is localized. */
const ELIGIBILITY_PREFIX = 'Land does not meet eligibility criteria:';

/**
 * Resolves a normalized API error to display text. Known backend messages are
 * translated; the eligibility list keeps its server-provided criterion names.
 */
export function resolveLandErrorMessage(error: unknown, t: TranslateFn): string {
  if (!isApiError(error)) {
    return t('errors.genericTitle');
  }

  const raw = error.message;
  const known = KNOWN_MESSAGES[raw];
  if (known) {
    return t(known);
  }

  if (raw.startsWith(ELIGIBILITY_PREFIX)) {
    const rest = raw.slice(ELIGIBILITY_PREFIX.length).trim();
    return `${t('lands.errors.eligibilityPrefix')}${rest}`;
  }

  if (error.kind === 'forbidden') {
    return t('lands.errors.forbidden');
  }
  if (error.kind === 'notFound') {
    return t('lands.errors.notFound');
  }

  return raw;
}
