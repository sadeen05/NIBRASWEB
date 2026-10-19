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

/**
 * Role ids the public registration endpoint accepts. The backend rejects
 * anything else, so Admin (3) / SuperAdmin (4) can never be requested here.
 */
export type RegisterRoleId = 1 | 2;

/**
 * `POST /api/auth/register` request body. Mirrors the backend `RegisterRequest`
 * record; `companyName` / `companyRegistrationNumber` are required by the
 * server when `roleId` is 2 (Investor).
 */
export interface RegisterRequest {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  roleId: RegisterRoleId;
  companyName?: string | null;
  companyRegistrationNumber?: string | null;
}

/** Client-side session, derived from {@link AuthResponse}. */
export interface Session {
  token: string;
  expiresAt: string;
  user: AuthUserSummary;
}
