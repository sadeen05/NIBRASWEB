import { Button } from 'antd';
import { Home } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';

import { FullPageState } from '@/components/FullPageState';

/**
 * Shown when a route denies access to a signed-in user. The page is
 * deliberately role-agnostic: it never forwards the user into a role-specific
 * area, because a 403 must not move someone onto another account type's page.
 * Its only action is the shared Overview route (`/app`), which every signed-in
 * role can open. The backend remains the authorization authority; this only
 * provides a safe recovery path.
 */
export function ForbiddenPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <FullPageState
      variant="info"
      code="403"
      title={t('errors.forbiddenTitle')}
      description={t('errors.forbiddenBody')}
      action={
        <Button
          type="primary"
          icon={<Home size={16} aria-hidden="true" />}
          onClick={() => navigate('/app', { replace: true })}
        >
          {t('shell.overview')}
        </Button>
      }
    />
  );
}
