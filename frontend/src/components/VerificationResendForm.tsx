import { Alert, Button, Form, Input, Typography } from 'antd';
import { Mail, RefreshCw } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { resendVerification } from '@/api/auth';
import { isApiError } from '@/api/errors';
import type { ResendVerificationRequest } from '@/types/auth';

/**
 * The cooldown of the resend endpoint, mirrored from the backend configuration
 * (`Email:ResendCooldownSeconds`, default 60). The countdown is a courtesy: the
 * server enforces the limit, the form only avoids a pointless round trip.
 */
const RESEND_COOLDOWN_SECONDS = 60;

interface ResendFormValues {
  email: string;
}

interface VerificationResendFormProps {
  /** The address to pre-fill, e.g. the one the user just entered. */
  initialEmail?: string;
}

/**
 * A request for a new verification mail.
 *
 * The endpoint answers with the same generic message for every address, so the
 * form never shows - and never asks the server to reveal - whether the address
 * belongs to an account or whether it still has to be verified.
 */
export function VerificationResendForm({ initialEmail }: VerificationResendFormProps) {
  const { t } = useTranslation();
  const [form] = Form.useForm<ResendFormValues>();
  const [state, setState] = useState<'idle' | 'sending' | 'sent' | 'failed'>('idle');
  const [error, setError] = useState<string | null>(null);
  const [cooldown, setCooldown] = useState(0);

  // The countdown of the resend cooldown. It only tells the user when the next
  // request can be made; the server enforces the limit.
  useEffect(() => {
    if (cooldown <= 0) {
      return;
    }

    const timer = window.setInterval(() => {
      setCooldown((current) => (current > 0 ? current - 1 : 0));
    }, 1000);

    return () => window.clearInterval(timer);
  }, [cooldown]);

  const handleSubmit = async (values: ResendFormValues) => {
    setState('sending');
    setError(null);

    try {
      const payload: ResendVerificationRequest = { email: values.email.trim() };
      await resendVerification(payload);

      // The answer is the same for every address, so nothing is shown about the
      // account. The cooldown starts now, so the next request is not sent
      // before the server would accept it.
      setState('sent');
      setCooldown(RESEND_COOLDOWN_SECONDS);
      form.resetFields();
    } catch (caught) {
      setState('failed');
      setError(
        isApiError(caught) && caught.status === 429
          ? t('verifyEmail.rateLimitedBody')
          : t('verifyEmail.resendFailed'),
      );
    }
  };

  return (
    <div style={{ marginTop: 18 }}>
      <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 4 }}>
        {t('verifyEmail.resendTitle')}
      </Typography.Title>
      <Typography.Paragraph type="secondary">
        {t('verifyEmail.resendSubtitle')}
      </Typography.Paragraph>

      {error && (
        <Alert type="error" showIcon role="alert" title={error} style={{ marginBottom: 18 }} />
      )}

      {state === 'sent' && (
        <Alert
          type="success"
          showIcon
          role="status"
          title={t('verifyEmail.resendSent')}
          style={{ marginBottom: 18 }}
        />
      )}

      <Form<ResendFormValues>
        form={form}
        layout="vertical"
        requiredMark={false}
        onFinish={handleSubmit}
        disabled={state === 'sending' || cooldown > 0}
        autoComplete="off"
        initialValues={initialEmail ? { email: initialEmail } : undefined}
      >
        <Form.Item
          name="email"
          label={t('verifyEmail.emailLabel')}
          rules={[
            { required: true, message: t('verifyEmail.emailRequired') },
            { type: 'email', message: t('verifyEmail.emailInvalid') },
          ]}
        >
          <Input
            size="large"
            type="email"
            maxLength={255}
            autoComplete="email"
            placeholder={t('verifyEmail.emailPlaceholder')}
            prefix={<Mail size={16} aria-hidden="true" />}
          />
        </Form.Item>

        <Form.Item style={{ marginBottom: 0, marginTop: 8 }}>
          <Button
            type="primary"
            size="large"
            htmlType="submit"
            block
            icon={<RefreshCw size={16} aria-hidden="true" />}
            loading={state === 'sending'}
            disabled={state === 'sending' || cooldown > 0}
          >
            {t('verifyEmail.resendSubmit')}
          </Button>
        </Form.Item>
      </Form>

      {cooldown > 0 && (
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('verifyEmail.cooldown', { seconds: cooldown })}
        </Typography.Paragraph>
      )}
    </div>
  );
}
