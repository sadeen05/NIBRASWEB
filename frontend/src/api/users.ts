import { deleteJson, getJson, postJson } from '@/api/client';
import type {
  CreateUserRequest,
  RoleDto,
  UserApprovalStatusDto,
  UserCreationResponse,
  UserListResponse,
} from '@/types/users';

/**
 * Verified Users / Roles endpoints (Phase 5C). The whole controller is
 * Admin/SuperAdmin only. Role ids are read from `GET /api/Roles`, never assumed.
 */

export interface UserListParams {
  pageNumber: number;
  pageSize: number;
  /** Exact role name (`Landlord` | `Investor` | `Admin` | `SuperAdmin`). */
  role?: string;
  isApproved?: boolean;
}

export function fetchUsers(params: UserListParams): Promise<UserListResponse> {
  return getJson<UserListResponse>('/Users', {
    params: {
      pageNumber: params.pageNumber,
      pageSize: params.pageSize,
      role: params.role,
      isApproved: params.isApproved,
    },
  });
}

/** The eligibility and complete approval history of one account. */
export function fetchUserApproval(id: number): Promise<UserApprovalStatusDto> {
  return getJson<UserApprovalStatusDto>(`/Users/${id}/approval`);
}

/** `POST /api/Users` returns 201 with a `UserDto` body. */
export function createUser(body: CreateUserRequest): Promise<UserCreationResponse> {
  return postJson<UserCreationResponse>('/Users', body);
}

/** `POST /api/Users/{id}/approve` returns 204. */
export function approveUser(id: number): Promise<null> {
  return postJson<null>(`/Users/${id}/approve`);
}

export function rejectUserApproval(id: number, reason: string): Promise<null> {
  return postJson<null>(`/Users/${id}/reject-approval`, { reason });
}

/** `DELETE /api/Users/{id}` returns 204 (soft delete). */
export function deleteUser(id: number): Promise<null> {
  return deleteJson<null>(`/Users/${id}`);
}

export function resendUserMail(id: number, admin: boolean): Promise<{ message: string }> {
  return postJson<{ message: string }>(`/Users/${id}/${admin ? 'admin-invitation' : 'verification'}`);
}

export function fetchRoles(): Promise<RoleDto[]> {
  return getJson<RoleDto[]>('/Roles');
}
