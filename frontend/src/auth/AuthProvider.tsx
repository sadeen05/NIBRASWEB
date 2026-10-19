import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { fetchCurrentUser, login as loginRequest } from '@/api/auth';
import { setAccessTokenProvider, setUnauthorizedHandler } from '@/api/client';
import { AuthContext, type AuthContextValue, type AuthStatus } from '@/auth/context';
import {
  clearStoredSession,
  isSessionExpired,
  readStoredSession,
  toSession,
  writeStoredSession,
} from '@/auth/session';
import type { Session } from '@/types/auth';

const MAX_TIMEOUT_MS = 2_147_000_000;

/** Reads a stored session synchronously, discarding it when already expired. */
function readInitialSession(): Session | null {
  const stored = readStoredSession();
  if (!stored || isSessionExpired(stored)) {
    clearStoredSession();
    return null;
  }
  return stored;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [initialSession] = useState(readInitialSession);
  const [session, setSession] = useState<Session | null>(null);
  const [status, setStatus] = useState<AuthStatus>(
    initialSession ? 'loading' : 'anonymous',
  );
  const sessionRef = useRef<Session | null>(initialSession);

  const applySession = useCallback((next: Session | null) => {
    sessionRef.current = next;
    setSession(next);
    setStatus(next ? 'authenticated' : 'anonymous');
    if (next) {
      writeStoredSession(next);
    } else {
      clearStoredSession();
    }
  }, []);

  const logout = useCallback(() => {
    applySession(null);
  }, [applySession]);

  // The API client reads the current token through this provider, avoiding a
  // circular import between the client and the auth layer.
  useEffect(() => {
    setAccessTokenProvider(() => sessionRef.current?.token ?? null);
    return () => setAccessTokenProvider(() => null);
  }, []);

  // A 401 from any request clears the session.
  useEffect(() => {
    setUnauthorizedHandler(logout);
    return () => setUnauthorizedHandler(null);
  }, [logout]);

  // Revalidate a restored session with the backend, which remains the authority.
  useEffect(() => {
    if (!initialSession) {
      return;
    }

    let cancelled = false;

    fetchCurrentUser()
      .then((user) => {
        if (!cancelled) {
          applySession({ token: initialSession.token, expiresAt: initialSession.expiresAt, user });
        }
      })
      .catch(() => {
        if (!cancelled) {
          applySession(null);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [initialSession, applySession]);

  // Proactively sign out when the token expiry passes (via a timer, never
  // synchronously during the effect body).
  useEffect(() => {
    if (!session) {
      return;
    }

    const expiresAtMs = Date.parse(session.expiresAt);
    const remaining = expiresAtMs - Date.now();
    const delay = Number.isFinite(expiresAtMs) ? Math.max(0, Math.min(remaining, MAX_TIMEOUT_MS)) : 0;

    const timer = window.setTimeout(logout, delay);
    return () => window.clearTimeout(timer);
  }, [session, logout]);

  const login = useCallback(
    async (email: string, password: string) => {
      const response = await loginRequest({ email, password });
      const next = toSession(response);
      applySession(next);
      return next.user;
    },
    [applySession],
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      session,
      user: session?.user ?? null,
      login,
      logout,
    }),
    [status, session, login, logout],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
