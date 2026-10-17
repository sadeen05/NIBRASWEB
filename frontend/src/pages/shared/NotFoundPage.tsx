import { Button } from 'antd';
import { Compass } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';

import { FullPageState } from '@/components/FullPageState';

export function NotFoundPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <FullPageState
      variant="info"
      code="404"
      title={t('errors.notFoundTitle')}
      description={t('errors.notFoundBody')}
      action={
        <Button
          type="primary"
          icon={<Compass size={16} aria-hidden="true" />}
          onClick={() => navigate('/app', { replace: true })}
        >
          {t('shell.overview')}
        </Button>
      }
    />
  );
}
