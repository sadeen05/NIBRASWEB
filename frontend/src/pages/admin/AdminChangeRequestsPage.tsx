import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Col,
  Descriptions,
  Form,
  Input,
  Modal,
  Row,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { Check, Eye, X } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { decideChangeRequest, fetchPendingChangeRequests } from '@/api/lands';
import { resolveApiMessage } from '@/api/knownMessages';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import type { LandChangeRequestDto, LandFieldsSnapshot } from '@/types/lands';

interface DecisionFormValues {
  decisionReason: string;
}

/**
 * SuperAdmin queue of landlord change requests on approved lands. Only a SuperAdmin
 * can decide these; the backend rejects every other role, so this page is a
 * SuperAdmin-only route and every action is re-authorised server-side.
 */
export function AdminChangeRequestsPage() {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<DecisionFormValues>();
  const [selected, setSelected] = useState<LandChangeRequestDto | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const requestsQuery = useQuery({
    queryKey: ['admin-change-requests'],
    queryFn: fetchPendingChangeRequests,
  });

  const refresh = async (landId: number) => {
    await queryClient.invalidateQueries({ queryKey: ['admin-change-requests'] });
    await queryClient.invalidateQueries({ queryKey: ['land', landId] });
    await queryClient.invalidateQueries({ queryKey: ['land', landId, 'change-requests'] });
    await queryClient.invalidateQueries({ queryKey: ['admin-lands'] });
    await queryClient.invalidateQueries({ queryKey: ['lands'] });
  };

  const decisionMutation = useMutation({
    mutationFn: (input: { id: number; landId: number; approved: boolean; decisionReason: string }) =>
      decideChangeRequest(input.id, {
        approved: input.approved,
        decisionReason: input.decisionReason,
      }),
    onSuccess: async (_data, variables) => {
      setSelected(null);
      setActionError(null);
      form.resetFields();
      message.success(
        variables.approved
          ? t('admin.changeRequestApproved')
          : t('admin.changeRequestRejected'),
      );
      await refresh(variables.landId);
    },
    onError: (error: unknown) => setActionError(resolveApiMessage(error, t)),
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

  const decide = async (approved: boolean) => {
    if (!selected) return;
    try {
      const values = await form.validateFields();
      setActionError(null);
      decisionMutation.mutate({
        id: selected.id,
        landId: selected.landId,
        approved,
        decisionReason: values.decisionReason.trim(),
      });
    } catch {
      // Validation errors are shown inline by the form.
    }
  };

  const columns = useMemo<TableColumnsType<LandChangeRequestDto>>(
    () => [
      {
        title: t('lands.list.landNumber'),
        dataIndex: 'landNumber',
        key: 'landNumber',
        render: (value: string, request) => (
          <Link to={`/app/admin/lands/${request.landId}`}>{value}</Link>
        ),
      },
      {
        title: t('lands.changeRequest.requestedAt'),
        dataIndex: 'requestedAt',
        key: 'requestedAt',
        render: (value: string) => formatDateTime(value, locale),
      },
      {
        title: t('lands.changeRequest.requestedBy'),
        dataIndex: 'requestedByName',
        key: 'requestedByName',
      },
      { title: t('admin.changeRequestReason'), dataIndex: 'reason', key: 'reason' },
      {
        title: t('lands.list.actions'),
        key: 'actions',
        render: (_value, request) => (
          <Button
            size="small"
            icon={<Eye size={14} aria-hidden="true" />}
            onClick={() => {
              setActionError(null);
              form.resetFields();
              setSelected(request);
            }}
          >
            {t('admin.changeRequestReview')}
          </Button>
        ),
      },
    ],
    [t, locale, form],
  );

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Typography.Title level={1} style={{ margin: 0 }}>
          {t('admin.changeRequestQueueTitle')}
        </Typography.Title>
        <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
          {t('admin.changeRequestQueueSubtitle')}
        </Typography.Paragraph>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        {requestsQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveApiMessage(requestsQuery.error, t)}
            action={
              <Button size="small" onClick={() => void requestsQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        ) : (
          <Table<LandChangeRequestDto>
            rowKey="id"
            columns={columns}
            dataSource={requestsQuery.data ?? []}
            loading={requestsQuery.isLoading}
            locale={{ emptyText: t('admin.changeRequestQueueEmpty') }}
            pagination={false}
          />
        )}
      </Card>

      <Modal
        open={selected !== null}
        title={t('admin.changeRequestDecisionTitle')}
        onCancel={() => setSelected(null)}
        destroyOnHidden
        footer={
          <Space>
            <Button onClick={() => setSelected(null)}>{t('common.cancel')}</Button>
            <Button
              danger
              icon={<X size={16} aria-hidden="true" />}
              loading={decisionMutation.isPending}
              onClick={() => void decide(false)}
            >
              {t('admin.changeRequestReject')}
            </Button>
            <Button
              type="primary"
              icon={<Check size={16} aria-hidden="true" />}
              loading={decisionMutation.isPending}
              onClick={() => void decide(true)}
            >
              {t('admin.changeRequestApprove')}
            </Button>
          </Space>
        }
      >
        {selected && (
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            <Alert type="warning" showIcon role="status" title={t('admin.changeRequestHiddenNotice')} />

            <Descriptions column={1} size="small" bordered>
              <Descriptions.Item label={t('lands.changeRequest.requestedBy')}>
                {selected.requestedByName} · {formatDateTime(selected.requestedAt, locale)}
              </Descriptions.Item>
              <Descriptions.Item label={t('admin.changeRequestReason')}>
                {selected.reason}
              </Descriptions.Item>
            </Descriptions>

            <Row gutter={16}>
              <Col xs={24} md={12}>
                <Descriptions
                  title={t('admin.changeRequestOriginal')}
                  column={1}
                  size="small"
                  bordered
                  items={selected.originalFields ? fieldRows(selected.originalFields) : []}
                />
                {/* Unreadable stored JSON is reported as unavailable, never as an empty or
                    repaired snapshot, so a decision is never taken on invented values. */}
                {!selected.originalFields && (
                  <Typography.Paragraph type="warning" style={{ marginTop: 8 }}>
                    {t('admin.changeRequestSnapshotUnreadable')}
                  </Typography.Paragraph>
                )}
              </Col>
              <Col xs={24} md={12}>
                <Descriptions
                  title={t('admin.changeRequestProposed')}
                  column={1}
                  size="small"
                  bordered
                  items={selected.proposedFields ? fieldRows(selected.proposedFields) : []}
                />
                {!selected.proposedFields && (
                  <Typography.Paragraph type="warning" style={{ marginTop: 8 }}>
                    {t('admin.changeRequestSnapshotUnreadable')}
                  </Typography.Paragraph>
                )}
              </Col>
            </Row>

            <div>
              <Typography.Text strong>
                {t('admin.changeRequestSupportingDocuments')}
              </Typography.Text>
              {selected.supportingDocuments === null ? (
                <Typography.Paragraph type="warning" style={{ marginBottom: 0 }}>
                  {t('admin.changeRequestDocumentsUnavailable')}
                </Typography.Paragraph>
              ) : selected.supportingDocuments.length === 0 ? (
                <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
                  —
                </Typography.Paragraph>
              ) : (
                <ul style={{ margin: '4px 0 0', paddingInlineStart: 20 }}>
                  {selected.supportingDocuments.map((document, index) => (
                    <li key={`${document.documentTypeId}-${index}`}>
                      <Tag>#{document.documentTypeId}</Tag>
                      {document.filePath}
                    </li>
                  ))}
                </ul>
              )}
            </div>

            {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}

            <Form<DecisionFormValues> form={form} layout="vertical">
              <Form.Item
                name="decisionReason"
                label={t('admin.changeRequestDecisionNote')}
                rules={[
                  { required: true, message: t('admin.changeRequestDecisionNoteRequired') },
                  { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
                ]}
              >
                <Input.TextArea rows={3} maxLength={500} />
              </Form.Item>
            </Form>
          </Space>
        )}
      </Modal>
    </Space>
  );
}
