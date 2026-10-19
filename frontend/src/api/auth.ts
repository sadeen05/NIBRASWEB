import { getJson, postJson } from '@/api/client';
import type { AuthResponse, AuthUserSummary, LoginRequest, RegisterRequest } from '@/types/auth';

/**
 * Real backend endpoints only:
 *   POST /api/auth/register (anonymous, rate-limited) -> AuthUserSummary
 *   POST /api/auth/login    (anonymous, rate-limited) -> AuthResponse
 *   GET  /api/auth/me       (authenticated)           -> AuthUserSummary
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
