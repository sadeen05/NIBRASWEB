import { Button, Tooltip } from 'antd';
import { Languages } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { normalizeLanguage } from '@/i18n';

/** Switches between the two supported languages (English / Arabic). */
export function LanguageToggle() {
  const { i18n, t } = useTranslation();
  const current = normalizeLanguage(i18n.resolvedLanguage ?? i18n.language ?? 'en');
  const next = current === 'ar' ? 'en' : 'ar';
  const label = next === 'ar' ? t('common.switchToArabic') : t('common.switchToEnglish');

  return (
    <Tooltip title={label}>
      <Button
        type="text"
        size="middle"
        icon={<Languages size={18} aria-hidden="true" />}
        onClick={() => void i18n.changeLanguage(next)}
        aria-label={label}
      >
        {next === 'ar' ? t('common.languageArabic') : t('common.languageEnglish')}
      </Button>
    </Tooltip>
  );
}
