import { Alert, Button } from 'antd';
import { LogOut } from 'lucide-react';
import { Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet } from 'react-router-dom';

import { useAuth } from '@/auth/useAuth';
import { AppLogo } from '@/components/AppLogo';
import { LanguageToggle } from '@/components/LanguageToggle';
import { RouteFallback } from '@/components/RouteFallback';

const navLinkClass = ({ isActive }: { isActive: boolean }) =>
  `shell-nav__link${isActive ? ' is-active' : ''}`;

interface NavItem {
  to: string;
  labelKey: string;
  end?: boolean;
}

function navItemsFor(role: string | undefined): NavItem[] {
  const items: NavItem[] = [{ to: '/app', labelKey: 'shell.navOverview', end: true }];

  if (role === 'Landlord') {
    items.push({ to: '/app/lands', labelKey: 'shell.navLands' });
  }
  if (role === 'Investor') {
    items.push({ to: '/app/discover', labelKey: 'shell.navDiscover' });
    items.push({ to: '/app/offers', labelKey: 'shell.navMyOffers' });
  }
  if (role === 'Admin' || role === 'SuperAdmin') {
    items.push({ to: '/app/admin/lands', labelKey: 'shell.navAdminLands' });
    items.push({ to: '/app/admin/offers', labelKey: 'shell.navAdminOffers' });
    items.push({ to: '/app/admin/users', labelKey: 'shell.navAdminUsers' });
    items.push({ to: '/app/admin/criteria', labelKey: 'criteria.title' });
  }
  if (role === 'SuperAdmin') {
    items.push({ to: '/app/admin/change-requests', labelKey: 'shell.navAdminChangeRequests' });
  }
  if (role === 'Landlord' || role === 'Investor' || role === 'Admin' || role === 'SuperAdmin') {
    items.push({ to: '/app/contracts', labelKey: 'shell.navContracts' });
    items.push({ to: '/app/account', labelKey: 'shell.navAccount' });
  }

  return items;
}

/**
 * Authenticated application shell. Navigation is role-aware; the backend
 * remains the authority for every request.
 */
export function AppShell() {
  const { user, logout } = useAuth();
  const { t } = useTranslation();

  const roleLabel = user ? t(`roles.${user.role}`, { defaultValue: user.role }) : '';
  const navItems = navItemsFor(user?.role);

  return (
    <div style={{ minHeight: '100vh', display: 'flex', flexDirection: 'column' }}>
      {/*
        Keyboard-first escape from the persistent header/navigation. It stays hidden
        until it receives focus and is the first tab stop of every authenticated page.
      */}
      <a className="skip-link" href="#main-content">
        {t('shell.skipToContent')}
      </a>
      <header className="shell-header">
        <AppLogo compact />
        <nav className="shell-nav" aria-label={t('shell.navigation')}>
          {navItems.map((item) => (
            <NavLink key={item.to} to={item.to} end={item.end} className={navLinkClass}>
              {t(item.labelKey)}
            </NavLink>
          ))}
        </nav>
        <div className="shell-header__actions">
          {user && (
            <span className="user-chip">
              <span aria-hidden="true">●</span>
              <span>{t('shell.signedInAs')}</span>
              <strong style={{ color: 'var(--nb-text-primary)', fontWeight: 600 }}>
                {user.fullName}
              </strong>
              {roleLabel && <span aria-hidden="true">·</span>}
              {roleLabel && <span>{roleLabel}</span>}
            </span>
          )}
          <LanguageToggle />
          <Button
            icon={<LogOut size={16} aria-hidden="true" />}
            onClick={logout}
            type="text"
          >
            {t('shell.signOut')}
          </Button>
        </div>
      </header>
      <main className="shell-content" id="main-content" tabIndex={-1}>
        {/*
          Persistent, informational only: an unapproved investor can sign in, but
          the backend refuses land browsing and offers until an admin approves the
          account. No permission is granted or implied here.
        */}
        {user && !user.isApproved && (
          <Alert
            type="warning"
            showIcon
            role="status"
            title={t('shell.pendingApprovalTitle')}
            description={t('shell.pendingApprovalBody')}
            style={{ marginBottom: 16 }}
          />
        )}
        <Suspense fallback={<RouteFallback />}>
          <Outlet />
        </Suspense>
      </main>
    </div>
  );
}
