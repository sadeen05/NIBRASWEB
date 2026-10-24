import { lazy } from 'react';

/**
 * Route-level code splitting. Every page is loaded on demand, so the entry
 * chunk keeps only the shell, the guards and the router. Named exports are
 * mapped to the `default` property the React.lazy contract expects.
 */
export const AdminLandQueuePage = lazy(() =>
  import('@/pages/admin/AdminLandQueuePage').then((m) => ({ default: m.AdminLandQueuePage })),
);
export const AdminChangeRequestsPage = lazy(() =>
  import('@/pages/admin/AdminChangeRequestsPage').then((m) => ({ default: m.AdminChangeRequestsPage })),
);
export const AdminLandReviewPage = lazy(() =>
  import('@/pages/admin/AdminLandReviewPage').then((m) => ({ default: m.AdminLandReviewPage })),
);
export const AdminOffersPage = lazy(() =>
  import('@/pages/admin/AdminOffersPage').then((m) => ({ default: m.AdminOffersPage })),
);
export const AdminCriteriaPage = lazy(() =>
  import('@/pages/admin/AdminCriteriaPage').then((m) => ({ default: m.AdminCriteriaPage })),
);
export const AdminUsersPage = lazy(() =>
  import('@/pages/admin/AdminUsersPage').then((m) => ({ default: m.AdminUsersPage })),
);
export const AccountPage = lazy(() =>
  import('@/pages/app/AccountPage').then((m) => ({ default: m.AccountPage })),
);
export const DashboardPlaceholder = lazy(() =>
  import('@/pages/app/DashboardPlaceholder').then((m) => ({ default: m.DashboardPlaceholder })),
);
export const ActivateAdminPage = lazy(() =>
  import('@/pages/auth/ActivateAdminPage').then((m) => ({ default: m.ActivateAdminPage })),
);
export const ConfirmEmailChangePage = lazy(() =>
  import('@/pages/auth/ConfirmEmailChangePage').then((m) => ({ default: m.ConfirmEmailChangePage })),
);
export const LoginPage = lazy(() =>
  import('@/pages/auth/LoginPage').then((m) => ({ default: m.LoginPage })),
);
export const RegisterPage = lazy(() =>
  import('@/pages/auth/RegisterPage').then((m) => ({ default: m.RegisterPage })),
);
export const VerifyEmailPage = lazy(() =>
  import('@/pages/auth/VerifyEmailPage').then((m) => ({ default: m.VerifyEmailPage })),
);
export const ContractDetailPage = lazy(() =>
  import('@/pages/contracts/ContractDetailPage').then((m) => ({ default: m.ContractDetailPage })),
);
export const ContractsListPage = lazy(() =>
  import('@/pages/contracts/ContractsListPage').then((m) => ({ default: m.ContractsListPage })),
);
export const DiscoverLandDetailPage = lazy(() =>
  import('@/pages/investor/DiscoverLandDetailPage').then((m) => ({ default: m.DiscoverLandDetailPage })),
);
export const DiscoverLandsPage = lazy(() =>
  import('@/pages/investor/DiscoverLandsPage').then((m) => ({ default: m.DiscoverLandsPage })),
);
export const LandDetailPage = lazy(() =>
  import('@/pages/lands/LandDetailPage').then((m) => ({ default: m.LandDetailPage })),
);
export const LandFormPage = lazy(() =>
  import('@/pages/lands/LandFormPage').then((m) => ({ default: m.LandFormPage })),
);
export const LandListPage = lazy(() =>
  import('@/pages/lands/LandListPage').then((m) => ({ default: m.LandListPage })),
);
export const LandOffersPage = lazy(() =>
  import('@/pages/offers/LandOffersPage').then((m) => ({ default: m.LandOffersPage })),
);
export const MyOffersPage = lazy(() =>
  import('@/pages/offers/MyOffersPage').then((m) => ({ default: m.MyOffersPage })),
);
export const OfferDetailPage = lazy(() =>
  import('@/pages/offers/OfferDetailPage').then((m) => ({ default: m.OfferDetailPage })),
);
export const ForbiddenPage = lazy(() =>
  import('@/pages/shared/ForbiddenPage').then((m) => ({ default: m.ForbiddenPage })),
);
export const NotFoundPage = lazy(() =>
  import('@/pages/shared/NotFoundPage').then((m) => ({ default: m.NotFoundPage })),
);
