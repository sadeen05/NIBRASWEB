import { isApiError } from '@/api/errors';

type TranslateFn = (key: string, options?: Record<string, unknown>) => string;

/**
 * Exact backend messages that have localized copy. Anything not listed is shown
 * verbatim so no server detail is lost. Strings are the literals thrown by
 * OfferService / ContractService / UserService (Phase 5C).
 */
export const KNOWN_MESSAGES: Record<string, string> = {
  // --- Offers ---------------------------------------------------------------
  'An investor cannot make an offer on their own land.': 'offers.errors.ownLand',
  'Only investors can create offers.': 'offers.errors.investorsOnly',
  'Only approved investors can create offers.': 'offers.errors.notApproved',
  'Investor account is not approved.': 'offers.errors.notApproved',
  'ConnectionMechanism is not a defined value.': 'offers.errors.badMechanism',
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

  // --- Users ----------------------------------------------------------------
  'Only an Investor with company details can be approved.': 'users.errors.investorNeedsCompany',
  'You cannot manage your own account.': 'users.errors.selfManage',
  'SuperAdmin accounts cannot be managed through the API.': 'users.errors.superAdmin',
  'Admins may manage only Landlord and Investor accounts.': 'users.errors.adminScope',
  'You are not authorized to manage users.': 'users.errors.notAuthorized',
  'You are not authorized to create users.': 'users.errors.notAuthorized',
  'You are not authorized to create this user role.': 'users.errors.roleNotAllowed',
  'Email already in use by another user.': 'users.errors.emailInUse',
  'Cannot delete user who owns lands.': 'users.errors.hasLands',
  'Cannot delete user who has contracts.': 'users.errors.hasContracts',
  'Cannot delete user with pending contract cancellations.': 'users.errors.hasCancellations',
  'User role is invalid.': 'users.errors.roleInvalid',
  'Role filter is invalid.': 'users.errors.roleInvalid',
  'Investor company details are required.': 'users.errors.investorCompanyRequired',

  // --- Shared ---------------------------------------------------------------
  'A rejection reason is required.': 'common.reasonRequired',
  'A termination reason is required.': 'common.reasonRequired',
  'Authentication is required.': 'apiErrors.forbidden',
  'Page number must be at least 1 and page size must be between 1 and 50.': 'common.pageRange',
};

const ELIGIBILITY_PREFIX = 'Land does not meet eligibility criteria:';

/**
 * Resolves a normalized API error to display text. Known backend messages are
 * translated; unknown server text is shown verbatim.
 */
export function resolveApiMessage(error: unknown, t: TranslateFn): string {
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
    return t('apiErrors.forbidden');
  }
  if (error.kind === 'notFound') {
    return t('apiErrors.notFound');
  }

  return raw;
}
