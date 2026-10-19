/**
 * Reads the single-use token out of the URL fragment and drops the fragment from
 * the address bar in the same step.
 *
 * The verification links carry the token in the fragment (`#token=...`), never in
 * a query string: a fragment is not transmitted to the server, so the token cannot
 * appear in an access log, a proxy log or a referrer. `replaceState` replaces the
 * current history entry, so the token is neither left in the address bar nor
 * restorable with the back button.
 *
 * A token in the query string is deliberately ignored - the backend never produces
 * one, and accepting it would teach the wrong contract.
 */
export function takeTokenFromFragment(): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  const { hash } = window.location;
  if (!hash) {
    return null;
  }

  // Remove the fragment first: the token must not stay in the address bar. The
  // router keeps its own bookkeeping (`usr`, `key`, `idx`) in the history state,
  // so that state is preserved - wiping it would break the index the router uses
  // for the back button.
  window.history.replaceState(
    window.history.state,
    '',
    window.location.pathname + window.location.search,
  );

  const fragment = hash.startsWith('#') ? hash.slice(1) : hash;
  const token = new URLSearchParams(fragment).get('token')?.trim();
  return token ? token : null;
}
