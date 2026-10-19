import { isApiError } from '@/api/errors';
import {
  describeEligibilityFailure,
  describePinnedCriteriaFailure,
  ELIGIBILITY_PREFIX,
} from '@/pages/lands/criteriaFormat';

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
  'On an approved land only the document types whose latest version is not approved can be corrected.':
    'lands.errors.documentCorrectionTypeOnly',
  'Document type does not exist.': 'lands.errors.documentTypeMissing',
  'You are not the owner of this land.': 'lands.errors.notOwner',
  'Governorate does not exist.': 'lands.errors.governorateMissing',
  // Change requests on an approved land (the landlord-facing panel).
  'A change request can only be made for an approved land.': 'lands.errors.changeRequestNotApproved',
  'This land already has a change request awaiting review.': 'lands.errors.changeRequestPending',
  'A supporting document type does not exist.': 'lands.errors.changeRequestDocumentTypeMissing',
  "You are not allowed to view this land's change requests.": 'lands.errors.changeRequestForbidden',
  'Only a SuperAdmin can review change requests.': 'admin.changeRequestQueueSuperAdminOnly',
  'Only a SuperAdmin can approve or reject a change request.': 'admin.changeRequestDecideSuperAdminOnly',
  'This change request has already been decided.': 'lands.errors.changeRequestDecided',
  'The land is no longer approved; the change cannot be applied.':
    'lands.errors.changeRequestLandNotApproved',
  'The land changed after this change request was submitted, so its stored proposal is stale and cannot be applied.':
    'lands.errors.changeRequestStale',
  'This change request was already decided by another reviewer. Reload it and try again.':
    'lands.errors.changeRequestConcurrent',
};

/** The technical-assessment failure message is dynamic; only its prefix is localized. */
const TECHNICAL_ASSESSMENT_PREFIX = 'Technical assessment is incomplete:';

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
    // Replace the raw server keys ("area, slope") with the localized criterion
    // names plus the direction that applies, so the message is actionable.
    const fallback = `${t('lands.errors.eligibilityPrefix')}${raw
      .slice(ELIGIBILITY_PREFIX.length)
      .trim()}`;
    return describeEligibilityFailure(t, raw) ?? fallback;
  }

  if (raw.startsWith(TECHNICAL_ASSESSMENT_PREFIX)) {
    const rest = raw.slice(TECHNICAL_ASSESSMENT_PREFIX.length).trim();
    return `${t('lands.errors.assessmentIncompletePrefix')}${rest}`;
  }

  // A re-approval on an approved land that would not satisfy the criteria version the land
  // was verified against; the criterion keys are localized like the other eligibility errors.
  const pinned = describePinnedCriteriaFailure(t, raw);
  if (pinned) {
    return pinned;
  }

  if (error.kind === 'forbidden') {
    return t('lands.errors.forbidden');
  }
  if (error.kind === 'notFound') {
    return t('lands.errors.notFound');
  }

  return raw;
}
