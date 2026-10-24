import { Alert, Button, Form, Input, Spin, Typography } from 'antd';
import { CheckCircle2, Lock } from 'lucide-react';
import { useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';

import { activateAdmin } from '@/api/auth';
import { isApiError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import { LanguageToggle } from '@/components/LanguageToggle';
import { AuthAside } from '@/pages/auth/AuthAside';
import { takeTokenFromFragment } from '@/pages/auth/verificationToken';
import type { ActivateAdminRequest } from '@/types/auth';

/**
 * Bounds mirrored from the backend `ActivateAdminRequest` DTO, so the client
 * rejects the same input the server would. `MaxUtf8Bytes(72)` is a byte limit,
 * not a character limit, hence the explicit UTF-8 check.
 */
const PASSWORD_MIN = 10;
const PASSWORD_MAX_CHARS = 128;
const PASSWORD_MAX_BYTES = 72;

interface ActivateAdminFormValues {
  password: string;
  confirmPassword: string;
}

type TokenState = 'pending' | 'present' | 'missing';

/** UTF-8 byte length, matching the server's `MaxUtf8Bytes` validation. */
function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

/**
 * Activation of a new administrator account.
 *
 * The single-use invitation token is the only credential: it is read from the
 * URL fragment, removed from the address bar and the history, and kept in
 * component state only. It is never written to storage, never logged and never
 * sent anywhere except the activation request body.
 */
export function ActivateAdminPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [form] = Form.useForm<ActivateAdminFormValues>();
  const [tokenState, setTokenState] = useState<TokenState>('pending');
  const [token, setToken] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [activated, setActivated] = useState(false);
  const tokenTaken = useRef(false);

  useLayoutEffect(() => {
    // React's StrictMode runs effects twice in development. The ref guard keeps
    // the token that was taken on the first run - the fragment is already gone
    // by the second one.
    if (tokenTaken.current) {
      return;
    }
    tokenTaken.current = true;
    const found = takeTokenFromFragment();
    setToken(found);
    setTokenState(found ? 'present' : 'missing');
  }, []);

  /**
   * Maps the endpoint's answers to safe copy. The backend answers every failed
   * activation with the same generic message, so the page never tells more
   * than the server does: only the address-in-use conflict, which the server
   * states explicitly, is reported on its own.
   */
  const describeError = (error: unknown): string => {
    if (!isApiError(error)) {
      return t('activateAdmin.unexpectedError');
    }

    // A password that the server's own policy rejected is a validation
    // failure, not a bad link: keep the specific message.
    if (error.validationErrors) {
      return resolveApiMessage(error, t);
    }

    switch (error.status) {
      case 400:
        return t('activateAdmin.invalidLink');
      case 409:
        return error.errorCode === 'ACTIVATION_CONFLICT'
          ? t('activateAdmin.addressInUse')
          : t('activateAdmin.invalidLink');
      case 429:
        return t('activateAdmin.rateLimited');
      default:
        return error.kind === 'network' || error.kind === 'unavailable'
          ? t('activateAdmin.networkError')
          : resolveApiMessage(error, t);
    }
  };

  const handleSubmit = async (values: ActivateAdminFormValues) => {
    if (!token) {
      return;
    }

    setSubmitting(true);
    setErrorMessage(null);
    try {
      const payload: ActivateAdminRequest = { token, password: values.password };
      await activateAdmin(payload);
      // The token is single-use and now spent: it leaves the component state
      // together with the password, so neither lingers in the page.
      setToken(null);
      setActivated(true);
      form.resetFields();
    } catch (error) {
      setErrorMessage(describeError(error));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="login-layout">
      <AuthAside />

      <section className="login-panel">
        <div className="login-card">
          <div className="login-card__header">
            <div>
              <h1 style={{ margin: 0, fontSize: 22 }}>{t('activateAdmin.title')}</h1>
              <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
                {t('activateAdmin.subtitle')}
              </p>
            </div>
            <LanguageToggle />
          </div>

          {activated ? (
            <div role="status" aria-live="polite" style={{ textAlign: 'center' }}>
              <CheckCircle2 size={44} color="var(--nb-primary)" aria-hidden="true" />
              <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 14, marginBottom: 0 }}>
                {t('activateAdmin.successTitle')}
              </Typography.Title>
              <Typography.Paragraph style={{ margin: '8px 0 0' }}>
                {t('activateAdmin.successBody')}
              </Typography.Paragraph>
              <Button
                type="primary"
                size="large"
                block
                onClick={() => navigate('/login')}
                style={{ marginTop: 12 }}
              >
                {t('activateAdmin.goToSignIn')}
              </Button>
            </div>
          ) : tokenState === 'missing' ? (
            <>
              <Alert
                type="error"
                showIcon
                role="alert"
                title={t('activateAdmin.missingTokenTitle')}
                description={t('activateAdmin.missingTokenBody')}
              />
              <p style={{ margin: '18px 0 0', textAlign: 'center' }}>
                <Link to="/login">{t('activateAdmin.backToSignIn')}</Link>
              </p>
            </>
          ) : tokenState === 'pending' ? (
            <div role="status" aria-live="polite" style={{ textAlign: 'center', padding: '24px 0' }}>
              <Spin />
              <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
                {t('activateAdmin.loading')}
              </Typography.Paragraph>
            </div>
          ) : (
            <>
              {errorMessage && (
                <Alert
                  type="error"
                  showIcon
                  role="alert"
                  title={errorMessage}
                  style={{ marginBottom: 18 }}
                />
              )}

              <Form<ActivateAdminFormValues>
                form={form}
                layout="vertical"
                requiredMark={false}
                onFinish={handleSubmit}
                disabled={submitting}
                autoComplete="off"
              >
                <Form.Item
                  name="password"
                  label={t('activateAdmin.passwordLabel')}
                  rules={[
                    { required: true, message: t('register.required') },
                    { min: PASSWORD_MIN, message: t('register.passwordMin') },
                    {
                      max: PASSWORD_MAX_CHARS,
                      message: t('register.maxLength', { max: PASSWORD_MAX_CHARS }),
                    },
                    {
                      validator: (_rule, value: string | undefined) =>
                        !value || utf8ByteLength(value) <= PASSWORD_MAX_BYTES
                          ? Promise.resolve()
                          : Promise.reject(new Error(t('register.passwordTooLong'))),
                    },
                  ]}
                >
                  <Input.Password
                    size="large"
                    maxLength={PASSWORD_MAX_CHARS}
                    autoComplete="new-password"
                    placeholder={t('activateAdmin.passwordPlaceholder')}
                    prefix={<Lock size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                <Form.Item
                  name="confirmPassword"
                  label={t('activateAdmin.confirmPasswordLabel')}
                  dependencies={['password']}
                  rules={[
                    { required: true, message: t('register.required') },
                    {
                      validator: (_rule, value: string | undefined) =>
                        !value || value === form.getFieldValue('password')
                          ? Promise.resolve()
                          : Promise.reject(new Error(t('activateAdmin.passwordsDoNotMatch'))),
                    },
                  ]}
                >
                  <Input.Password
                    size="large"
                    maxLength={PASSWORD_MAX_CHARS}
                    autoComplete="new-password"
                    placeholder={t('activateAdmin.confirmPasswordPlaceholder')}
                    prefix={<Lock size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                <Form.Item style={{ marginBottom: 0, marginTop: 8 }}>
                  <Button
                    type="primary"
                    size="large"
                    htmlType="submit"
                    block
                    loading={submitting}
                    disabled={submitting}
                  >
                    {submitting ? t('activateAdmin.submitting') : t('activateAdmin.submit')}
                  </Button>
                </Form.Item>
              </Form>

              <p style={{ margin: '18px 0 0', textAlign: 'center' }}>
                <Link to="/login">{t('activateAdmin.backToSignIn')}</Link>
              </p>
            </>
          )}
        </div>
      </section>
    </div>
  );
}
