import { getJson, postJson } from '@/api/client';
import type {
  ActivateAdminRequest,
  AuthResponse,
  AuthUserSummary,
  ChangeEmailRequest,
  LoginRequest,
  RegisterRequest,
  ResendVerificationRequest,
  VerifyEmailRequest,
} from '@/types/auth';

/**
 * Real backend endpoints only:
 *   POST /api/auth/register (anonymous, rate-limited) -> AuthUserSummary
 *   POST /api/auth/login    (anonymous, rate-limited) -> AuthResponse
 *   GET  /api/auth/me       (authenticated)           -> AuthUserSummary
 *   POST /api/auth/activate-admin (anonymous, rate-limited) -> 204 No Content
 *   POST /api/auth/verify-email (anonymous, rate-limited) -> 204 No Content
 *   POST /api/auth/resend-verification (anonymous, rate-limited) -> 202
 *   POST /api/auth/change-email (authenticated, rate-limited) -> 202
 *   POST /api/auth/confirm-email-change (anonymous, rate-limited) -> 204 No Content
 *
 * Registration returns the created user's summary, NOT a session: the caller
 * must sign in separately to obtain a JWT.
 */

export function register(request: RegisterRequest): Promise<AuthUserSummary> {
  return postJson<AuthUserSummary>('/auth/register', request);
}

export function login(request: LoginRequest): Promise<AuthResponse> {
  return postJson<AuthResponse>('/auth/login', request);
}

export function fetchCurrentUser(): Promise<AuthUserSummary> {
  return getJson<AuthUserSummary>('/auth/me');
}

/**
 * Activation of a new administrator account:
 *   POST /api/auth/activate-admin (anonymous, rate-limited) -> 204 No Content
 *
 * The single-use invitation token is the only credential, so no session is
 * involved: the endpoint creates no token and the new administrator signs in
 * with the password they just chose. The token travels in the request body
 * only - never in a query string and never in a log.
 */
export function activateAdmin(request: ActivateAdminRequest): Promise<void> {
  return postJson<void>('/auth/activate-admin', request);
}

/**
 * Confirmation of the address of a new Landlord or Investor account:
 *   POST /api/auth/verify-email (anonymous, rate-limited) -> 204 No Content
 *
 * The single-use token is the only credential, so no session is involved: the
 * endpoint creates no token and the account signs in with its own password
 * afterwards. The token travels in the request body only - never in a query
 * string and never in a log.
 */
export function verifyEmail(request: VerifyEmailRequest): Promise<void> {
  return postJson<void>('/auth/verify-email', request);
}

/**
 * A request for a new verification mail:
 *   POST /api/auth/resend-verification (anonymous, rate-limited) -> 202
 *
 * The endpoint answers with the same generic message in every case, so the
 * answer never reveals whether the address belongs to an account or whether it
 * still has to be verified.
 */
export function resendVerification(
  request: ResendVerificationRequest,
): Promise<{ message: string }> {
  return postJson<{ message: string }>('/auth/resend-verification', request);
}

/**
 * Requests an address change for the signed-in account. The current password
 * is required. The active address is unchanged until the confirmation link is
 * redeemed; on success the server sends a message to the new address.
 */
export function changeEmail(
  request: ChangeEmailRequest,
): Promise<{ message: string }> {
  return postJson<{ message: string }>('/auth/change-email', request);
}

/**
 * Confirms a pending address change:
 *   POST /api/auth/confirm-email-change (anonymous, rate-limited) -> 204
 *
 * The token is read from the URL fragment by the confirmation page. The
 * endpoint replaces the active address and invalidates every JWT issued before
 * the change.
 */
export function confirmEmailChange(request: VerifyEmailRequest): Promise<void> {
  return postJson<void>('/auth/confirm-email-change', request);
}
