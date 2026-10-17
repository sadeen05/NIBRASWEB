import { useTranslation } from 'react-i18next';
import { Navigate, Outlet, useLocation } from 'react-router-dom';

import { useAuth } from '@/auth/useAuth';
import { FullPageState } from '@/components/FullPageState';
import type { KnownRole } from '@/types/auth';

/**
 * Route guard for authenticated areas. The backend remains the authority; this
 * only controls navigation and avoids flashing protected UI.
 */
export function RequireAuth() {
  const { status } = useAuth();
  const location = useLocation();
  const { t } = useTranslation();

  if (status === 'loading') {
    return (
      <FullPageState
        variant="loading"
        title={t('errors.checkingSessionTitle')}
        description={t('errors.checkingSessionBody')}
      />
    );
  }

  if (status === 'anonymous') {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: `${location.pathname}${location.search}` }}
      />
    );
  }

  return <Outlet />;
}

/**
 * UI-only role gate. The backend remains the security authority: this only
 * prevents a signed-in user from navigating into a feature that would 403.
 */
export function RequireRole({ roles }: { roles: readonly KnownRole[] }) {
  const { status, user } = useAuth();
  const location = useLocation();
  const { t } = useTranslation();

  if (status === 'loading') {
    return (
      <FullPageState
        variant="loading"
        title={t('errors.checkingSessionTitle')}
        description={t('errors.checkingSessionBody')}
      />
    );
  }

  if (status === 'anonymous') {
    return (
      <Navigate
        to="/login"
        replace
        state={{ from: `${location.pathname}${location.search}` }}
      />
    );
  }

  const role = user?.role as KnownRole | undefined;
  if (!role || !roles.includes(role)) {
    return <Navigate to="/403" replace />;
  }

  return <Outlet />;
}
