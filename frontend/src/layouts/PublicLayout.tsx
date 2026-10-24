import { Suspense } from 'react';
import { Outlet } from 'react-router-dom';

import { RouteFallback } from '@/components/RouteFallback';

/**
 * Layout for unauthenticated surfaces (login, error pages). Provides the NIBRAS
 * background treatment and the shared language switch.
 */
export function PublicLayout() {
  return (
    <div className="public-root">
      <Suspense fallback={<RouteFallback />}>
        <Outlet />
      </Suspense>
    </div>
  );
}
