import { useMutation } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  Space,
  Tag,
  Typography,
} from 'antd';
import { MailCheck, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { changeEmail } from '@/api/auth';
import { isApiError } from '@/api/errors';
import { useAuth } from '@/auth/useAuth';

interface ChangeEmailFormValues {
  newEmail: string;
  currentPassword: string;
}

/**
 * Account settings for the signed-in user. The only mutation is the address
 * change: it requires the current password, sends a confirmation message to the
 * new address, and never replaces the active address before that message is
 * confirmed. The backend invalidates every earlier JWT after the confirmation,
 * so the page tells the user to sign in again.
 */
export function AccountPage() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const { message } = AntApp.useApp();
  const [form] = Form.useForm<ChangeEmailFormValues>();
  const [sentTo, setSentTo] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: changeEmail,
    onSuccess: (_result, variables) => {
      setSentTo(variables.newEmail);
      form.resetFields(['currentPassword']);
      void message.success(t('account.sent'));
    },
    onError: (error) => {
      void message.error(
        isApiError(error) ? error.message : t('account.error'),
      );
    },
  });

  const emailVerifiedLabel =
    user?.emailVerified === true
      ? t('account.verified')
      : user?.emailVerified === false
        ? t('account.unverified')
        : t('account.legacyVerified');

  return (
    <Space direction="vertical" size={20} style={{ width: '100%', maxWidth: 760 }}>
      <div>
        <Typography.Title level={1} style={{ marginBottom: 4 }}>
          {t('account.title')}
        </Typography.Title>
        <Typography.Text type="secondary">{t('account.subtitle')}</Typography.Text>
      </div>

      <Card title={t('account.currentAddress')}>
        <Descriptions column={1} size="small">
          <Descriptions.Item label={t('account.email')}>
            <Space>
              <span>{user?.email ?? t('account.unknown')}</span>
              <Tag color={user?.emailVerified === false ? 'gold' : 'green'}>
                {emailVerifiedLabel}
              </Tag>
            </Space>
          </Descriptions.Item>
          <Descriptions.Item label={t('account.pendingAddress')}>
            {user?.pendingEmail ?? sentTo ?? t('account.none')}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      <Card
        title={
          <Space>
            <ShieldCheck size={18} aria-hidden="true" />
            <span>{t('account.changeTitle')}</span>
          </Space>
        }
      >
        <Alert
          type="info"
          showIcon
          icon={<MailCheck size={18} aria-hidden="true" />}
          title={t('account.changeNotice')}
          description={t('account.changeNoticeBody')}
          style={{ marginBottom: 18 }}
        />

        {sentTo && (
          <Alert
            type="success"
            showIcon
            role="status"
            title={t('account.sentTitle')}
            description={t('account.sentBody', { email: sentTo })}
            style={{ marginBottom: 18 }}
          />
        )}

        <Form
          form={form}
          layout="vertical"
          requiredMark={false}
          onFinish={(values) => mutation.mutate(values)}
        >
          <Form.Item
            name="newEmail"
            label={t('account.newEmail')}
            rules={[
              { required: true, message: t('account.newEmailRequired') },
              { type: 'email', message: t('account.newEmailInvalid') },
            ]}
          >
            <Input
              autoComplete="email"
              inputMode="email"
              placeholder={t('account.newEmailPlaceholder')}
              maxLength={255}
            />
          </Form.Item>

          <Form.Item
            name="currentPassword"
            label={t('account.currentPassword')}
            rules={[{ required: true, message: t('account.currentPasswordRequired') }]}
          >
            <Input.Password
              autoComplete="current-password"
              maxLength={128}
              placeholder={t('account.currentPasswordPlaceholder')}
            />
          </Form.Item>

          <Button type="primary" htmlType="submit" loading={mutation.isPending}>
            {t('account.submit')}
          </Button>
        </Form>

        <Typography.Paragraph type="secondary" style={{ margin: '14px 0 0' }}>
          {t('account.signOutNotice')}
        </Typography.Paragraph>
      </Card>
    </Space>
  );
}
