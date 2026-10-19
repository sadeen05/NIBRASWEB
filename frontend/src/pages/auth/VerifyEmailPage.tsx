import { Alert, Button, Spin, Typography } from 'antd';
import { CheckCircle2 } from 'lucide-react';
import { useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { verifyEmail } from '@/api/auth';
import { isApiError } from '@/api/errors';
import { LanguageToggle } from '@/components/LanguageToggle';
import { VerificationResendForm } from '@/components/VerificationResendForm';
import { AuthAside } from '@/pages/auth/AuthAside';
import { takeTokenFromFragment } from '@/pages/auth/verificationToken';
import type { VerifyEmailRequest } from '@/types/auth';

type VerifyState =
  | 'checking'
  | 'success'
  | 'invalid'
  | 'conflict'
  | 'rateLimited'
  | 'network'
  | 'error'
  | 'missingToken';

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
  const [verifyState, setVerifyState] = useState<VerifyState>('checking');
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

  // The countdown of the resend cooldown lives in the shared form.

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

              <VerificationResendForm />

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
