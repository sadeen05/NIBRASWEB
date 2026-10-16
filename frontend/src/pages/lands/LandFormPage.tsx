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
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { createLand, fetchLand, updateLand } from '@/api/lands';
import { fetchGrids, fetchRegions } from '@/api/reference';
import { FullPageState } from '@/components/FullPageState';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import type { LandWriteRequest } from '@/types/lands';

interface LandFormValues {
  regionId: number;
  gridId: number;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  slopePercentage: number;
  distanceToGridKm: number;
  solarIrradiance: number;
  elevationM: number;
}

interface LandFormPageProps {
  mode: 'create' | 'edit';
}

export function LandFormPage({ mode }: LandFormPageProps) {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const { id: idParam } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<LandFormValues>();
  const [submitError, setSubmitError] = useState<string | null>(null);

  const isArabic = (i18n.resolvedLanguage ?? i18n.language ?? 'en').startsWith('ar');
  const editId = mode === 'edit' ? Number(idParam) : null;
  const validEditId = editId !== null && Number.isInteger(editId) && editId > 0;

  const regionsQuery = useQuery({
    queryKey: ['reference', 'regions'],
    queryFn: fetchRegions,
    staleTime: 5 * 60_000,
  });

  const gridsQuery = useQuery({
    queryKey: ['reference', 'grids'],
    queryFn: fetchGrids,
    staleTime: 5 * 60_000,
  });

  const landQuery = useQuery({
    queryKey: ['land', editId],
    queryFn: () => fetchLand(editId as number),
    enabled: mode === 'edit' && validEditId,
  });

  const land = landQuery.data;

  useEffect(() => {
    if (mode === 'edit' && land) {
      form.setFieldsValue({
        regionId: land.regionId,
        gridId: land.gridId,
        landNumber: land.landNumber,
        village: land.village ?? '',
        basin: land.basin ?? '',
        parcelNumber: land.parcelNumber ?? '',
        areaDonum: land.areaDonum,
        slopePercentage: land.slopePercentage,
        distanceToGridKm: land.distanceToGridKm,
        solarIrradiance: land.solarIrradiance,
        elevationM: land.elevationM,
      });
    }
  }, [mode, land, form]);

  const selectedRegionId = Form.useWatch('regionId', form);

  const regionOptions = useMemo(
    () =>
      (regionsQuery.data ?? []).map((region) => ({
        value: region.id,
        label: isArabic ? region.nameAr : region.nameEn,
      })),
    [regionsQuery.data, isArabic],
  );

  const gridOptions = useMemo(
    () =>
      (gridsQuery.data ?? [])
        .filter((grid) => grid.regionId === selectedRegionId)
        .map((grid) => ({ value: grid.id, label: grid.name })),
    [gridsQuery.data, selectedRegionId],
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
      setSubmitError(resolveLandErrorMessage(error, t));
    },
  });

  const onFinish = (values: LandFormValues) => {
    const payload: LandWriteRequest = {
      regionId: values.regionId,
      gridId: values.gridId,
      landNumber: values.landNumber.trim(),
      village: values.village.trim(),
      basin: values.basin.trim(),
      parcelNumber: values.parcelNumber.trim(),
      areaDonum: values.areaDonum,
      slopePercentage: values.slopePercentage,
      distanceToGridKm: values.distanceToGridKm,
      solarIrradiance: values.solarIrradiance,
      elevationM: values.elevationM,
    };
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

  const numberRule = (min: number, max: number) => [
    { required: true, message: t('lands.form.required') },
    { type: 'number' as const, min, max, message: t('lands.form.range', { min, max }) },
  ];

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

      <Card>
        {submitError && (
          <Alert type="error" showIcon role="alert" title={submitError} style={{ marginBottom: 16 }} />
        )}

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
                name="regionId"
                label={t('lands.form.region')}
                rules={[{ required: true, message: t('lands.form.regionRequired') }]}
              >
                <Select
                  options={regionOptions}
                  loading={regionsQuery.isLoading}
                  placeholder={t('lands.form.regionPlaceholder')}
                  showSearch
                  optionFilterProp="label"
                  onChange={() => form.setFieldValue('gridId', undefined)}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="gridId"
                label={t('lands.form.grid')}
                rules={[{ required: true, message: t('lands.form.gridRequired') }]}
                extra={t('lands.form.gridHint')}
              >
                <Select
                  options={gridOptions}
                  loading={gridsQuery.isLoading}
                  placeholder={
                    selectedRegionId ? t('lands.form.gridPlaceholder') : t('lands.form.selectRegionFirst')
                  }
                  disabled={!selectedRegionId}
                  showSearch
                  optionFilterProp="label"
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
                rules={numberRule(0.0001, 99999999.99)}
              >
                <InputNumber min={0.0001} max={99999999.99} step={0.01} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="slopePercentage"
                label={t('lands.form.slopePercentage')}
                rules={numberRule(0, 100)}
              >
                <InputNumber min={0} max={100} step={0.01} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="distanceToGridKm"
                label={t('lands.form.distanceToGridKm')}
                rules={numberRule(0, 1000000)}
              >
                <InputNumber min={0} max={1000000} step={0.01} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="solarIrradiance"
                label={t('lands.form.solarIrradiance')}
                rules={numberRule(0, 12)}
              >
                <InputNumber min={0} max={12} step={0.01} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item
                name="elevationM"
                label={t('lands.form.elevationM')}
                rules={numberRule(-1000, 10000)}
              >
                <InputNumber min={-1000} max={10000} step={1} style={{ width: '100%' }} />
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
