import { useMutation } from '@tanstack/react-query';
import { Alert, Button, Form, Input, Space, Typography } from 'antd';
import { ArrowLeft, CheckCircle2, HandCoins, Lock, Mail, Phone, Sprout, User } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';

import { register as registerAccount } from '@/api/auth';
import { resolveApiMessage } from '@/api/knownMessages';
import { LanguageToggle } from '@/components/LanguageToggle';
import { AuthAside } from '@/pages/auth/AuthAside';
import type { AuthUserSummary, RegisterRequest, RegisterRoleId } from '@/types/auth';

/**
 * Bounds mirrored from the backend `RegisterRequest` DTO so the client rejects
 * the same input the server would. `MaxUtf8Bytes(72)` is a byte limit, not a
 * character limit, hence the explicit UTF-8 check on the password.
 */
const FULL_NAME_MAX = 100;
const EMAIL_MAX = 255;
const PHONE_MAX = 20;
const PASSWORD_MIN = 10;
const PASSWORD_MAX_CHARS = 128;
const PASSWORD_MAX_BYTES = 72;
const COMPANY_NAME_MAX = 200;
const COMPANY_REGISTRATION_MAX = 100;

const LANDLORD_ROLE_ID: RegisterRoleId = 1;
const INVESTOR_ROLE_ID: RegisterRoleId = 2;

interface RegisterFormValues {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  companyName?: string;
  companyRegistrationNumber?: string;
}

type RegisterStep = 'role' | 'form' | 'done';

/** UTF-8 byte length, matching the server's `MaxUtf8Bytes` validation. */
function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length;
}

/**
 * Public self-registration. Only Landlord (1) and Investor (2) are offered; the
 * endpoint never returns a session, so a successful registration ends on a
 * confirmation step that points the user at the sign-in page instead of
 * pretending they are signed in.
 */
export function RegisterPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [step, setStep] = useState<RegisterStep>('role');
  const [roleId, setRoleId] = useState<RegisterRoleId>(LANDLORD_ROLE_ID);
  const [created, setCreated] = useState<AuthUserSummary | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [form] = Form.useForm<RegisterFormValues>();

  const isInvestor = roleId === INVESTOR_ROLE_ID;
  const roleLabel = isInvestor ? t('roles.Investor') : t('roles.Landlord');
  const createdIsInvestor = created?.role === 'Investor';

  const mutation = useMutation({
    mutationFn: (values: RegisterFormValues) => {
      const payload: RegisterRequest = {
        fullName: values.fullName.trim(),
        email: values.email.trim(),
        phone: values.phone.trim(),
        password: values.password,
        roleId,
        companyName: isInvestor ? (values.companyName ?? '').trim() : null,
        companyRegistrationNumber: isInvestor
          ? (values.companyRegistrationNumber ?? '').trim()
          : null,
      };
      return registerAccount(payload);
    },
    onSuccess: (summary) => {
      setErrorMessage(null);
      setCreated(summary);
      setStep('done');
    },
    onError: (error: unknown) => setErrorMessage(resolveApiMessage(error, t)),
  });

  const chooseRole = (next: RegisterRoleId) => {
    setRoleId(next);
    setErrorMessage(null);
    setCreated(null);
    form.resetFields();
    setStep('form');
  };

  const backToRoles = () => {
    setErrorMessage(null);
    setStep('role');
  };

  const heading =
    step === 'role' ? t('register.title') : step === 'done' ? t('register.successTitle') : null;
  const subtitle = step === 'role' ? t('register.subtitle') : t('register.formSubtitle');

  return (
    <div className="login-layout">
      <AuthAside />

      <section className="login-panel">
        <div className="login-card">
          <div className="login-card__header">
            <div>
              <h2 style={{ margin: 0, fontSize: 22 }}>
                {heading ?? t('register.formTitle', { role: roleLabel })}
              </h2>
              {step !== 'done' && (
                <p style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>{subtitle}</p>
              )}
            </div>
            <LanguageToggle />
          </div>

          {step === 'role' && (
            <>
              <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
                {t('register.chooseRole')}
              </Typography.Paragraph>
              <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
                <Button
                  size="large"
                  block
                  icon={<Sprout size={18} aria-hidden="true" />}
                  onClick={() => chooseRole(LANDLORD_ROLE_ID)}
                  style={{ height: 'auto', padding: '14px 16px', whiteSpace: 'normal' }}
                >
                  <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    <span style={{ fontWeight: 600 }}>{t('roles.Landlord')}</span>
                    <span style={{ fontWeight: 400, fontSize: 12, opacity: 0.75 }}>
                      {t('register.landlordHint')}
                    </span>
                  </span>
                </Button>
                <Button
                  size="large"
                  block
                  icon={<HandCoins size={18} aria-hidden="true" />}
                  onClick={() => chooseRole(INVESTOR_ROLE_ID)}
                  style={{ height: 'auto', padding: '14px 16px', whiteSpace: 'normal' }}
                >
                  <span style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
                    <span style={{ fontWeight: 600 }}>{t('roles.Investor')}</span>
                    <span style={{ fontWeight: 400, fontSize: 12, opacity: 0.75 }}>
                      {t('register.investorHint')}
                    </span>
                  </span>
                </Button>
              </Space>
              <p style={{ margin: '20px 0 0', textAlign: 'center' }}>
                <Typography.Text type="secondary">{t('register.alreadyHaveAccount')}</Typography.Text>{' '}
                <Link to="/login">{t('register.signInLink')}</Link>
              </p>
            </>
          )}

          {step === 'form' && (
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

              <Form<RegisterFormValues>
                form={form}
                layout="vertical"
                requiredMark={false}
                onFinish={(values) => mutation.mutate(values)}
                disabled={mutation.isPending}
                autoComplete="on"
              >
                <Form.Item
                  name="fullName"
                  label={t('register.fullName')}
                  rules={[
                    { required: true, message: t('register.required') },
                    { max: FULL_NAME_MAX, message: t('register.maxLength', { max: FULL_NAME_MAX }) },
                  ]}
                >
                  <Input
                    size="large"
                    maxLength={FULL_NAME_MAX}
                    autoComplete="name"
                    placeholder={t('register.fullNamePlaceholder')}
                    prefix={<User size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                <Form.Item
                  name="email"
                  label={t('register.email')}
                  rules={[
                    { required: true, message: t('register.required') },
                    { type: 'email', message: t('register.emailInvalid') },
                    { max: EMAIL_MAX, message: t('register.maxLength', { max: EMAIL_MAX }) },
                  ]}
                >
                  <Input
                    size="large"
                    type="email"
                    maxLength={EMAIL_MAX}
                    autoComplete="email"
                    placeholder={t('register.emailPlaceholder')}
                    prefix={<Mail size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                <Form.Item
                  name="phone"
                  label={t('register.phone')}
                  rules={[
                    { required: true, message: t('register.required') },
                    { max: PHONE_MAX, message: t('register.maxLength', { max: PHONE_MAX }) },
                  ]}
                >
                  <Input
                    size="large"
                    maxLength={PHONE_MAX}
                    autoComplete="tel"
                    placeholder={t('register.phonePlaceholder')}
                    prefix={<Phone size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                <Form.Item
                  name="password"
                  label={t('register.password')}
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
                    placeholder={t('register.passwordPlaceholder')}
                    prefix={<Lock size={16} aria-hidden="true" />}
                  />
                </Form.Item>

                {isInvestor && (
                  <>
                    <Typography.Title level={5} style={{ marginTop: 4 }}>
                      {t('register.companySectionTitle')}
                    </Typography.Title>
                    <Form.Item
                      name="companyName"
                      label={t('register.companyName')}
                      rules={[
                        { required: true, message: t('register.required') },
                        {
                          max: COMPANY_NAME_MAX,
                          message: t('register.maxLength', { max: COMPANY_NAME_MAX }),
                        },
                      ]}
                    >
                      <Input
                        size="large"
                        maxLength={COMPANY_NAME_MAX}
                        autoComplete="organization"
                        placeholder={t('register.companyNamePlaceholder')}
                      />
                    </Form.Item>
                    <Form.Item
                      name="companyRegistrationNumber"
                      label={t('register.companyRegistrationNumber')}
                      rules={[
                        { required: true, message: t('register.required') },
                        {
                          max: COMPANY_REGISTRATION_MAX,
                          message: t('register.maxLength', { max: COMPANY_REGISTRATION_MAX }),
                        },
                      ]}
                    >
                      <Input
                        size="large"
                        maxLength={COMPANY_REGISTRATION_MAX}
                        placeholder={t('register.companyRegistrationNumberPlaceholder')}
                      />
                    </Form.Item>
                    <Typography.Paragraph type="secondary">
                      {t('register.companySectionHint')}
                    </Typography.Paragraph>
                  </>
                )}

                <Form.Item style={{ marginBottom: 0, marginTop: 8 }}>
                  <Button
                    type="primary"
                    size="large"
                    htmlType="submit"
                    block
                    loading={mutation.isPending}
                    disabled={mutation.isPending}
                  >
                    {mutation.isPending ? t('register.submitting') : t('register.submit')}
                  </Button>
                </Form.Item>
              </Form>

              <div style={{ marginTop: 16 }}>
                <Button
                  type="link"
                  icon={<ArrowLeft size={16} aria-hidden="true" />}
                  onClick={backToRoles}
                >
                  {t('register.backToRoles')}
                </Button>
              </div>
            </>
          )}

          {step === 'done' && (
            <div role="status" aria-live="polite" style={{ textAlign: 'center' }}>
              <CheckCircle2 size={44} color="var(--nb-primary)" aria-hidden="true" />
              <Typography.Paragraph style={{ margin: '14px 0 0' }}>
                {createdIsInvestor
                  ? t('register.successInvestorPending')
                  : t('register.successLandlord')}
              </Typography.Paragraph>
              <Button
                type="primary"
                size="large"
                block
                onClick={() => navigate('/login')}
                style={{ marginTop: 12 }}
              >
                {t('register.goToSignIn')}
              </Button>
            </div>
          )}
        </div>
      </section>
    </div>
  );
}
