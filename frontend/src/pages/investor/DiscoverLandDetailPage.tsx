import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd';
import { ArrowLeft, HandCoins } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { fetchEligibility, fetchLand } from '@/api/lands';
import { resolveApiMessage } from '@/api/knownMessages';
import { createOffer } from '@/api/offers';
import { FullPageState } from '@/components/FullPageState';
import { LandStatusTag } from '@/components/LandStatusTag';
import { formatDecimal } from '@/pages/lands/landFormat';
import { CONNECTION_MECHANISMS } from '@/types/offers';

const RESULT_COLORS: Record<string, string> = {
  Passed: 'green',
  Failed: 'red',
  NotEnforced: 'default',
};

interface OfferFormValues {
  requiredCapacityMw: number;
  connectionMechanism: number;
  landlordSharePct: number;
  durationYears?: number | null;
  startDate?: string | null;
  installationCost?: number | null;
  solarCellCapacityKw?: number | null;
  message?: string | null;
}

export function DiscoverLandDetailPage() {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const { id: idParam } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<OfferFormValues>();
  const [offerOpen, setOfferOpen] = useState(false);
  const [offerError, setOfferError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0;

  const landQuery = useQuery({
    queryKey: ['land', id],
    queryFn: () => fetchLand(id),
    enabled: validId,
  });

  const eligibilityQuery = useQuery({
    queryKey: ['land', id, 'eligibility'],
    queryFn: () => fetchEligibility(id),
    enabled: validId,
  });

  const createMutation = useMutation({
    mutationFn: (values: OfferFormValues) =>
      createOffer({
        landId: id,
        requiredCapacityMw: values.requiredCapacityMw,
        connectionMechanism: values.connectionMechanism,
        landlordSharePct: values.landlordSharePct,
        durationYears: values.durationYears ?? null,
        startDate: values.startDate || null,
        installationCost: values.installationCost ?? null,
        solarCellCapacityKw: values.solarCellCapacityKw ?? null,
        message: values.message?.trim() || null,
      }),
    onSuccess: async (offer) => {
      setOfferOpen(false);
      form.resetFields();
      setOfferError(null);
      message.success(t('offers.created'));
      await queryClient.invalidateQueries({ queryKey: ['my-offers'] });
      navigate(`/app/offers/${offer.id}`);
    },
    onError: (error: unknown) => {
      setOfferError(resolveApiMessage(error, t));
    },
  });

  if (!validId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('lands.detail.notFoundTitle')}
        description={t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/discover">
            <Button type="primary">{t('discover.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (landQuery.isLoading) {
    return (
      <FullPageState variant="loading" title={t('common.loading')} description={t('lands.detail.loading')} />
    );
  }

  if (landQuery.isError) {
    const status = isApiError(landQuery.error) ? landQuery.error.status : null;
    const isForbidden = status === 403;
    return (
      <FullPageState
        variant="info"
        code={isForbidden ? '403' : '404'}
        title={isForbidden ? t('errors.forbiddenTitle') : t('lands.detail.notFoundTitle')}
        description={isForbidden ? t('discover.notApprovedBody') : t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/discover">
            <Button type="primary">{t('discover.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  const land = landQuery.data;
  if (!land) {
    return null;
  }

  const criterionLabel = (criterion: string) => {
    const key = `lands.criteria.${criterion}`;
    const translated = t(key);
    return translated === key ? criterion : translated;
  };

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to="/app/discover" className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('discover.backToList')}
          </Link>
          <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
            {land.landNumber} <LandStatusTag status={land.status} />
          </Typography.Title>
          <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
            {land.governorateName ?? land.regionName ?? '—'}
          </Typography.Paragraph>
        </div>
        <Button
          type="primary"
          icon={<HandCoins size={16} aria-hidden="true" />}
          onClick={() => {
            setOfferError(null);
            setOfferOpen(true);
          }}
        >
          {t('offers.makeOffer')}
        </Button>
      </div>

      <Card title={t('lands.detail.sectionDetails')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'governorate', label: t('lands.form.governorate'), children: land.governorateName ?? '—' },
            { key: 'region', label: t('lands.form.region'), children: land.regionName ?? '—' },
            { key: 'village', label: t('lands.form.village'), children: land.village ?? '—' },
            { key: 'basin', label: t('lands.form.basin'), children: land.basin ?? '—' },
            { key: 'parcel', label: t('lands.form.parcelNumber'), children: land.parcelNumber ?? '-' },
            { key: 'description', label: t('lands.form.description'), children: land.description ?? '-' },
            {
              key: 'area',
              label: t('lands.form.areaDonum'),
              children: formatDecimal(land.areaDonum, locale),
            },
            {
              key: 'slope',
              label: t('lands.form.slopePercentage'),
              children: formatDecimal(land.slopePercentage, locale),
            },
            {
              key: 'distance',
              label: t('lands.form.distanceToGridKm'),
              children: formatDecimal(land.distanceToGridKm, locale),
            },
            {
              key: 'solar',
              label: t('lands.form.solarIrradiance'),
              children: formatDecimal(land.solarIrradiance, locale),
            },
            {
              key: 'elevation',
              label: t('lands.form.elevationM'),
              children: formatDecimal(land.elevationM, locale),
            },
          ]}
        />
      </Card>

      <Card title={t('lands.detail.sectionEligibility')}>
        {eligibilityQuery.isLoading ? (
          <Spin />
        ) : eligibilityQuery.isError ? (
          <Alert type="error" showIcon role="alert" title={resolveApiMessage(eligibilityQuery.error, t)} />
        ) : (eligibilityQuery.data?.results.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('lands.detail.eligibilityEmpty')}</Typography.Text>
        ) : (
          <Descriptions
            column={1}
            size="small"
            bordered
            items={(eligibilityQuery.data?.results ?? []).map((result) => ({
              key: result.criterion,
              label: criterionLabel(result.criterion),
              children: (
                <Space wrap>
                  <Tag color={RESULT_COLORS[result.result] ?? 'default'}>
                    {t(`lands.criteriaResult.${result.result}`, { defaultValue: result.result })}
                  </Tag>
                  <span>
                    {t('lands.detail.required')}: {result.required ?? '—'} ·{' '}
                    {t('lands.detail.actual')}: {formatDecimal(result.actual, locale)}
                  </span>
                </Space>
              ),
            }))}
          />
        )}
      </Card>

      <Modal
        open={offerOpen}
        title={t('offers.makeOffer')}
        okText={t('offers.submitOffer')}
        cancelText={t('common.cancel')}
        confirmLoading={createMutation.isPending}
        onCancel={() => setOfferOpen(false)}
        onOk={() => form.submit()}
        destroyOnHidden
      >
        {offerError && (
          <Alert type="error" showIcon role="alert" title={offerError} style={{ marginBottom: 12 }} />
        )}
        <Form<OfferFormValues>
          form={form}
          layout="vertical"
          onFinish={(values) => createMutation.mutate(values)}
          initialValues={{ connectionMechanism: 1 }}
        >
          <Form.Item
            name="requiredCapacityMw"
            label={t('offers.form.requiredCapacityMw')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { type: 'number', min: 0.01, max: 99999999.99, message: t('offers.form.capacityRange') },
            ]}
          >
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="connectionMechanism"
            label={t('offers.form.connectionMechanism')}
            rules={[{ required: true, message: t('lands.form.required') }]}
          >
            <Select
              options={CONNECTION_MECHANISMS.map((entry) => ({
                value: entry.value,
                label: t(`offers.mechanism.${entry.name}`),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="landlordSharePct"
            label={t('offers.form.landlordSharePct')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { type: 'number', min: 0.01, max: 999.99, message: t('offers.form.shareRange') },
            ]}
          >
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="durationYears"
            label={t('offers.form.durationYears')}
            rules={[{ type: 'number', min: 1, max: 100, message: t('offers.form.durationRange') }]}
          >
            <InputNumber min={1} max={100} step={1} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="startDate" label={t('offers.form.startDate')}>
            <Input type="date" />
          </Form.Item>
          <Form.Item name="installationCost" label={t('offers.form.installationCost')}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="solarCellCapacityKw" label={t('offers.form.solarCellCapacityKw')}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="message"
            label={t('offers.form.message')}
            rules={[{ max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
