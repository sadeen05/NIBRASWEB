import { getJson, postJson } from '@/api/client';
import type { AuthResponse, AuthUserSummary, LoginRequest } from '@/types/auth';

/**
 * Real backend endpoints only:
 *   POST /api/auth/login  (anonymous, rate-limited) -> AuthResponse
 *   GET  /api/auth/me     (authenticated)           -> AuthUserSummary
 */

export function login(request: LoginRequest): Promise<AuthResponse> {
  return postJson<AuthResponse>('/auth/login', request);
}

export function fetchCurrentUser(): Promise<AuthUserSummary> {
  return getJson<AuthUserSummary>('/auth/me');
}
