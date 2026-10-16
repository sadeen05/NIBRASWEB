import { Sun } from 'lucide-react';
import { useTranslation } from 'react-i18next';

/**
 * Typographic NIBRAS wordmark plus a simple Lucide icon. This is a deliberate
 * placeholder until a real brand logo asset is supplied; it is not a fake logo.
 */
export function AppLogo({ compact = false }: { compact?: boolean }) {
  const { t } = useTranslation();

  return (
    <span className="brand-lockup">
      <span className="brand-mark" aria-hidden="true">
        <Sun size={22} strokeWidth={2.2} />
      </span>
      <span className="brand-wordmark">
        <span className="brand-wordmark__name">{t('common.appName')}</span>
        {!compact && <span className="brand-wordmark__tagline">{t('common.tagline')}</span>}
      </span>
    </span>
  );
}
