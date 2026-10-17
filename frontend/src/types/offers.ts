/**
 * Offer DTOs, transcribed from the verified backend contract (Phase 5C).
 *
 * IMPORTANT: `OfferDto.status` and `connectionMechanism` are serialized as
 * **integers** (the backend has no string-enum converter), unlike
 * `ContractDto.status` which is a string. `OfferNegotiationHistoryDto.actionType`
 * is an integer as well.
 */

/** `OfferStatus` enum values. */
export const OFFER_STATUS = {
  PendingLandlordResponse: 1,
  PendingInvestorResponse: 2,
  PendingAdminApproval: 3,
  Accepted: 4,
  RejectedByLandlord: 5,
  RejectedByInvestor: 6,
  RejectedByAdmin: 7,
  WithdrawnByInvestor: 8,
  Closed: 9,
} as const;

export type OfferStatus = number;

/** Numeric status -> translation key suffix. */
export const OFFER_STATUS_NAMES: Record<number, string> = {
  1: 'PendingLandlordResponse',
  2: 'PendingInvestorResponse',
  3: 'PendingAdminApproval',
  4: 'Accepted',
  5: 'RejectedByLandlord',
  6: 'RejectedByInvestor',
  7: 'RejectedByAdmin',
  8: 'WithdrawnByInvestor',
  9: 'Closed',
};

/** `ConnectionMechanism` enum values. */
export const CONNECTION_MECHANISMS: readonly { value: number; name: string }[] = [
  { value: 1, name: 'BuyAllSellAll' },
  { value: 2, name: 'SelfConsumptionNetBilling' },
  { value: 3, name: 'ZeroExport' },
  { value: 4, name: 'FullNetting' },
];

export const CONNECTION_MECHANISM_NAMES: Record<number, string> = Object.fromEntries(
  CONNECTION_MECHANISMS.map((entry) => [entry.value, entry.name]),
);

/** `OfferHistoryActionType` enum values -> translation key suffix. */
export const OFFER_ACTION_NAMES: Record<number, string> = {
  0: 'AcceptedPendingAdminReview',
  1: 'RejectedByLandlord',
  2: 'RejectedByInvestor',
  3: 'Withdrawn',
  4: 'ApprovedByAdmin',
  5: 'RejectedByAdmin',
  6: 'AutoClosed',
  7: 'Reopened',
  8: 'ClosedByContract',
  9: 'CompetitorClosed',
  10: 'Created',
};

export interface OfferVersionDto {
  id: number;
  offerId: number;
  versionNumber: number;
  landlordSharePct: number | null;
  durationYears: number | null;
  startDate: string | null;
  installationCost: number | null;
  createdByName: string;
  createdByRole: string;
  solarCellCapacityKw: number | null;
  expectedAnnualRevenue: number | null;
  effectiveCostPerKw: number | null;
  paybackPeriodMonths: number | null;
  message: string | null;
  isCurrent: boolean;
  createdAt: string;
}

export interface OfferDto {
  id: number;
  landId: number;
  investorId: number;
  /** Integer `OfferStatus` (1–9). */
  status: number;
  /** Integer `ConnectionMechanism` (1–4). */
  connectionMechanism: number;
  currentAmount: number;
  lastProposedById: number;
  requiredCapacityMw: number;
  createdAt: string | null;
  updatedAt: string | null;
  relatedToRejectedOfferId: number | null;
  investorName: string | null;
  landLandlordName: string | null;
  latestVersion: OfferVersionDto | null;
}

export interface OfferNegotiationHistoryDto {
  id: number;
  offerId: number;
  /** Integer `OfferHistoryActionType` (0–10). */
  actionType: number;
  actorId: number;
  actorName: string | null;
  actorRole: string | null;
  amount: number | null;
  message: string | null;
  createdAt: string;
}

/** `POST /api/Offers`. */
export interface CreateOfferRequest {
  landId: number;
  requiredCapacityMw: number;
  connectionMechanism: number;
  landlordSharePct: number;
  durationYears?: number | null;
  startDate?: string | null;
  installationCost?: number | null;
  solarCellCapacityKw?: number | null;
  message?: string | null;
}

/** `POST /api/Offers/{id}/counter-offer`. */
export interface CounterOfferRequest {
  newAmount: number;
  message?: string | null;
}

/** `POST /api/Offers/{id}/reject` and `/admin-reject`. */
export interface RejectOfferRequest {
  reason: string;
}
