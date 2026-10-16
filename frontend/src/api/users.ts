import { deleteJson, getJson, postJson, putJson } from '@/api/client';
import type {
  CreateUserRequest,
  RoleDto,
  UpdateUserRequest,
  UserDto,
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

export function fetchUser(id: number): Promise<UserDto> {
  return getJson<UserDto>(`/Users/${id}`);
}

/** `POST /api/Users` returns 201 with a `UserDto` body. */
export function createUser(body: CreateUserRequest): Promise<UserDto> {
  return postJson<UserDto>('/Users', body);
}

/** `PUT /api/Users/{id}` returns 204. */
export function updateUser(id: number, body: UpdateUserRequest): Promise<null> {
  return putJson<null>(`/Users/${id}`, body);
}

/** `POST /api/Users/{id}/approve` returns 204. */
export function approveUser(id: number): Promise<null> {
  return postJson<null>(`/Users/${id}/approve`);
}

/** `DELETE /api/Users/{id}` returns 204 (soft delete). */
export function deleteUser(id: number): Promise<null> {
  return deleteJson<null>(`/Users/${id}`);
}

export function fetchRoles(): Promise<RoleDto[]> {
  return getJson<RoleDto[]>('/Roles');
}
