import { Spin } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * Fallback shown while a lazily loaded route chunk is fetched. It is rendered
 * inside the active layout, so the shell or the public background stays visible
 * and the swap does not flash a different page.
 */
export function RouteFallback() {
  const { t } = useTranslation();
  return (
    <div className="state-page" role="status" aria-live="polite">
      <div className="state-card">
        <Spin size="large" />
        <p style={{ color: 'var(--nb-text-secondary)', margin: '16px 0 0' }}>
          {t('common.loading')}
        </p>
      </div>
    </div>
  );
}
