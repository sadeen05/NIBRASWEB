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
import { AuthAside } from '@/pages/auth/AuthAside';

interface LoginFormValues {
  email: string;
  password: string;
}

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
    try {
      const authenticated = await login(values.email.trim(), values.password);
      navigate(safeRedirectTarget(fromTarget(location.state), authenticated.role), { replace: true });
    } catch (error) {
      if (!isApiError(error)) {
        setErrorMessage(t('login.unexpectedError'));
      } else if (error.kind === 'unauthorized') {
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
              <h2 style={{ margin: 0, fontSize: 22 }}>{t('login.title')}</h2>
              <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
                {t('login.subtitle')}
              </p>
            </div>
            <LanguageToggle />
          </div>

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
              ]}
            >
              <Input
                size="large"
                type="email"
                autoComplete="username"
                placeholder={t('login.emailPlaceholder')}
                prefix={<Mail size={16} aria-hidden="true" />}
              />
            </Form.Item>

            <Form.Item
              name="password"
              label={t('login.passwordLabel')}
              rules={[{ required: true, message: t('login.passwordRequired') }]}
            >
              <Input.Password
                size="large"
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
