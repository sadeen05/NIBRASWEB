import { Alert, Button, Spin, Typography } from 'antd';
import { CheckCircle2 } from 'lucide-react';
import { useLayoutEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { confirmEmailChange } from '@/api/auth';
import { isApiError } from '@/api/errors';
import { LanguageToggle } from '@/components/LanguageToggle';
import { AuthAside } from '@/pages/auth/AuthAside';
import { takeTokenFromFragment } from '@/pages/auth/verificationToken';
import type { VerifyEmailRequest } from '@/types/auth';

type ConfirmState =
  | 'checking'
  | 'success'
  | 'invalid'
  | 'conflict'
  | 'rateLimited'
  | 'network'
  | 'error'
  | 'missingToken';

function describeError(error: unknown): ConfirmState {
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
 * Public confirmation of a pending address change. The single-use token is
 * read from the URL fragment, removed from the address bar and history before
 * the first paint, and redeemed exactly once. A successful confirmation
 * invalidates every JWT issued before the change, so the user signs in again.
 */
export function ConfirmEmailChangePage() {
  const { t } = useTranslation();
  const [state, setState] = useState<ConfirmState>('checking');
  const redemptionStarted = useRef(false);

  useLayoutEffect(() => {
    if (redemptionStarted.current) {
      return;
    }
    redemptionStarted.current = true;

    const token = takeTokenFromFragment();

    void (async () => {
      if (!token) {
        setState('missingToken');
        return;
      }

      try {
        const payload: VerifyEmailRequest = { token };
        await confirmEmailChange(payload);
        setState('success');
      } catch (error) {
        setState(describeError(error));
      }
    })();
  }, []);

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

  const errorState =
    state === 'invalid' || state === 'missingToken'
      ? renderAlert(t('confirmEmailChange.invalidTitle'), t('confirmEmailChange.invalidBody'))
      : state === 'conflict'
        ? renderAlert(t('confirmEmailChange.conflictTitle'), t('confirmEmailChange.conflictBody'))
        : state === 'rateLimited'
          ? renderAlert(t('confirmEmailChange.rateLimitedTitle'), t('confirmEmailChange.rateLimitedBody'))
          : state === 'network'
            ? renderAlert(t('confirmEmailChange.networkTitle'), t('confirmEmailChange.networkBody'))
            : state === 'error'
              ? renderAlert(t('confirmEmailChange.errorTitle'), t('confirmEmailChange.errorBody'))
              : null;

  return (
    <div className="login-layout">
      <AuthAside />

      <section className="login-panel">
        <div className="login-card">
          <div className="login-card__header">
            <div>
              <h1 style={{ margin: 0, fontSize: 22 }}>{t('confirmEmailChange.title')}</h1>
              <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
                {t('confirmEmailChange.subtitle')}
              </p>
            </div>
            <LanguageToggle />
          </div>

          {state === 'checking' && (
            <div role="status" aria-live="polite" style={{ textAlign: 'center', padding: '24px 0' }}>
              <Spin />
              <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
                {t('confirmEmailChange.checking')}
              </Typography.Paragraph>
            </div>
          )}

          {state === 'success' && (
            <div role="status" aria-live="polite" style={{ textAlign: 'center' }}>
              <CheckCircle2 size={44} color="var(--nb-primary)" aria-hidden="true" />
              <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 14, marginBottom: 0 }}>
                {t('confirmEmailChange.successTitle')}
              </Typography.Title>
              <Typography.Paragraph style={{ margin: '8px 0 0' }}>
                {t('confirmEmailChange.successBody')}
              </Typography.Paragraph>
              <Button type="primary" size="large" block href="/login" style={{ marginTop: 12 }}>
                {t('confirmEmailChange.goToSignIn')}
              </Button>
            </div>
          )}

          {state !== 'checking' && state !== 'success' && (
            <>
              {errorState}
              <p style={{ margin: '18px 0 0', textAlign: 'center' }}>
                <Link to="/login">{t('confirmEmailChange.backToSignIn')}</Link>
              </p>
            </>
          )}
        </div>
      </section>
    </div>
  );
}
