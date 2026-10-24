import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Col,
  Descriptions,
  Form,
  Input,
  InputNumber,
  Row,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { MinusCircle, Plus, Send } from 'lucide-react';
import { useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';

import { fetchChangeRequests, submitChangeRequest } from '@/api/lands';
import { fetchDocumentTypes, fetchGovernorates } from '@/api/reference';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import type { LandChangeRequestDto, LandDto, LandFieldsSnapshot } from '@/types/lands';

const REQUEST_STATUS_COLORS: Record<string, string> = {
  Pending: 'gold',
  Approved: 'green',
  Rejected: 'red',
};

const REQUEST_STATUS_KEYS: Record<string, string> = {
  Pending: 'lands.changeRequest.statusPending',
  Approved: 'lands.changeRequest.statusApproved',
  Rejected: 'lands.changeRequest.statusRejected',
};

interface ChangeRequestFormValues {
  reason: string;
  governorateId?: number;
  landNumber: string;
  village: string;
  basin: string;
  parcelNumber: string;
  areaDonum: number;
  description?: string;
  supportingDocuments?: { documentTypeId?: number; filePath?: string }[];
}

const DESCRIPTION_MAX = 1000;

/**
 * Landlord-side change request flow for an already-approved land. The land is hidden
 * from investors for the whole review, so the panel only submits and reads; the
 * decision is a SuperAdmin action (see the admin change-request queue).
 */
export function LandChangeRequestPanel({ land }: { land: LandDto }) {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<ChangeRequestFormValues>();
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const isArabic = locale.startsWith('ar');

  const requestsQuery = useQuery({
    queryKey: ['land', land.id, 'change-requests'],
    queryFn: () => fetchChangeRequests(land.id),
  });
  const governoratesQuery = useQuery({
    queryKey: ['reference', 'governorates'],
    queryFn: fetchGovernorates,
    staleTime: 5 * 60_000,
  });
  const typesQuery = useQuery({
    queryKey: ['reference', 'documentTypes'],
    queryFn: fetchDocumentTypes,
    staleTime: 5 * 60_000,
  });

  const governorateOptions = useMemo(
    () =>
      (governoratesQuery.data ?? []).map((governorate) => ({
        value: governorate.id,
        label: isArabic ? governorate.nameAr : governorate.nameEn,
      })),
    [governoratesQuery.data, isArabic],
  );

  const documentTypeOptions = useMemo(
    () => (typesQuery.data ?? []).map((type) => ({ value: type.id, label: type.name })),
    [typesQuery.data],
  );

  useEffect(() => {
    form.setFieldsValue({
      governorateId: land.governorateId ?? undefined,
      landNumber: land.landNumber,
      village: land.village ?? '',
      basin: land.basin ?? '',
      parcelNumber: land.parcelNumber ?? '',
      areaDonum: land.areaDonum,
      description: land.description ?? '',
      reason: '',
      supportingDocuments: [],
    });
  }, [land, form]);

  const requests = requestsQuery.data ?? [];
  const hasPending = requests.some((request) => request.status === 'Pending');
  const canSubmit = land.status === 'Verified' && requestsQuery.isSuccess && !hasPending;

  const mutation = useMutation({
    mutationFn: (values: ChangeRequestFormValues) => {
      const supportingDocuments = (values.supportingDocuments ?? [])
        .filter((document) => document?.documentTypeId && document.filePath?.trim())
        .map((document) => ({
          documentTypeId: document.documentTypeId as number,
          filePath: (document.filePath as string).trim(),
        }));

      return submitChangeRequest({
        landId: land.id,
        reason: values.reason.trim(),
        governorateId: values.governorateId ?? null,
        landNumber: values.landNumber,
        village: values.village,
        basin: values.basin,
        parcelNumber: values.parcelNumber,
        areaDonum: values.areaDonum,
        description: values.description?.trim() || null,
        supportingDocuments,
      });
    },
    onSuccess: async () => {
      message.success(t('lands.changeRequest.submitted'));
      await queryClient.invalidateQueries({ queryKey: ['land', land.id] });
      await queryClient.invalidateQueries({ queryKey: ['land', land.id, 'change-requests'] });
      await queryClient.invalidateQueries({ queryKey: ['lands'] });
    },
    onError: (error: unknown) => {
      message.error(resolveLandErrorMessage(error, t));
    },
  });

  const fieldRows = (snapshot: LandFieldsSnapshot) => [
    { key: 'landNumber', label: t('lands.form.landNumber'), children: snapshot.landNumber },
    { key: 'village', label: t('lands.form.village'), children: snapshot.village || '—' },
    { key: 'basin', label: t('lands.form.basin'), children: snapshot.basin || '—' },
    { key: 'parcel', label: t('lands.form.parcelNumber'), children: snapshot.parcelNumber || '—' },
    {
      key: 'area',
      label: t('lands.form.areaDonum'),
      children: formatDecimal(snapshot.areaDonum, locale),
    },
    {
      key: 'description',
      label: t('lands.form.description'),
      children: snapshot.description || '—',
    },
  ];

  const columns: TableColumnsType<LandChangeRequestDto> = [
    {
      title: t('lands.changeRequest.requestedAt'),
      dataIndex: 'requestedAt',
      key: 'requestedAt',
      render: (value: string) => formatDateTime(value, locale),
    },
    {
      title: t('lands.changeRequest.status'),
      dataIndex: 'status',
      key: 'status',
      render: (value: string) => (
        <Tag color={REQUEST_STATUS_COLORS[value] ?? 'default'}>
          {t(REQUEST_STATUS_KEYS[value] ?? value, { defaultValue: value })}
        </Tag>
      ),
    },
    { title: t('lands.changeRequest.reason'), dataIndex: 'reason', key: 'reason' },
    {
      title: t('lands.changeRequest.decision'),
      key: 'decision',
      render: (_value, request) =>
        request.decisionReason ? (
          <Space orientation="vertical" size={0}>
            <Typography.Text type="secondary">
              {request.reviewedByName ?? '—'} · {formatDateTime(request.reviewedAt, locale)}
            </Typography.Text>
            <Typography.Text>{request.decisionReason}</Typography.Text>
          </Space>
        ) : (
          '—'
        ),
    },
  ];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      {canSubmit ? (
        <>
          <Alert type="info" showIcon role="status" title={t('lands.changeRequest.intro')} />
          <Form<ChangeRequestFormValues>
            form={form}
            layout="vertical"
            onFinish={(values) => mutation.mutate(values)}
            disabled={mutation.isPending}
          >
            <Form.Item
              name="reason"
              label={t('lands.changeRequest.reason')}
              rules={[
                { required: true, message: t('lands.changeRequest.reasonRequired') },
                { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
              ]}
            >
              <Input.TextArea rows={3} maxLength={500} placeholder={t('lands.changeRequest.reasonPlaceholder')} />
            </Form.Item>

            <Typography.Title level={2} className="nb-section-title">{t('lands.changeRequest.proposedTitle')}</Typography.Title>
            <Row gutter={16}>
              <Col xs={24} md={12}>
                <Form.Item name="governorateId" label={t('lands.form.governorate')}>
                  <Select
                    options={governorateOptions}
                    loading={governoratesQuery.isLoading}
                    allowClear
                    showSearch
                    optionFilterProp="label"
                    placeholder={t('lands.form.governoratePlaceholder')}
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
                  rules={[{ required: true, message: t('lands.form.required') }]}
                >
                  <InputNumber min={0.0001} max={99999999.99} step={0.01} style={{ width: '100%' }} />
                </Form.Item>
              </Col>
              <Col xs={24}>
                <Form.Item
                  name="description"
                  label={t('lands.form.description')}
                  rules={[{ max: DESCRIPTION_MAX, message: t('lands.form.maxLength', { max: DESCRIPTION_MAX }) }]}
                >
                  <Input.TextArea rows={2} maxLength={DESCRIPTION_MAX} />
                </Form.Item>
              </Col>
            </Row>

            <Typography.Title level={2} className="nb-section-title">{t('lands.changeRequest.documentsTitle')}</Typography.Title>
            <Form.List name="supportingDocuments">
              {(fields, { add, remove }) => (
                <Space orientation="vertical" size="small" style={{ width: '100%' }}>
                  {fields.map((field) => (
                    <Row key={field.key} gutter={16} align="middle" wrap>
                      <Col xs={24} md={8}>
                        <Form.Item
                          name={[field.name, 'documentTypeId']}
                          label={t('lands.documents.documentType')}
                          rules={[{ required: true, message: t('lands.documents.typeRequired') }]}
                          style={{ marginBottom: 8 }}
                        >
                          <Select
                            options={documentTypeOptions}
                            loading={typesQuery.isLoading}
                            showSearch
                            optionFilterProp="label"
                            placeholder={t('lands.documents.typePlaceholder')}
                          />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={14}>
                        <Form.Item
                          name={[field.name, 'filePath']}
                          label={t('lands.documents.pathLabel')}
                          rules={[{ required: true, message: t('lands.documents.pathRequired') }]}
                          style={{ marginBottom: 8 }}
                        >
                          <Input maxLength={500} placeholder={t('lands.documents.pathPlaceholder')} />
                        </Form.Item>
                      </Col>
                      <Col xs={24} md={2}>
                        <Button
                          type="text"
                          danger
                          aria-label={t('common.delete')}
                          icon={<MinusCircle size={16} aria-hidden="true" />}
                          onClick={() => remove(field.name)}
                        />
                      </Col>
                    </Row>
                  ))}
                  <Button
                    type="dashed"
                    icon={<Plus size={16} aria-hidden="true" />}
                    onClick={() => add({ documentTypeId: undefined, filePath: '' })}
                  >
                    {t('lands.changeRequest.addDocument')}
                  </Button>
                </Space>
              )}
            </Form.List>

            <Form.Item style={{ marginTop: 16, marginBottom: 0 }}>
              <Button
                type="primary"
                htmlType="submit"
                loading={mutation.isPending}
                icon={<Send size={16} aria-hidden="true" />}
              >
                {t('lands.changeRequest.submit')}
              </Button>
            </Form.Item>
          </Form>
        </>
      ) : (
        <Alert
          type="info"
          showIcon
          role="status"
          title={
            hasPending
              ? t('lands.detail.hiddenFromInvestors')
              : t('lands.changeRequest.intro')
          }
        />
      )}

      <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 0 }}>
        {t('lands.changeRequest.existingTitle')}
      </Typography.Title>
      {requestsQuery.isError ? (
        <Alert
          type="warning"
          showIcon
          role="alert"
          title={resolveLandErrorMessage(requestsQuery.error, t)}
        />
      ) : (
        <Table<LandChangeRequestDto>
          rowKey="id"
          size="small"
          columns={columns}
          dataSource={requests}
          loading={requestsQuery.isLoading}
          pagination={false}
          locale={{ emptyText: t('lands.changeRequest.empty') }}
          expandable={{
            expandedRowRender: (request) => (
              <Row gutter={16}>
                <Col xs={24} md={12}>
                  <Descriptions
                    title={t('lands.changeRequest.originalValues')}
                    column={1}
                    size="small"
                    bordered
                    items={request.originalFields ? fieldRows(request.originalFields) : []}
                  />
                  {/* A stored snapshot the server cannot read is reported as unavailable; no
                      empty or repaired values are ever shown in its place. */}
                  {!request.originalFields && (
                    <Typography.Paragraph type="warning" style={{ marginTop: 8 }}>
                      {t('lands.changeRequest.snapshotUnavailable')}
                    </Typography.Paragraph>
                  )}
                </Col>
                <Col xs={24} md={12}>
                  <Descriptions
                    title={t('lands.changeRequest.proposedValues')}
                    column={1}
                    size="small"
                    bordered
                    items={request.proposedFields ? fieldRows(request.proposedFields) : []}
                  />
                  {!request.proposedFields && (
                    <Typography.Paragraph type="warning" style={{ marginTop: 8 }}>
                      {t('lands.changeRequest.snapshotUnavailable')}
                    </Typography.Paragraph>
                  )}
                </Col>
              </Row>
            ),
          }}
        />
      )}
    </Space>
  );
}
