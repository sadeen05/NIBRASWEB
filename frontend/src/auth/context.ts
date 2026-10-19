import { createContext } from 'react';

import type { AuthUserSummary, Session } from '@/types/auth';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthContextValue {
  /** `loading` while a stored session is revalidated with the backend. */
  status: AuthStatus;
  session: Session | null;
  user: AuthUserSummary | null;
  /**
   * Authenticates against `POST /api/auth/login`; rejects with an `ApiError`.
   * Resolves with the authenticated user so the caller can pick a landing route
   * that the role is actually allowed to open.
   */
  login: (email: string, password: string) => Promise<AuthUserSummary>;
  /** Client-side sign-out (this backend has no logout endpoint). */
  logout: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
