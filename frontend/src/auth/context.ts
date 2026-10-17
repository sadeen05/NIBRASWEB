import { createContext } from 'react';

import type { AuthUserSummary, Session } from '@/types/auth';

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous';

export interface AuthContextValue {
  /** `loading` while a stored session is revalidated with the backend. */
  status: AuthStatus;
  session: Session | null;
  user: AuthUserSummary | null;
  /** Authenticates against `POST /api/auth/login`; rejects with an `ApiError`. */
  login: (email: string, password: string) => Promise<void>;
  /** Client-side sign-out (this backend has no logout endpoint). */
  logout: () => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
