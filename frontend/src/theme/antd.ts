import { theme, type ThemeConfig } from 'antd';

import { tokens } from '@/theme/tokens';

/**
 * Ant Design dark theme derived from the NIBRAS brand tokens. Direction is
 * applied separately through ConfigProvider so RTL/LTR is handled by antd.
 */
export function buildThemeConfig(isRtl: boolean): ThemeConfig {
  return {
    algorithm: theme.darkAlgorithm,
    token: {
      colorPrimary: tokens.primary,
      colorSuccess: tokens.primary,
      colorInfo: tokens.blue,
      colorWarning: tokens.warning,
      colorError: tokens.danger,
      colorBgBase: tokens.bgPrimary,
      colorBgContainer: tokens.bgSecondary,
      colorBgElevated: tokens.bgSecondary,
      colorTextBase: tokens.textPrimary,
      colorText: tokens.textPrimary,
      colorTextSecondary: tokens.textSecondary,
      colorTextTertiary: tokens.textMuted,
      colorBorder: tokens.border,
      colorBorderSecondary: tokens.border,
      borderRadius: tokens.radiusSm,
      fontFamily: isRtl ? tokens.fontArabic : tokens.fontLatin,
      fontSize: 15,
    },
    components: {
      Button: {
        fontWeight: 600,
      },
      Layout: {
        bodyBg: tokens.bgPrimary,
        headerBg: 'transparent',
      },
    },
  };
}
