import { Alert, Button, Form, Input, Spin, Typography } from 'antd';
import { CheckCircle2, Mail, RefreshCw } from 'lucide-react';
import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { resendVerification, verifyEmail } from '@/api/auth';
import { isApiError } from '@/api/errors';
import { LanguageToggle } from '@/components/LanguageToggle';
import { AuthAside } from '@/pages/auth/AuthAside';
import { takeTokenFromFragment } from '@/pages/auth/verificationToken';
import type { ResendVerificationRequest, VerifyEmailRequest } from '@/types/auth';

/**
 * The cooldown of the resend endpoint, mirrored from the backend configuration
 * (`Email:ResendCooldownSeconds`, default 60). The countdown is a courtesy: the
 * server enforces the limit, the page only avoids a pointless round trip.
 */
const RESEND_COOLDOWN_SECONDS = 60;

type VerifyState =
  | 'checking'
  | 'success'
  | 'invalid'
  | 'conflict'
  | 'rateLimited'
  | 'network'
  | 'error'
  | 'missingToken';

type ResendState = 'idle' | 'sending' | 'sent' | 'failed';

interface ResendFormValues {
  email: string;
}

/**
 * Maps the answer of the endpoint to a state. The server answers every failed
 * confirmation with the same generic message, so the page never tells more than
 * the server does: only the address-in-use conflict, which the server states
 * explicitly, is reported on its own.
 */
function describeVerificationError(error: unknown): VerifyState {
  if (!isApiError(error)) {
    return 'error';
  }

  switch (error.status) {
    case 400:
      return 'invalid';
    case 409:
      return 'conflict';
    case 429:
      return 'rateLimited';
    default:
      return error.kind === 'network' ||
        error.kind === 'unavailable' ||
        error.kind === 'timeout'
        ? 'network'
        : 'error';
  }
}

/**
 * Public confirmation of the address of a new Landlord or Investor account.
 *
 * The single-use token is read from the URL fragment, the fragment is removed
 * from the address bar and from the history before the first paint, and the
 * token is redeemed exactly once. It is never written to storage, never logged
 * and never sent anywhere except the request body of the confirmation.
 */
export function VerifyEmailPage() {
  const { t } = useTranslation();
  const [form] = Form.useForm<ResendFormValues>();
  const [verifyState, setVerifyState] = useState<VerifyState>('checking');
  const [resendState, setResendState] = useState<ResendState>('idle');
  const [resendError, setResendError] = useState<string | null>(null);
  const [cooldown, setCooldown] = useState(0);
  const redemptionStarted = useRef(false);

  useLayoutEffect(() => {
    // React's StrictMode runs the effect twice in development. The guard keeps
    // the token that was taken on the first run - the fragment is already gone
    // by the second one - and it makes sure the token is redeemed exactly once.
    if (redemptionStarted.current) {
      return;
    }
    redemptionStarted.current = true;

    const token = takeTokenFromFragment();

    void (async () => {
      if (!token) {
        setVerifyState('missingToken');
        return;
      }

      try {
        const payload: VerifyEmailRequest = { token };
        await verifyEmail(payload);
        setVerifyState('success');
      } catch (error) {
        setVerifyState(describeVerificationError(error));
      }
    })();
  }, []);

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

  const handleResend = async (values: ResendFormValues) => {
    setResendState('sending');
    setResendError(null);

    try {
      const payload: ResendVerificationRequest = { email: values.email.trim() };
      await resendVerification(payload);

      // The answer is the same for every address, so nothing is shown about
      // the account. The cooldown starts now, so the next request is not sent
      // before the server would accept it.
      setResendState('sent');
      setCooldown(RESEND_COOLDOWN_SECONDS);
      form.resetFields();
    } catch (error) {
      setResendState('failed');
      setResendError(
        isApiError(error) && error.status === 429
          ? t('verifyEmail.rateLimitedBody')
          : t('verifyEmail.resendFailed'),
      );
    }
  };

  const renderAlert = (title: string, body: string) => (
    <Alert
      type="error"
      showIcon
      role="alert"
      title={title}
      description={body}
      style={{ marginBottom: 18 }}
    />
  );

  const state =
    verifyState === 'invalid' || verifyState === 'missingToken'
      ? renderAlert(t('verifyEmail.invalidTitle'), t('verifyEmail.invalidBody'))
      : verifyState === 'conflict'
        ? renderAlert(t('verifyEmail.conflictTitle'), t('verifyEmail.conflictBody'))
        : verifyState === 'rateLimited'
          ? renderAlert(t('verifyEmail.rateLimitedTitle'), t('verifyEmail.rateLimitedBody'))
          : verifyState === 'network'
            ? renderAlert(t('verifyEmail.networkTitle'), t('verifyEmail.networkBody'))
            : verifyState === 'error'
              ? renderAlert(t('verifyEmail.errorTitle'), t('verifyEmail.errorBody'))
              : null;

  return (
    <div className="login-layout">
      <AuthAside />

      <section className="login-panel">
        <div className="login-card">
          <div className="login-card__header">
            <div>
              <h2 style={{ margin: 0, fontSize: 22 }}>{t('verifyEmail.title')}</h2>
              <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
                {t('verifyEmail.subtitle')}
              </p>
            </div>
            <LanguageToggle />
          </div>

          {verifyState === 'checking' && (
            <div role="status" aria-live="polite" style={{ textAlign: 'center', padding: '24px 0' }}>
              <Spin />
              <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
                {t('verifyEmail.checking')}
              </Typography.Paragraph>
            </div>
          )}

          {verifyState === 'success' && (
            <div role="status" aria-live="polite" style={{ textAlign: 'center' }}>
              <CheckCircle2 size={44} color="var(--nb-primary)" aria-hidden="true" />
              <Typography.Title level={5} style={{ marginTop: 14, marginBottom: 0 }}>
                {t('verifyEmail.successTitle')}
              </Typography.Title>
              <Typography.Paragraph style={{ margin: '8px 0 0' }}>
                {t('verifyEmail.successBody')}
              </Typography.Paragraph>
              <Button
                type="primary"
                size="large"
                block
                href="/login"
                style={{ marginTop: 12 }}
              >
                {t('verifyEmail.goToSignIn')}
              </Button>
            </div>
          )}

          {verifyState !== 'checking' && verifyState !== 'success' && (
            <>
              {state}

              <Typography.Title level={5} style={{ marginTop: 4 }}>
                {t('verifyEmail.resendTitle')}
              </Typography.Title>
              <Typography.Paragraph type="secondary">
                {t('verifyEmail.resendSubtitle')}
              </Typography.Paragraph>

              {resendError && (
                <Alert
                  type="error"
                  showIcon
                  role="alert"
                  title={resendError}
                  style={{ marginBottom: 18 }}
                />
              )}

              {resendState === 'sent' && (
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
                onFinish={handleResend}
                disabled={resendState === 'sending' || cooldown > 0}
                autoComplete="off"
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
                    loading={resendState === 'sending'}
                    disabled={resendState === 'sending' || cooldown > 0}
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

              <p style={{ margin: '18px 0 0', textAlign: 'center' }}>
                <Link to="/login">{t('verifyEmail.backToSignIn')}</Link>
              </p>
            </>
          )}
        </div>
      </section>
    </div>
  );
}
