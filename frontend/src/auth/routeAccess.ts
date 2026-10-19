/**
 * Role-aware navigation targets.
 *
 * These helpers only decide *where to send the user after authentication or after
 * a 403*. They mirror the client-side route groups declared in
 * `frontend/src/app/router.tsx` (`RequireRole`) so a user is never bounced into a
 * route their role cannot open, which used to produce a `/403` immediately after
 * signing in.
 *
 * The backend remains the only authorization authority: nothing here grants or
 * checks a permission, it only avoids a pointless navigation.
 */

import type { KnownRole } from '@/types/auth';

const ADMIN_ROLES: readonly KnownRole[] = ['Admin', 'SuperAdmin'];
const PARTY_ROLES: readonly KnownRole[] = ['Landlord', 'Investor', 'Admin', 'SuperAdmin'];

interface RouteRule {
  /** Matched against the path only (query string removed). */
  pattern: RegExp;
  roles: readonly KnownRole[];
}

/**
 * Ordered most specific first, mirroring `router.tsx`:
 * `/app/lands/:id/offers` belongs to the owner/admin group, while `/app/lands`
 * itself is the Landlord group. `/app/offers` (My offers) is Investor-only but
 * `/app/offers/:id` is shared with the other parties.
 */
const ROUTE_RULES: readonly RouteRule[] = [
  { pattern: /^\/app\/admin\/change-requests(?:\/|$)/, roles: ['SuperAdmin'] },
  { pattern: /^\/app\/admin(?:\/|$)/, roles: ADMIN_ROLES },
  { pattern: /^\/app\/lands\/[^/]+\/offers\/?$/, roles: ['Landlord', ...ADMIN_ROLES] },
  { pattern: /^\/app\/discover(?:\/|$)/, roles: ['Investor'] },
  { pattern: /^\/app\/offers\/[^/]+\/?$/, roles: PARTY_ROLES },
  { pattern: /^\/app\/offers\/?$/, roles: ['Investor'] },
  { pattern: /^\/app\/lands(?:\/|$)/, roles: ['Landlord'] },
  { pattern: /^\/app\/contracts(?:\/|$)/, roles: PARTY_ROLES },
  // The Overview index is the shell landing page for every signed-in role.
  { pattern: /^\/app\/?$/, roles: PARTY_ROLES },
];

function pathOf(target: string): string {
  const queryIndex = target.indexOf('?');
  const path = queryIndex === -1 ? target : target.slice(0, queryIndex);
  const hashIndex = path.indexOf('#');
  return hashIndex === -1 ? path : path.slice(0, hashIndex);
}

/** `true` when `pathname` belongs to a route group the role may open. */
export function isRouteAllowedForRole(
  pathname: string,
  role: string | null | undefined,
): boolean {
  if (typeof role !== 'string' || role.length === 0) {
    return false;
  }
  const path = pathOf(pathname);
  const rule = ROUTE_RULES.find((candidate) => candidate.pattern.test(path));
  return rule ? rule.roles.includes(role as KnownRole) : false;
}

/**
 * Resolves the post-login/`403` destination: the originally requested target is
 * preserved only when the signed-in role may open it, otherwise the caller's
 * fallback (the Overview index by default, which every role can open) is used.
 * Only same-origin absolute paths are accepted.
 */
export function safeRedirectTarget(
  from: unknown,
  role: string | null | undefined,
  fallback = '/app',
): string {
  if (typeof from !== 'string' || !from.startsWith('/') || from.startsWith('//')) {
    return fallback;
  }
  return isRouteAllowedForRole(from, role) ? from : fallback;
}
