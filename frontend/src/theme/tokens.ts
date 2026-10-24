/**
 * Centralized NIBRAS design tokens. This is the single source of truth for the
 * brand palette shared between the Ant Design theme and plain CSS
 * (src/styles/global.css mirrors these as CSS custom properties).
 */
export const tokens = {
  bgPrimary: '#070d12',
  bgSecondary: '#0d1821',
  primary: '#22c55e',
  blue: '#3b82f6',
  warning: '#f59e0b',
  danger: '#ef4444',
  textPrimary: '#f0f6ff',
  textSecondary: '#8a9bb5',
  textMuted: '#4a5568',
  border: 'rgba(255,255,255,0.08)',
  borderStrong: 'rgba(255,255,255,0.18)',
  surface: 'rgba(255,255,255,0.04)',
  surfaceHover: 'rgba(255,255,255,0.07)',
  radius: 14,
  radiusSm: 8,
  fontLatin: "'Inter', 'Tajawal', system-ui, -apple-system, 'Segoe UI', sans-serif",
  fontArabic: "'Tajawal', 'Inter', system-ui, sans-serif",
} as const;
