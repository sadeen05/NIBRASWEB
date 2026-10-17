import { Outlet } from 'react-router-dom';

/**
 * Layout for unauthenticated surfaces (login, error pages). Provides the NIBRAS
 * background treatment and the shared language switch.
 */
export function PublicLayout() {
  return (
    <div className="public-root">
      <Outlet />
    </div>
  );
}
