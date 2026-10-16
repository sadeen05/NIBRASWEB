/**
 * Auth/session types shared across the frontend. These mirror the backend
 * `AuthResponse` / `AuthUserSummary` records exactly (camelCase JSON).
 */

/** Role names as returned by the backend (`RoleNames` / seeded ROLES table). */
export type KnownRole = 'Landlord' | 'Investor' | 'Admin' | 'SuperAdmin';

export interface AuthUserSummary {
  id: number;
  fullName: string;
  /** Backend role name; the four known values are listed in {@link KnownRole}. */
  role: string;
  /** Whether an investor account has been approved. Not the same as authentication. */
  isApproved: boolean;
}

/** `POST /api/auth/login` response body. */
export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: AuthUserSummary;
}

/** `POST /api/auth/login` request body. */
export interface LoginRequest {
  email: string;
  password: string;
}

/** Client-side session, derived from {@link AuthResponse}. */
export interface Session {
  token: string;
  expiresAt: string;
  user: AuthUserSummary;
}
