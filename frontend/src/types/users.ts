/**
 * User / role DTOs, transcribed from the verified backend contract (Phase 5C).
 * The backend returns role names as strings; role ids are only used when
 * creating a user, and are resolved from `GET /api/Roles` rather than hard-coded.
 */

export const ROLE_NAMES = ['Landlord', 'Investor', 'Admin', 'SuperAdmin'] as const;

export type RoleName = (typeof ROLE_NAMES)[number];

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
}

export interface UserListResponse {
  items: UserDto[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
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

/** `PUT /api/Users/{id}`. */
export interface UpdateUserRequest {
  fullName: string;
  email: string;
  phone: string;
}

export interface RoleDto {
  id: number;
  name: string;
}
