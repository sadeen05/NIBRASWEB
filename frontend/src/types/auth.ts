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

/**
 * `POST /api/auth/activate-admin` request body. Mirrors the backend
 * `ActivateAdminRequest` record: the single-use invitation token together with
 * the password the new administrator chooses for themselves. The endpoint
 * answers 204 No Content on success.
 */
export interface ActivateAdminRequest {
  token: string;
  password: string;
}

/** Client-side session, derived from {@link AuthResponse}. */
export interface Session {
  token: string;
  expiresAt: string;
  user: AuthUserSummary;
}
