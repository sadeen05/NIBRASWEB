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
  /**
   * The verification part of a registration answer: `handed_over` when a verification mail
   * was handed to the mailer, `failed` when it could not be handed over, and
   * `not_applicable` when the registration does not require a confirmation.
   *
   * The backend does not return the field yet - it arrives with the increment that creates
   * new accounts unverified. The field is therefore optional, and the client keeps working
   * with both shapes: while it is absent, a registration does not require a confirmation.
   */
  verification?: 'not_applicable' | 'handed_over' | 'failed';
  /** A short explanation that belongs to the verification status. Never a credential. */
  verificationMessage?: string;
  /** The active address of the account. Present on login and `/auth/me`. */
  email?: string;
  /** `true` when the active address is proven; `false` while it is not; absent on older shapes. */
  emailVerified?: boolean | null;
  /** An address that is waiting for confirmation; the active address is unchanged. */
  pendingEmail?: string | null;
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

/**
 * `POST /api/auth/verify-email` request body. Mirrors the backend
 * `VerifyEmailRequest` record: the single-use registration token from the
 * verification mail. Only a registration token is accepted; the endpoint
 * answers 204 No Content on success.
 */
export interface VerifyEmailRequest {
  token: string;
}

/**
 * `POST /api/auth/change-email` request body. Mirrors the backend
 * `ChangeEmailRequest`: the current password is required, and the active
 * address is only replaced after the new address has been confirmed.
 */
export interface ChangeEmailRequest {
  newEmail: string;
  currentPassword: string;
}

/**
 * `POST /api/auth/resend-verification` request body. Mirrors the backend
 * `ResendVerificationRequest` record. The endpoint answers with the same
 * generic 202 whether the address belongs to an account or not, so the answer
 * never reveals that an account exists.
 */
export interface ResendVerificationRequest {
  email: string;
}

/** Client-side session, derived from {@link AuthResponse}. */
export interface Session {
  token: string;
  expiresAt: string;
  user: AuthUserSummary;
}
