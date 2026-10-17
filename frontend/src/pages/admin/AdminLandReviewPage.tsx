import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  Modal,
  Space,
  Spin,
  Table,
  Tag,
  Timeline,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { ArrowLeft, Check, HandCoins, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import {
  fetchDocuments,
  fetchEligibility,
  fetchHistory,
  fetchLand,
  reviewDocument,
  rejectLand,
  verifyLand,
} from '@/api/lands';
import { FullPageState } from '@/components/FullPageState';
import { LandStatusTag } from '@/components/LandStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import type { LandDocumentDto } from '@/types/lands';

const RESULT_COLORS: Record<string, string> = {
  Passed: 'green',
  Failed: 'red',
  NotEnforced: 'default',
};

const DOC_STATUS_COLORS: Record<string, string> = {
  Pending: 'gold',
  Approved: 'green',
  Rejected: 'red',
};

interface ReasonFormValues {
  reason: string;
}

interface NoteFormValues {
  note: string;
}

export function AdminLandReviewPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { id: idParam } = useParams();
  const queryClient = useQueryClient();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [noteForm] = Form.useForm<NoteFormValues>();
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectDocId, setRejectDocId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0;

  const landQuery = useQuery({
    queryKey: ['land', id],
    queryFn: () => fetchLand(id),
    enabled: validId,
  });
  const documentsQuery = useQuery({
    queryKey: ['land', id, 'documents'],
    queryFn: () => fetchDocuments(id),
    enabled: validId,
  });
  const historyQuery = useQuery({
    queryKey: ['land', id, 'history'],
    queryFn: () => fetchHistory(id),
    enabled: validId,
  });
  const eligibilityQuery = useQuery({
    queryKey: ['land', id, 'eligibility'],
    queryFn: () => fetchEligibility(id),
    enabled: validId,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['land', id] });
    await queryClient.invalidateQueries({ queryKey: ['admin-lands'] });
  };

  const onActionError = (error: unknown) => setActionError(resolveApiMessage(error, t));

  const verifyMutation = useMutation({
    mutationFn: () => verifyLand(id),
    onSuccess: async () => {
      setActionError(null);
      message.success(t('admin.landVerified'));
      await refresh();
    },
    onError: onActionError,
  });

  const rejectMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => rejectLand(id, values.reason.trim()),
    onSuccess: async () => {
      setRejectOpen(false);
      rejectForm.resetFields();
      setActionError(null);
      message.success(t('admin.landRejected'));
      await refresh();
    },
    onError: onActionError,
  });

  const reviewMutation = useMutation({
    mutationFn: (input: { documentId: number; approved: boolean; note?: string | null }) =>
      reviewDocument(id, input.documentId, { approved: input.approved, note: input.note ?? null }),
    onSuccess: async () => {
      setRejectDocId(null);
      noteForm.resetFields();
      setActionError(null);
      message.success(t('admin.documentReviewed'));
      await queryClient.invalidateQueries({ queryKey: ['land', id, 'documents'] });
    },
    onError: onActionError,
  });

  if (!validId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('lands.detail.notFoundTitle')}
        description={t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/admin/lands">
            <Button type="primary">{t('admin.backToQueue')}</Button>
          </Link>
        }
      />
    );
  }

  if (landQuery.isLoading) {
    return <FullPageState variant="loading" title={t('common.loading')} description={t('lands.detail.loading')} />;
  }

  if (landQuery.isError) {
    const status = isApiError(landQuery.error) ? landQuery.error.status : null;
    return (
      <FullPageState
        variant="info"
        code={status === 403 ? '403' : '404'}
        title={status === 403 ? t('errors.forbiddenTitle') : t('lands.detail.notFoundTitle')}
        description={status === 403 ? t('errors.forbiddenBody') : t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/admin/lands">
            <Button type="primary">{t('admin.backToQueue')}</Button>
          </Link>
        }
      />
    );
  }

  const land = landQuery.data;
  if (!land) {
    return null;
  }

  const canDecide = land.status === 'PendingVerification';
  const documentsReviewable = land.status !== 'Verified' && land.status !== 'UnderContract';

  const documentColumns: TableColumnsType<LandDocumentDto> = [
    { title: t('lands.documents.documentType'), dataIndex: 'documentTypeName', key: 'documentTypeName' },
    { title: t('lands.documents.pathLabel'), dataIndex: 'filePath', key: 'filePath' },
    { title: t('lands.documents.version'), dataIndex: 'version', key: 'version' },
    {
      title: t('lands.documents.status'),
      dataIndex: 'status',
      key: 'status',
      render: (value: string) => <Tag color={DOC_STATUS_COLORS[value] ?? 'default'}>{value}</Tag>,
    },
    {
      title: t('lands.documents.uploadedAt'),
      dataIndex: 'uploadedAt',
      key: 'uploadedAt',
      render: (value: string | null) => formatDateTime(value, locale),
    },
    {
      title: t('lands.list.actions'),
      key: 'actions',
      render: (_value, document) =>
        documentsReviewable ? (
          <Space>
            <Button
              size="small"
              type="primary"
              icon={<Check size={14} aria-hidden="true" />}
              loading={reviewMutation.isPending}
              onClick={() => reviewMutation.mutate({ documentId: document.id, approved: true })}
            >
              {t('admin.approveDocument')}
            </Button>
            <Button
              size="small"
              danger
              icon={<X size={14} aria-hidden="true" />}
              onClick={() => {
                setActionError(null);
                setRejectDocId(document.id);
              }}
            >
              {t('admin.rejectDocument')}
            </Button>
          </Space>
        ) : (
          '—'
        ),
    },
  ];

  const confirmVerify = () =>
    modal.confirm({
      title: t('admin.confirmVerifyTitle'),
      content: t('admin.confirmVerifyBody'),
      okText: t('admin.verify'),
      cancelText: t('common.cancel'),
      onOk: () => verifyMutation.mutateAsync(),
    });

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to="/app/admin/lands" className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('admin.backToQueue')}
          </Link>
          <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
            {land.landNumber} <LandStatusTag status={land.status} />
          </Typography.Title>
          <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
            {land.regionName}
          </Typography.Paragraph>
        </div>
        <Space wrap>
          <Link to={`/app/lands/${land.id}/offers`}>
            <Button icon={<HandCoins size={16} aria-hidden="true" />}>{t('offers.landOffersTitle')}</Button>
          </Link>
          {canDecide && (
            <Button type="primary" icon={<Check size={16} aria-hidden="true" />} loading={verifyMutation.isPending} onClick={confirmVerify}>
              {t('admin.verify')}
            </Button>
          )}
          {canDecide && (
            <Button danger icon={<X size={16} aria-hidden="true" />} onClick={() => { setActionError(null); setRejectOpen(true); }}>
              {t('admin.reject')}
            </Button>
          )}
        </Space>
      </div>

      {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}

      <Card title={t('lands.detail.sectionDetails')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'region', label: t('lands.form.region'), children: land.regionName },
            { key: 'grid', label: t('lands.form.grid'), children: land.gridId },
            { key: 'village', label: t('lands.form.village'), children: land.village ?? '—' },
            { key: 'basin', label: t('lands.form.basin'), children: land.basin ?? '—' },
            { key: 'parcel', label: t('lands.form.parcelNumber'), children: land.parcelNumber ?? '—' },
            { key: 'area', label: t('lands.form.areaDonum'), children: formatDecimal(land.areaDonum, locale) },
            { key: 'slope', label: t('lands.form.slopePercentage'), children: formatDecimal(land.slopePercentage, locale) },
            { key: 'distance', label: t('lands.form.distanceToGridKm'), children: formatDecimal(land.distanceToGridKm, locale) },
            { key: 'solar', label: t('lands.form.solarIrradiance'), children: formatDecimal(land.solarIrradiance, locale) },
            { key: 'elevation', label: t('lands.form.elevationM'), children: formatDecimal(land.elevationM, locale) },
          ]}
        />
      </Card>

      <Card title={t('lands.detail.sectionEligibility')}>
        {eligibilityQuery.isLoading ? (
          <Spin />
        ) : eligibilityQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(eligibilityQuery.error, t)} />
        ) : (eligibilityQuery.data?.results.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('lands.detail.eligibilityEmpty')}</Typography.Text>
        ) : (
          <Descriptions
            column={1}
            size="small"
            bordered
            items={(eligibilityQuery.data?.results ?? []).map((result) => ({
              key: result.criterion,
              label: t(`lands.criteria.${result.criterion}`, { defaultValue: result.criterion }),
              children: (
                <Space wrap>
                  <Tag color={RESULT_COLORS[result.result] ?? 'default'}>
                    {t(`lands.criteriaResult.${result.result}`, { defaultValue: result.result })}
                  </Tag>
                  <span>
                    {t('lands.detail.required')}: {result.required ?? '—'} · {t('lands.detail.actual')}:{' '}
                    {formatDecimal(result.actual, locale)}
                  </span>
                </Space>
              ),
            }))}
          />
        )}
      </Card>

      <Card title={t('lands.detail.sectionDocuments')}>
        {documentsQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(documentsQuery.error, t)} />
        ) : (
          <Table<LandDocumentDto>
            rowKey="id"
            columns={documentColumns}
            dataSource={documentsQuery.data ?? []}
            loading={documentsQuery.isLoading}
            locale={{ emptyText: t('lands.documents.empty') }}
            pagination={false}
            size="small"
          />
        )}
      </Card>

      <Card title={t('lands.detail.sectionHistory')}>
        {historyQuery.isLoading ? (
          <Spin />
        ) : historyQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(historyQuery.error, t)} />
        ) : (historyQuery.data?.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('lands.detail.historyEmpty')}</Typography.Text>
        ) : (
          <Timeline
            items={[...(historyQuery.data ?? [])]
              .sort((a, b) => Date.parse(a.changedAt) - Date.parse(b.changedAt))
              .map((entry) => ({
                key: entry.id,
                color: entry.statusName === 'Rejected' ? 'red' : 'green',
                title: (
                  <Space wrap>
                    <LandStatusTag status={entry.statusName} />
                    <Typography.Text type="secondary">{formatDateTime(entry.changedAt, locale)}</Typography.Text>
                  </Space>
                ),
                content: (
                  <div>
                    <div>{entry.changeByName}</div>
                    {entry.reason && <Typography.Text type="secondary">{entry.reason}</Typography.Text>}
                  </div>
                ),
              }))}
          />
        )}
      </Card>

      <Modal
        open={rejectOpen}
        title={t('admin.reject')}
        okText={t('admin.reject')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={rejectMutation.isPending}
        onCancel={() => setRejectOpen(false)}
        onOk={() => rejectForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues> form={rejectForm} layout="vertical" onFinish={(values) => rejectMutation.mutate(values)}>
          <Form.Item
            name="reason"
            label={t('common.reason')}
            rules={[{ required: true, message: t('common.reasonRequired') }, { max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={rejectDocId !== null}
        title={t('admin.rejectDocument')}
        okText={t('admin.rejectDocument')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={reviewMutation.isPending}
        onCancel={() => setRejectDocId(null)}
        onOk={() => noteForm.submit()}
        destroyOnHidden
      >
        <Form<NoteFormValues>
          form={noteForm}
          layout="vertical"
          onFinish={(values) =>
            reviewMutation.mutate({ documentId: rejectDocId ?? 0, approved: false, note: values.note.trim() })
          }
        >
          <Form.Item
            name="note"
            label={t('admin.reviewNote')}
            rules={[{ required: true, message: t('admin.noteRequired') }, { max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
