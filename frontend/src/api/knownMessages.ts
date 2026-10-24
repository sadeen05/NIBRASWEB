import { isApiError, isLocalizedMessageError } from '@/api/errors';
import {
  describeEligibilityFailure,
  describePinnedCriteriaFailure,
  ELIGIBILITY_PREFIX,
} from '@/pages/lands/criteriaFormat';

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/**
 * Exact backend messages that have localized copy. Anything not listed is shown
 * verbatim so no server detail is lost. Strings are the literals thrown by
 * OfferService / ContractService / UserService (Phase 5C).
 */
export const KNOWN_MESSAGES: Record<string, string> = {
  'A rejection reason of at most 500 characters is required.': 'common.reasonRequired',
  'The verification mail could not be delivered. The account stays unverified; the verification can be sent again.': 'mailDelivery.failed',
  'The invitation could not be delivered. The account stays inactive; the invitation can be sent again.': 'mailDelivery.failed',
  'Criteria values and a reason are required.': 'common.reasonRequired',
  'Criteria changed. Refresh the current record before submitting again.': 'criteria.conflict',
  'Only SuperAdmin may change eligibility criteria.': 'apiErrors.forbidden',
  'Only an active SuperAdmin may change eligibility criteria.': 'apiErrors.forbidden',
  // --- Offers ---------------------------------------------------------------
  'An investor cannot make an offer on their own land.': 'offers.errors.ownLand',
  'Only investors can create offers.': 'offers.errors.investorsOnly',
  'Only approved investors can create offers.': 'offers.errors.notApproved',
  'Investor account is not approved.': 'offers.errors.notApproved',
  'ConnectionMechanism is not a defined value.': 'offers.errors.badMechanism',
  'You are not allowed to view this land.': 'discover.notVisibleBody',
  'Only verified lands can receive offers.': 'offers.errors.landNotVerified',
  'Only offers on Verified land can be approved.': 'offers.errors.landNotVerified',
  'You already have an active offer on this land.': 'offers.errors.activeOfferExists',
  'This offer is not in a negotiable state.': 'offers.errors.notNegotiable',
  'Only the landlord can respond at this stage.': 'offers.errors.wrongResponder',
  'Only the investor can respond at this stage.': 'offers.errors.wrongResponder',
  'Only the landlord can accept at this stage.': 'offers.errors.wrongResponder',
  'Only the investor can accept at this stage.': 'offers.errors.wrongResponder',
  'Only the landlord can reject at this stage.': 'offers.errors.wrongResponder',
  'Only the investor can reject at this stage.': 'offers.errors.wrongResponder',
  'Only investors can withdraw offers.': 'offers.errors.investorsOnly',
  'You can only withdraw your own offer.': 'offers.errors.ownOfferOnly',
  'This offer cannot be withdrawn in its current state.': 'offers.errors.notWithdrawable',
  'Only offers pending admin approval can be approved.': 'offers.errors.notPendingApproval',
  'Only offers pending admin approval can be rejected.': 'offers.errors.notPendingApproval',
  'No capacity left in this electrical grid.': 'offers.errors.noCapacity',
  'This offer has already been approved.': 'offers.errors.alreadyApproved',
  'Offer has no versions to approve.': 'offers.errors.noVersions',
  'Only investors or admins can list offers.': 'offers.errors.investorsOnly',
  'Only the land owner or an admin can view offers for this land.':
    'offers.errors.landOffersForbidden',
  'You are not authorized to view this offer.': 'offers.errors.forbidden',
  'You are not authorized to act on this offer.': 'offers.errors.forbidden',
  'You are not authorized to view this negotiation history.': 'offers.errors.forbidden',
  'A concurrent counter-offer was detected; please retry.': 'offers.errors.concurrent',
  'Another offer on this land is under active negotiation; competing offers cannot be changed until that negotiation concludes.':
    'offers.errors.frozen',
  'Counter amount must be greater than zero.': 'offers.errors.counterPositive',
  'Offer not found.': 'offers.errors.notFound',
  'This land is not available for offers while a change request is under review.':
    'offers.errors.landHidden',
  'This offer cannot be approved while the land is hidden for a change-request review.':
    'offers.errors.landHiddenForApproval',
  'This land is not available for offers while its required documents are being corrected.':
    'offers.errors.landEvidenceInvalid',
  "This offer cannot be approved while the land's required documents are being corrected.":
    'offers.errors.landEvidenceInvalidForApproval',

  // --- Contracts ------------------------------------------------------------
  'Only an accepted offer can become a contract.': 'contracts.errors.offerNotAccepted',
  'A contract already exists for this offer.': 'contracts.errors.duplicate',
  'The land is not under contract.': 'contracts.errors.landNotUnderContract',
  'This offer has no active grid reservation.': 'contracts.errors.noReservation',
  'The grid reservation for this offer has expired.': 'contracts.errors.reservationExpired',
  'Only the contract parties can sign.': 'contracts.errors.partiesOnly',
  'Only contracts pending signatures can be signed.': 'contracts.errors.notPendingSignatures',
  'Only contracts pending signatures can be reviewed.': 'contracts.errors.notPendingSignatures',
  'The investor has already signed this contract.': 'contracts.errors.alreadySigned',
  'The landlord has already signed this contract.': 'contracts.errors.alreadySigned',
  'Both parties must sign before the contract can be reviewed.': 'contracts.errors.signaturesRequired',
  'Only active contracts can be terminated.': 'contracts.errors.notActive',
  'Contract not found.': 'contracts.errors.notFound',
  'You are not authorized to view this contract.': 'contracts.errors.forbidden',
  'Only the contract parties can request a revision.': 'contracts.errors.revisionPartiesOnly',
  'Only the contract parties can respond to a revision.': 'contracts.errors.revisionPartiesOnly',
  'This contract already has an open revision.': 'contracts.errors.revisionOpen',
  'This contract changed after you loaded it. Reload the current draft and try again.':
    'contracts.errors.revisionStale',
  'The proposed revision must change at least one contract term.': 'contracts.errors.revisionNoChange',
  'This revision is not awaiting party consent.': 'contracts.errors.revisionNotPendingConsent',
  'This party has already responded to this revision.': 'contracts.errors.revisionAlreadyResponded',
  'This revision is not awaiting admin review.': 'contracts.errors.revisionNotPendingAdmin',
  'This revision is not awaiting a SuperAdmin decision.':
    'contracts.errors.revisionNotPendingSuperAdmin',
  'Both the investor and the landlord must approve the revised terms before admin review.':
    'contracts.errors.revisionConsentsRequired',
  'Fully signed contracts cannot be revised; use the amendment workflow.':
    'contracts.errors.revisionFullySigned',
  'Reviewed or finalised contracts cannot use the draft-revision workflow.':
    'contracts.errors.revisionFinalised',
  'Only a contract pending signatures can be revised.':
    'contracts.errors.notPendingSignatures',
  'This contract has no recorded draft to revise.': 'contracts.errors.revisionNoDraft',
  'A contract revision is in progress; resolve it before signing the contract.':
    'contracts.errors.revisionInProgress',
  'A contract revision is in progress; resolve it before reviewing the contract.':
    'contracts.errors.revisionInProgress',
  'Only a SuperAdmin can decide an escalated contract revision.':
    'contracts.errors.revisionSuperAdminOnly',
  'The admin decision must be Approve or Escalate.': 'contracts.errors.revisionApprovalInvalid',
  'The SuperAdmin decision must be Continue or Cancel.':
    'contracts.errors.revisionSuperAdminInvalid',
  'An escalation reason is required.': 'common.reasonRequired',
  'A SuperAdmin decision reason is required.': 'common.reasonRequired',
  'This contract has no active grid reservation; the revision cannot proceed.':
    'contracts.errors.revisionReservationMissing',
  'The linked grid reservation does not match this contract.':
    'contracts.errors.revisionReservationMissing',
  'The grid reservation for this contract has expired; the revision cannot proceed.':
    'contracts.errors.revisionReservationExpired',
  'The current contract terms are unavailable.': 'contracts.errors.revisionTermsUnavailable',
  'The stored revision terms cannot be read, so the revision cannot be approved.':
    'contracts.errors.revisionTermsUnreadable',
  'The stored revision terms do not match this contract.':
    'contracts.errors.revisionTermsMismatch',
  'The accepted offer version changed after this revision was requested. Reload and try again.':
    'contracts.errors.revisionOfferChanged',
  'The accepted offer version is no longer available.': 'contracts.errors.revisionOfferChanged',
  'The offer is not in the contract-owned state.': 'contracts.errors.revisionOfferState',
  'The offer no longer matches this contract.': 'contracts.errors.revisionOfferMismatch',
  'The land is no longer under contract.': 'contracts.errors.landNotUnderContract',
  'The proposed revision does not change any contract term.': 'contracts.errors.revisionNoChange',
  'Contract revision not found.': 'contracts.errors.revisionNotFound',

  // --- Users ----------------------------------------------------------------
  'Only an Investor with company details can be approved.': 'users.errors.investorNeedsCompany',
  'You cannot manage your own account.': 'users.errors.selfManage',
  'SuperAdmin accounts cannot be managed through the API.': 'users.errors.superAdmin',
  'Admins may manage only Landlord and Investor accounts.': 'users.errors.adminScope',
  'You are not authorized to manage users.': 'users.errors.notAuthorized',
  'You are not authorized to create users.': 'users.errors.notAuthorized',
  'You are not authorized to create this user role.': 'users.errors.roleNotAllowed',
  'Email already in use by another user.': 'users.errors.emailInUse',
  'Email or phone already in use by another user.': 'users.errors.emailOrPhoneInUse',
  'Cannot delete user who owns lands.': 'users.errors.hasLands',
  'Cannot delete user who has contracts.': 'users.errors.hasContracts',
  'Cannot delete user with pending contract cancellations.': 'users.errors.hasCancellations',
  'User role is invalid.': 'users.errors.roleInvalid',
  'Role filter is invalid.': 'users.errors.roleInvalid',
  'Investor company details are required.': 'users.errors.investorCompanyRequired',

  // --- Registration ---------------------------------------------------------
  'Email or phone is already registered': 'register.duplicate',
  'Only Landlord and Investor registration is allowed.': 'register.roleNotAllowed',
  'Password exceeds the supported length.': 'register.passwordTooLong',

  // --- Admin activation -----------------------------------------------------
  // The endpoint answers every failed activation with the same generic message
  // so it does not reveal whether a token ever existed; the copy keeps that
  // generic wording and never guesses at a reason.
  'The activation link is not valid.': 'activateAdmin.invalidLink',
  'The address of the account is already in use.': 'activateAdmin.addressInUse',

  // --- Address confirmation -------------------------------------------------
  // The same rule: every unusable token is answered with the same generic
  // message, so the copy stays generic and never names a reason.
  'The verification link is not valid.': 'verifyEmail.invalidBody',

  // --- Land technical assessment (Phase 1) ----------------------------------
  'Governorate does not exist.': 'lands.errors.governorateMissing',
  'Region and grid must be provided together.': 'admin.assessmentRegionGridPair',
  'Region does not exist.': 'lands.errors.assessmentRegionMissing',
  'A verified land cannot be reassessed.': 'admin.assessmentFrozen',
  'The land has no assigned region and grid; technical assessment is required before verification.':
    'lands.errors.assessmentRequired',
  'The assigned grid does not belong to the assigned region.':
    'lands.errors.assessmentGridRegionMismatch',
  // Raised by the assessment itself (admin-only action): the key is shared with the
  // landlord-facing resolver so the same failure reads the same way everywhere.
  'Grid must exist and belong to the selected region.': 'lands.errors.gridRegion',
  'The land has no assigned electrical grid; technical assessment is required before approval.':
    'lands.errors.gridNotAssigned',

  // --- Document evidence & the verification decision -------------------------
  // These are the guards an admin actually hits while reviewing: each one is a
  // different category (evidence not available / not reviewed / frozen), so each
  // gets its own copy instead of leaking the server's English sentence.
  'Every required document type must have an approved latest version.':
    'admin.documentsNotApproved',
  'No document types are configured as required for verification; verification cannot proceed.':
    'admin.noRequiredDocumentTypes',
  'A note is required when rejecting a document.': 'admin.noteRequired',
  'Documents of a verified land cannot be reviewed again.': 'admin.documentsFrozenVerified',
  'Documents can only be uploaded while the land is Draft or Rejected.':
    'lands.errors.documentsFrozen',
  'On an approved land only the document types whose latest version is not approved can be corrected.':
    'lands.errors.documentCorrectionTypeOnly',
  'Document type does not exist.': 'lands.errors.documentTypeMissing',
  'Document not found.': 'lands.errors.documentNotFound',
  'Land is not pending verification.': 'admin.notPendingVerification',
  'No eligibility criteria configured.': 'lands.errors.noCriteria',

  // --- Landlord change requests on approved lands ---------------------------
  // Raised by the change-request flow (LandService) and surfaced by both the landlord
  // panel and the SuperAdmin queue; the same failure reads the same way everywhere.
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
  'Only a SuperAdmin can re-review the documents of an approved land.':
    'admin.documentReReviewSuperAdminOnly',

  // --- Stored data that cannot be read (L1) and pinned criteria that are unavailable (L2) ---
  // Both are controlled 409s: nothing is substituted for the unreadable stored value, and the
  // referenced criteria version is never replaced by the latest one.
  'The stored before-change snapshot of this request cannot be read, so the change cannot be validated. Rejecting the request is still possible; the stored data is left untouched.':
    'admin.changeRequestSnapshotUnreadable',
  'The stored proposed-change snapshot of this request cannot be read, so the change cannot be applied. Rejecting the request is still possible; the stored data is left untouched.':
    'admin.changeRequestSnapshotUnreadable',
  'The criteria version this land was approved against is unavailable, so the change cannot be validated against it. Rejecting the request is still possible; the stored criteria basis must be restored before a change can be approved.':
    'admin.changeRequestPinnedCriteriaUnavailable',
  'The criteria version this land was approved against is unavailable, so its required documents cannot be approved. The land stays hidden from investors until the stored criteria basis is restored.':
    'admin.pinnedCriteriaUnavailableForReview',

  // --- Shared ---------------------------------------------------------------
  'A rejection reason is required.': 'common.reasonRequired',
  'A termination reason is required.': 'common.reasonRequired',
  'Authentication is required.': 'apiErrors.forbidden',
  'Page number must be at least 1 and page size must be between 1 and 50.': 'common.pageRange',
};

/** The technical-assessment failure message is dynamic; only its prefix is localized. */
const TECHNICAL_ASSESSMENT_PREFIX = 'Technical assessment is incomplete:';

/**
 * Resolves a normalized API error to display text. Known backend messages are
 * translated; unknown server text is shown verbatim.
 */
export function resolveApiMessage(error: unknown, t: TranslateFn): string {
  if (isLocalizedMessageError(error)) {
    return error.message;
  }

  if (!isApiError(error)) {
    return t('errors.genericTitle');
  }

  const raw = error.message;
  const known = KNOWN_MESSAGES[raw];
  if (known) {
    return t(known);
  }

  if (raw.startsWith(ELIGIBILITY_PREFIX)) {
    // Localize the raw criterion keys the server sends ("area, slope") so the
    // message explains which measurements are missing the requirement.
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
    return t('apiErrors.forbidden');
  }
  if (error.kind === 'notFound') {
    return t('apiErrors.notFound');
  }

  return raw;
}
