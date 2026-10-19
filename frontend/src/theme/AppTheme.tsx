import { ConfigProvider } from 'antd';
import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';

import { buildThemeConfig } from '@/theme/antd';
import { isRtlLanguage } from '@/i18n';

export function AppTheme({ children }: { children: ReactNode }) {
  const { i18n } = useTranslation();
  const language = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const isRtl = isRtlLanguage(language);

  const themeConfig = useMemo(() => buildThemeConfig(isRtl), [isRtl]);

  return (
    <ConfigProvider theme={themeConfig} direction={isRtl ? 'rtl' : 'ltr'}>
      {children}
    </ConfigProvider>
  );
}
