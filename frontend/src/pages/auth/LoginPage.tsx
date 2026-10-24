import { Alert, Button, Form, Input, Typography } from 'antd';
import { Lock, Mail } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom';

import { isApiError, type ApiError } from '@/api/errors';
import { safeRedirectTarget } from '@/auth/routeAccess';
import { useAuth } from '@/auth/useAuth';
import { FullPageState } from '@/components/FullPageState';
import { LanguageToggle } from '@/components/LanguageToggle';
import { VerificationResendForm } from '@/components/VerificationResendForm';
import { AuthAside } from '@/pages/auth/AuthAside';

interface LoginFormValues {
  email: string;
  password: string;
}

/**
 * The login form caps input at the same lengths the backend `LoginRequest`
 * declares (Email 255, Password 128), so an over-long entry is guided at the
 * field instead of being rejected by the server after submission.
 */
const EMAIL_MAX = 255;
const PASSWORD_MAX = 128;

interface LocationState {
  from?: string;
}

/** `RequireAuth` stashes the requested path in `location.state.from`. */
function fromTarget(state: unknown): unknown {
  if (typeof state === 'object' && state !== null && 'from' in state) {
    return (state as LocationState).from;
  }
  return undefined;
}

/**
 * The exact default messages ASP.NET Core produces for the login DTO's
 * DataAnnotations. Only these known strings are translated; any other server
 * message is shown unchanged rather than guessed at.
 */
const KNOWN_LOGIN_VALIDATION_KEYS: Record<string, string> = {
  'The Email field is required.': 'login.emailRequired',
  'The Email field is not a valid e-mail address.': 'login.emailInvalid',
  'The Password field is required.': 'login.passwordRequired',
};

function findKnownValidationKey(error: ApiError): string | null {
  if (!error.validationErrors) {
    return null;
  }

  for (const messages of Object.values(error.validationErrors)) {
    for (const message of messages) {
      const key = KNOWN_LOGIN_VALIDATION_KEYS[message];
      if (key) {
        return key;
      }
    }
  }

  return null;
}

export function LoginPage() {
  const { t } = useTranslation();
  const { status, user, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [submitting, setSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [emailNotVerified, setEmailNotVerified] = useState(false);
  const [attemptedEmail, setAttemptedEmail] = useState('');

  if (status === 'authenticated') {
    // Only honour the intended destination when this role may actually open it;
    // otherwise land on the Overview page instead of bouncing to `/403`.
    return <Navigate to={safeRedirectTarget(fromTarget(location.state), user?.role)} replace />;
  }

  if (status === 'loading') {
    return (
      <FullPageState
        variant="loading"
        title={t('errors.checkingSessionTitle')}
        description={t('errors.checkingSessionBody')}
      />
    );
  }

  const handleSubmit = async (values: LoginFormValues) => {
    setSubmitting(true);
    setErrorMessage(null);
    setEmailNotVerified(false);
    try {
      const authenticated = await login(values.email.trim(), values.password);
      navigate(safeRedirectTarget(fromTarget(location.state), authenticated.role), { replace: true });
    } catch (error) {
      if (!isApiError(error)) {
        setErrorMessage(t('login.unexpectedError'));
      } else if (error.status === 403 && error.errorCode === 'EMAIL_NOT_VERIFIED') {
        // The address of the account has not been confirmed yet. This is a different
        // situation than a wrong password, and the account is not wrong: the page explains
        // how to get a new verification message.
        setEmailNotVerified(true);
        setAttemptedEmail(values.email.trim());
      } else if (error.kind === 'unauthorized') {
        // A wrong address and a wrong password still answer the same generic message.
        setErrorMessage(t('login.invalidCredentials'));
      } else if (error.kind === 'network' || error.kind === 'unavailable') {
        setErrorMessage(t('login.networkError'));
      } else {
        // Keep whatever the backend said unless it is a known login validation
        // message that we already have localized copy for.
        const knownKey = findKnownValidationKey(error);
        setErrorMessage(knownKey ? t(knownKey) : error.message);
      }
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
              <h1 style={{ margin: 0, fontSize: 22 }}>{t('login.title')}</h1>
              <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
                {t('login.subtitle')}
              </p>
            </div>
            <LanguageToggle />
          </div>

          {emailNotVerified && (
            <div style={{ marginBottom: 18 }}>
              <Alert
                type="warning"
                showIcon
                role="status"
                title={t('login.emailNotVerifiedTitle')}
                description={t('login.emailNotVerifiedBody')}
              />
              <Button
                type="primary"
                size="large"
                block
                onClick={() => navigate('/verify-email')}
                style={{ marginTop: 12 }}
              >
                {t('login.goToVerify')}
              </Button>

              <VerificationResendForm initialEmail={attemptedEmail} />
            </div>
          )}

          {errorMessage && (
            <Alert
              type="error"
              showIcon
              role="alert"
              title={errorMessage}
              style={{ marginBottom: 18 }}
            />
          )}

          <Form<LoginFormValues>
            layout="vertical"
            requiredMark={false}
            onFinish={handleSubmit}
            disabled={submitting}
            autoComplete="on"
          >
            <Form.Item
              name="email"
              label={t('login.emailLabel')}
              rules={[
                { required: true, message: t('login.emailRequired') },
                { type: 'email', message: t('login.emailInvalid') },
                { max: EMAIL_MAX, message: t('login.maxLength', { max: EMAIL_MAX }) },
              ]}
            >
              <Input
                size="large"
                type="email"
                maxLength={EMAIL_MAX}
                autoComplete="username"
                placeholder={t('login.emailPlaceholder')}
                prefix={<Mail size={16} aria-hidden="true" />}
              />
            </Form.Item>

            <Form.Item
              name="password"
              label={t('login.passwordLabel')}
              rules={[
                { required: true, message: t('login.passwordRequired') },
                { max: PASSWORD_MAX, message: t('login.maxLength', { max: PASSWORD_MAX }) },
              ]}
            >
              <Input.Password
                size="large"
                maxLength={PASSWORD_MAX}
                autoComplete="current-password"
                placeholder={t('login.passwordPlaceholder')}
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
                {submitting ? t('login.submitting') : t('login.submit')}
              </Button>
            </Form.Item>
          </Form>

          <p style={{ margin: '18px 0 0', textAlign: 'center' }}>
            <Typography.Text type="secondary">{t('register.noAccount')}</Typography.Text>{' '}
            <Link to="/register">{t('register.createAccountLink')}</Link>
          </p>
        </div>
      </section>
    </div>
  );
}
