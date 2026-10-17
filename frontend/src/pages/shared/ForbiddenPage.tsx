import { Button } from 'antd';
import { ArrowLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';

import { FullPageState } from '@/components/FullPageState';

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
          icon={<ArrowLeft size={16} aria-hidden="true" />}
          onClick={() => navigate('/app', { replace: true })}
        >
          {t('shell.overview')}
        </Button>
      }
    />
  );
}
