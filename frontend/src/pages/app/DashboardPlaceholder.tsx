import { Card, Descriptions, Tag, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useAuth } from '@/auth/useAuth';

function formatExpiry(value: string, locale: string): string {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }
  try {
    return parsed.toLocaleString(locale);
  } catch {
    return parsed.toISOString();
  }
}

/**
 * Authenticated overview landing page. It shows who is signed in and points to the
 * workspace the current role can open; it renders no business records of its own.
 */
export function DashboardPlaceholder() {
  const { user, session } = useAuth();
  const { t, i18n } = useTranslation();

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const roleLabel = user ? t(`roles.${user.role}`, { defaultValue: user.role }) : '';

  return (
    <Card title={t('shell.foundationTitle')} styles={{ body: { paddingTop: 12 } }}>
      <Typography.Paragraph style={{ color: 'var(--nb-text-secondary)', maxWidth: 720 }}>
        {t('shell.foundationBody')}
      </Typography.Paragraph>

      {user && (
        <Descriptions
          column={1}
          size="small"
          bordered
          title={t('shell.sessionLabel')}
          items={[
            {
              key: 'name',
              label: t('shell.signedInAs'),
              children: user.fullName,
            },
            {
              key: 'role',
              label: t('shell.roleLabel'),
              children: <Tag color="green">{roleLabel}</Tag>,
            },
            {
              key: 'expires',
              label: t('shell.expiresLabel'),
              children: session ? formatExpiry(session.expiresAt, locale) : '—',
            },
          ]}
        />
      )}
    </Card>
  );
}
