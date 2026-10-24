/**
 * User / role DTOs, transcribed from the verified backend contract (Phase 5C).
 * The backend returns role names as strings; role ids are only used when
 * creating a user, and are resolved from `GET /api/Roles` rather than hard-coded.
 */

export const ROLE_NAMES = ['Landlord', 'Investor', 'Admin', 'SuperAdmin'] as const;

export interface UserDto {
  id: number;
  fullName: string;
  email: string;
  phone: string;
  role: string;
  createdAt: string;
  isApproved: boolean;
  companyName: string | null;
  companyRegistrationNumber: string | null;
  /** `true`/`false`; `null` is a legacy account whose address counts as verified. */
  emailVerified?: boolean | null;
  emailVerifiedAt?: string | null;
  pendingEmail?: string | null;
  approvedById?: number | null;
  approvedByName?: string | null;
  approvedByRole?: string | null;
  approvedAt?: string | null;
  approvedActorSnapshotRecorded?: boolean;
}

/** One durable approval decision from the append-only security event trail. */
export interface UserApprovalHistoryEntryDto {
  eventId: number;
  actorId: number | null;
  actorName: string | null;
  actorRole: string | null;
  approvedAt: string;
  details: string | null;
  eventType: 'AccountApproved' | 'AccountApprovalRejected';
  actorSnapshotRecorded: boolean;
}

/** `GET /api/Users/{id}/approval`. */
export interface UserApprovalStatusDto {
  userId: number;
  role: string;
  isInvestor: boolean;
  emailVerified: boolean | null;
  companyDetailsPresent: boolean;
  isApproved: boolean;
  eligibleForApproval: boolean;
  approvedById: number | null;
  approvedByName: string | null;
  approvedByRole: string | null;
  approvedAt: string | null;
  history: UserApprovalHistoryEntryDto[];
  approvedActorSnapshotRecorded: boolean;
}

export interface UserListResponse {
  items: UserDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface UserCreationResponse extends UserDto {
  invitation: 'not_applicable' | 'handed_over' | 'failed';
  invitationMessage: string | null;
  verification: 'not_applicable' | 'handed_over' | 'failed' | null;
  verificationMessage: string | null;
}

/** `POST /api/Users`. */
export interface CreateUserRequest {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  roleId: number;
  companyName?: string | null;
  companyRegistrationNumber?: string | null;
}

export interface RoleDto {
  id: number;
  name: string;
}
