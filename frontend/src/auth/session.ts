import type { AuthResponse, AuthUserSummary, Session } from '@/types/auth';

const SESSION_STORAGE_KEY = 'nibras.session';

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function parseUser(value: unknown): AuthUserSummary | null {
  if (!isRecord(value)) {
    return null;
  }
  const { id, fullName, role, isApproved } = value;
  if (
    typeof id !== 'number' ||
    typeof fullName !== 'string' ||
    typeof role !== 'string' ||
    typeof isApproved !== 'boolean'
  ) {
    return null;
  }
  return { id, fullName, role, isApproved };
}

/** Maps a validated login response to the stored session shape. */
export function toSession(response: AuthResponse): Session {
  return {
    token: response.token,
    expiresAt: response.expiresAt,
    user: response.user,
  };
}

/** Reads and structurally validates a stored session; never trusts unknown shapes. */
export function readStoredSession(): Session | null {
  try {
    const raw = window.localStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed: unknown = JSON.parse(raw);
    if (!isRecord(parsed)) {
      return null;
    }
    const token = parsed.token;
    const expiresAt = parsed.expiresAt;
    const user = parseUser(parsed.user);
    if (typeof token !== 'string' || typeof expiresAt !== 'string' || !user) {
      return null;
    }
    return { token, expiresAt, user };
  } catch {
    return null;
  }
}

export function writeStoredSession(session: Session): void {
  try {
    window.localStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
  } catch {
    // Storage may be unavailable; the in-memory session still works for this tab.
  }
}

export function clearStoredSession(): void {
  try {
    window.localStorage.removeItem(SESSION_STORAGE_KEY);
  } catch {
    // Ignore storage failures on clear.
  }
}

/** True when the session has no usable expiry or the expiry is in the past. */
export function isSessionExpired(session: Session, now: number = Date.now()): boolean {
  const expiresAtMs = Date.parse(session.expiresAt);
  return !Number.isFinite(expiresAtMs) || expiresAtMs <= now;
}
