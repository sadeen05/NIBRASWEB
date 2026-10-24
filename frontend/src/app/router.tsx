import { createBrowserRouter, Navigate } from 'react-router-dom';

import {
  AccountPage,
  ActivateAdminPage,
  AdminChangeRequestsPage,
  AdminCriteriaPage,
  AdminLandQueuePage,
  AdminLandReviewPage,
  AdminOffersPage,
  AdminUsersPage,
  ConfirmEmailChangePage,
  ContractDetailPage,
  ContractsListPage,
  DashboardPlaceholder,
  DiscoverLandDetailPage,
  DiscoverLandsPage,
  ForbiddenPage,
  LandDetailPage,
  LandFormPage,
  LandListPage,
  LandOffersPage,
  LoginPage,
  MyOffersPage,
  NotFoundPage,
  OfferDetailPage,
  RegisterPage,
  VerifyEmailPage,
} from '@/app/lazyPages';
import { RequireAuth, RequireRole } from '@/auth/guards';
import { AppShell } from '@/layouts/AppShell';
import { PublicLayout } from '@/layouts/PublicLayout';

const ADMIN_ROLES = ['Admin', 'SuperAdmin'] as const;
const PARTY_ROLES = ['Landlord', 'Investor', 'Admin', 'SuperAdmin'] as const;

/**
 * Routes for the Landlord, Investor, Admin/SuperAdmin and Contracts modules.
 * Every guard is UI-only; the backend authorizes each request.
 */
export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { path: '/login', element: <LoginPage /> },
      { path: '/register', element: <RegisterPage /> },
      // The activation link of a new administrator account carries its token in
      // the URL fragment, so this page is public and reads the fragment itself.
      { path: '/activate-admin', element: <ActivateAdminPage /> },
      // The verification link of a new Landlord or Investor account carries its
      // token in the URL fragment as well, so this page is public too.
      { path: '/verify-email', element: <VerifyEmailPage /> },
      // The address-change link also carries its token in the fragment.
      { path: '/confirm-email-change', element: <ConfirmEmailChangePage /> },
      { path: '/403', element: <ForbiddenPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  {
    element: <RequireAuth />,
    children: [
      {
        path: '/app',
        element: <AppShell />,
        children: [
          { index: true, element: <DashboardPlaceholder /> },

          // --- Every authenticated role ---
          { path: 'account', element: <AccountPage /> },

          // --- Landlord ---
          {
            element: <RequireRole roles={['Landlord']} />,
            children: [
              { path: 'lands', element: <LandListPage /> },
              { path: 'lands/new', element: <LandFormPage mode="create" /> },
              { path: 'lands/:id', element: <LandDetailPage /> },
              { path: 'lands/:id/edit', element: <LandFormPage mode="edit" /> },
            ],
          },

          // --- Investor ---
          {
            element: <RequireRole roles={['Investor']} />,
            children: [
              { path: 'discover', element: <DiscoverLandsPage /> },
              { path: 'discover/:id', element: <DiscoverLandDetailPage /> },
              { path: 'offers', element: <MyOffersPage /> },
            ],
          },

          // --- Offers on a land (owner or admin) ---
          {
            element: <RequireRole roles={['Landlord', ...ADMIN_ROLES]} />,
            children: [{ path: 'lands/:id/offers', element: <LandOffersPage /> }],
          },

          // --- Shared offer detail (parties or admin) ---
          {
            element: <RequireRole roles={[...PARTY_ROLES]} />,
            children: [
              { path: 'offers/:id', element: <OfferDetailPage /> },
              { path: 'contracts', element: <ContractsListPage /> },
              { path: 'contracts/:id', element: <ContractDetailPage /> },
            ],
          },

          // --- SuperAdmin ---
          {
            element: <RequireRole roles={['SuperAdmin']} />,
            children: [
              { path: 'admin/change-requests', element: <AdminChangeRequestsPage /> },
            ],
          },

          // --- Admin / SuperAdmin ---
          {
            element: <RequireRole roles={[...ADMIN_ROLES]} />,
            children: [
              { path: 'admin/lands', element: <AdminLandQueuePage /> },
              { path: 'admin/lands/:id', element: <AdminLandReviewPage /> },
              { path: 'admin/offers', element: <AdminOffersPage /> },
              { path: 'admin/users', element: <AdminUsersPage /> },
              { path: 'admin/criteria', element: <AdminCriteriaPage /> },
            ],
          },
        ],
      },
    ],
  },
  { path: '/', element: <Navigate to="/app" replace /> },
]);
