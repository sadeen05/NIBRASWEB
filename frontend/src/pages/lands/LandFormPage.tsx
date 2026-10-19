import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Col,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Typography,
} from 'antd';
import { ArrowLeft, Save } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { createLand, fetchLand, updateLand } from '@/api/lands';
import { fetchCurrentCriterion, fetchGovernorates } from '@/api/reference';
import { FullPageState } from '@/components/FullPageState';
import { criterionRequirementRows, describeEligibilityFailureWithThresholds } from '@/pages/lands/criteriaFormat';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import type { LandWriteRequest } from '@/types/lands';

const DESCRIPTION_MAX = 1000;

interface LandFormValues {
  governorateId?: number;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  description?: string;
}

interface LandFormPageProps {
  mode: 'create' | 'edit';
}

/**
 * Landlord land registration / edit. Only landlord-editable fields are collected:
 * governorate, location identifiers, area and an optional description. The region,
 * the grid and the technical measurements are recorded later by an admin technical
 * assessment and are never entered here.
 */
export function LandFormPage({ mode }: LandFormPageProps) {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const { id: idParam } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<LandFormValues>();
  const [submitError, setSubmitError] = useState<string | null>(null);
  const submitErrorRef = useRef<HTMLDivElement | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const isArabic = locale.startsWith('ar');
  const editId = mode === 'edit' ? Number(idParam) : null;
  const validEditId = editId !== null && Number.isInteger(editId) && editId > 0;

  const governoratesQuery = useQuery({
    queryKey: ['reference', 'governorates'],
    queryFn: fetchGovernorates,
    staleTime: 5 * 60_000,
  });

  // The thresholds the server will check on save, shown so the landlord is not
  // guessing. Purely informational: the backend stays the only authority.
  const criterionQuery = useQuery({
    queryKey: ['criteria', 'current'],
    queryFn: fetchCurrentCriterion,
    staleTime: 5 * 60_000,
  });

  const criterionRows = useMemo(
    () => criterionRequirementRows(t, locale, criterionQuery.data ?? null),
    [t, locale, criterionQuery.data],
  );

  const landQuery = useQuery({
    queryKey: ['land', editId],
    queryFn: () => fetchLand(editId as number),
    enabled: mode === 'edit' && validEditId,
  });

  const land = landQuery.data;

  useEffect(() => {
    if (mode === 'edit' && land) {
      form.setFieldsValue({
        // Left undefined for a legacy land so the owner is never forced to
        // retroactively assign a governorate.
        governorateId: land.governorateId ?? undefined,
        landNumber: land.landNumber,
        village: land.village ?? '',
        basin: land.basin ?? '',
        parcelNumber: land.parcelNumber ?? '',
        areaDonum: land.areaDonum,
        description: land.description ?? '',
      });
    }
  }, [mode, land, form]);

  const governorateOptions = useMemo(
    () =>
      (governoratesQuery.data ?? []).map((governorate) => ({
        value: governorate.id,
        label: isArabic ? governorate.nameAr : governorate.nameEn,
      })),
    [governoratesQuery.data, isArabic],
  );

  const saveMutation = useMutation({
    mutationFn: (payload: LandWriteRequest): Promise<unknown> => {
      if (mode === 'create') {
        return createLand(payload);
      }
      if (editId === null) {
        return Promise.reject(new Error('Missing land id.'));
      }
      return updateLand(editId, payload);
    },
    onSuccess: async (result) => {
      setSubmitError(null);
      await queryClient.invalidateQueries({ queryKey: ['lands'] });
      if (mode === 'create' && result && typeof result === 'object' && 'id' in result) {
        const createdId = (result as { id: number }).id;
        await queryClient.invalidateQueries({ queryKey: ['land', createdId] });
        message.success(t('lands.form.created'));
        navigate(`/app/lands/${createdId}`, { replace: true });
        return;
      }
      await queryClient.invalidateQueries({ queryKey: ['land', editId] });
      message.success(t('lands.form.updated'));
      navigate(`/app/lands/${editId}`, { replace: true });
    },
    onError: (error: unknown) => {
      // Use the exact thresholds when this page already has the current
      // criterion, so a rejected save names the limit that was missed rather
      // than only the direction of the requirement.
      const text =
        describeEligibilityFailureWithThresholds(
          t,
          locale,
          isApiError(error) ? error.message : '',
          criterionQuery.data ?? null,
        ) ?? resolveLandErrorMessage(error, t);
      setSubmitError(text);
      // Make the failure impossible to miss: many forms are taller than the
      // viewport, so the inline alert alone could sit off-screen and a rejected
      // save would look like nothing happened.
      message.error(text);
      submitErrorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'center' });
    },
  });

  const onFinish = (values: LandFormValues) => {
    const description = values.description?.trim() ?? '';
    const payload: LandWriteRequest = {
      landNumber: values.landNumber.trim(),
      village: values.village.trim(),
      basin: values.basin.trim(),
      parcelNumber: values.parcelNumber.trim(),
      areaDonum: values.areaDonum,
      description: description.length > 0 ? description : null,
    };
    if (values.governorateId !== undefined && values.governorateId !== null) {
      payload.governorateId = values.governorateId;
    }
    saveMutation.mutate(payload);
  };

  const backTarget = mode === 'edit' && validEditId ? `/app/lands/${editId}` : '/app/lands';

  if (mode === 'edit' && !validEditId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('lands.detail.notFoundTitle')}
        description={t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/lands">
            <Button type="primary">{t('lands.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (mode === 'edit' && landQuery.isLoading) {
    return (
      <FullPageState
        variant="loading"
        title={t('common.loading')}
        description={t('lands.form.loadingLand')}
      />
    );
  }

  if (mode === 'edit' && landQuery.isError) {
    const status = isApiError(landQuery.error) ? landQuery.error.status : null;
    const isForbidden = status === 403;
    return (
      <FullPageState
        variant="info"
        code={isForbidden ? '403' : '404'}
        title={isForbidden ? t('errors.forbiddenTitle') : t('lands.detail.notFoundTitle')}
        description={isForbidden ? t('lands.detail.forbiddenBody') : t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/lands">
            <Button type="primary">{t('lands.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (mode === 'edit' && land && land.status !== 'Draft' && land.status !== 'Rejected') {
    return (
      <FullPageState
        variant="info"
        title={t('lands.form.editNotAllowedTitle')}
        description={t('lands.form.editNotAllowedBody')}
        action={
          <Link to={`/app/lands/${land.id}`}>
            <Button type="primary">{t('common.back')}</Button>
          </Link>
        }
      />
    );
  }

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Link to={backTarget} className="back-link">
          <ArrowLeft size={16} aria-hidden="true" />
          {t('common.back')}
        </Link>
        <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
          {mode === 'create' ? t('lands.form.createTitle') : t('lands.form.editTitle')}
        </Typography.Title>
      </div>

      <Card title={t('lands.criteriaCard.title')}>
        <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
          {t('lands.criteriaCard.body')}
        </Typography.Paragraph>
        {criterionQuery.data ? (
          <ul style={{ margin: 0, paddingInlineStart: 20 }}>
            {criterionRows.map((row) => (
              <li key={row.key}>
                {row.label}: <strong>{row.requirement}</strong>
              </li>
            ))}
          </ul>
        ) : criterionQuery.isError ? (
          <Alert type="warning" showIcon title={t('lands.criteriaCard.unavailable')} />
        ) : (
          <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>
        )}
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0, marginTop: 12 }}>
          {t('lands.criteriaCard.editableHint')}
        </Typography.Paragraph>
      </Card>

      <Card>
        <div ref={submitErrorRef}>
          {submitError && (
            <Alert
              type="error"
              showIcon
              role="alert"
              title={submitError}
              style={{ marginBottom: 16 }}
            />
          )}
        </div>

        <Form<LandFormValues>
          form={form}
          layout="vertical"
          requiredMark={false}
          onFinish={onFinish}
          disabled={saveMutation.isPending}
        >
          <Row gutter={16}>
            <Col xs={24} md={12}>
              <Form.Item
                name="governorateId"
                label={t('lands.form.governorate')}
                extra={mode === 'edit' ? t('lands.form.governorateEditHint') : undefined}
                rules={
                  mode === 'create'
                    ? [{ required: true, message: t('lands.form.governorateRequired') }]
                    : []
                }
              >
                <Select
                  options={governorateOptions}
                  loading={governoratesQuery.isLoading}
                  placeholder={t('lands.form.governoratePlaceholder')}
                  showSearch
                  optionFilterProp="label"
                  allowClear={mode === 'edit'}
                />
              </Form.Item>
            </Col>

            <Col xs={24} md={12}>
              <Form.Item
                name="landNumber"
                label={t('lands.form.landNumber')}
                rules={[
                  { required: true, message: t('lands.form.required') },
                  { max: 50, message: t('lands.form.maxLength', { max: 50 }) },
                ]}
              >
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="village"
                label={t('lands.form.village')}
                rules={[
                  { required: true, message: t('lands.form.required') },
                  { max: 100, message: t('lands.form.maxLength', { max: 100 }) },
                ]}
              >
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="basin"
                label={t('lands.form.basin')}
                rules={[
                  { required: true, message: t('lands.form.required') },
                  { max: 100, message: t('lands.form.maxLength', { max: 100 }) },
                ]}
              >
                <Input maxLength={100} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="parcelNumber"
                label={t('lands.form.parcelNumber')}
                rules={[
                  { required: true, message: t('lands.form.required') },
                  { max: 100, message: t('lands.form.maxLength', { max: 100 }) },
                ]}
              >
                <Input maxLength={100} />
              </Form.Item>
            </Col>

            <Col xs={24} md={12}>
              <Form.Item
                name="areaDonum"
                label={t('lands.form.areaDonum')}
                rules={[
                  { required: true, message: t('lands.form.required') },
                  {
                    type: 'number' as const,
                    min: 0.0001,
                    max: 99999999.99,
                    message: t('lands.form.range', { min: 0.0001, max: 99999999.99 }),
                  },
                ]}
              >
                <InputNumber min={0.0001} max={99999999.99} step={0.01} style={{ width: '100%' }} />
              </Form.Item>
            </Col>

            <Col xs={24}>
              <Form.Item
                name="description"
                label={t('lands.form.description')}
                rules={[
                  { max: DESCRIPTION_MAX, message: t('lands.form.maxLength', { max: DESCRIPTION_MAX }) },
                ]}
              >
                <Input.TextArea
                  rows={4}
                  maxLength={DESCRIPTION_MAX}
                  showCount
                  placeholder={t('lands.form.descriptionPlaceholder')}
                />
              </Form.Item>
            </Col>
          </Row>

          <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
            {t('lands.form.eligibilityNote')}
          </Typography.Paragraph>

          <Form.Item style={{ marginBottom: 0 }}>
            <Space>
              <Button
                type="primary"
                htmlType="submit"
                loading={saveMutation.isPending}
                icon={<Save size={16} aria-hidden="true" />}
              >
                {saveMutation.isPending
                  ? t('common.saving')
                  : mode === 'create'
                    ? t('lands.form.createSubmit')
                    : t('lands.form.editSubmit')}
              </Button>
              <Link to={backTarget}>
                <Button>{t('common.cancel')}</Button>
              </Link>
            </Space>
          </Form.Item>
        </Form>
      </Card>
    </Space>
  );
}
