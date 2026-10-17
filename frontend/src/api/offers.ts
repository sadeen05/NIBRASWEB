import { getJson, postJson } from '@/api/client';
import type {
  CounterOfferRequest,
  CreateOfferRequest,
  OfferDto,
  OfferNegotiationHistoryDto,
  RejectOfferRequest,
} from '@/types/offers';

/**
 * Verified Offers endpoints only (Phase 5C). Identity comes from the bearer
 * token; no endpoint accepts an investor id from the client.
 */

/** `GET /api/Offers/my` — the caller's offers (investors) or all offers (admins). */
export function fetchMyOffers(statusFilter?: string): Promise<OfferDto[]> {
  return getJson<OfferDto[]>('/Offers/my', {
    params: statusFilter ? { statusFilter } : undefined,
  });
}

/** `GET /api/Offers/land/{landId}` — land owner or admin only. */
export function fetchOffersForLand(landId: number): Promise<OfferDto[]> {
  return getJson<OfferDto[]>(`/Offers/land/${landId}`);
}

/** `GET /api/Offers/pending-admin-approval` — admin only. */
export function fetchPendingAdminApproval(): Promise<OfferDto[]> {
  return getJson<OfferDto[]>('/Offers/pending-admin-approval');
}

export function fetchOffer(id: number): Promise<OfferDto> {
  return getJson<OfferDto>(`/Offers/${id}`);
}

export function fetchNegotiationHistory(id: number): Promise<OfferNegotiationHistoryDto[]> {
  return getJson<OfferNegotiationHistoryDto[]>(`/Offers/${id}/negotiation-history`);
}

/** `POST /api/Offers` — investors only, land must be Verified. */
export function createOffer(body: CreateOfferRequest): Promise<OfferDto> {
  return postJson<OfferDto>('/Offers', body);
}

export function counterOffer(id: number, body: CounterOfferRequest): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/counter-offer`, body);
}

export function acceptOffer(id: number): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/accept`);
}

export function rejectOffer(id: number, body: RejectOfferRequest): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/reject`, body);
}

export function withdrawOffer(id: number): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/withdraw`);
}

export function adminApproveOffer(id: number): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/admin-approve`);
}

export function adminRejectOffer(id: number, body: RejectOfferRequest): Promise<OfferDto> {
  return postJson<OfferDto>(`/Offers/${id}/admin-reject`, body);
}
