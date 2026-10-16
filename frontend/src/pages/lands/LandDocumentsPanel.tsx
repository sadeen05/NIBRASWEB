import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App as AntApp, Button, Form, Input, Select, Space, Table, Tag } from 'antd';
import type { TableColumnsType } from 'antd';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { fetchDocuments, uploadDocument } from '@/api/lands';
import { fetchDocumentTypes } from '@/api/reference';
import { formatDateTime } from '@/pages/lands/landFormat';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import type { LandDocumentDto } from '@/types/lands';

interface DocumentFormValues {
  documentTypeId: number;
  filePath: string;
}

const DOC_STATUS_COLORS: Record<string, string> = {
  Pending: 'gold',
  Approved: 'green',
  Rejected: 'red',
};

const DOC_STATUS_KEYS: Record<string, string> = {
  Pending: 'lands.documents.statusPending',
  Approved: 'lands.documents.statusApproved',
  Rejected: 'lands.documents.statusRejected',
};

function DocumentStatusTag({ status }: { status: string }) {
  const { t } = useTranslation();
  const key = DOC_STATUS_KEYS[status];
  return <Tag color={DOC_STATUS_COLORS[status] ?? 'default'}>{key ? t(key) : status}</Tag>;
}

interface LandDocumentsPanelProps {
  landId: number;
  /** Only `Draft` and `Rejected` lands accept new document references. */
  status: string;
}

export function LandDocumentsPanel({ landId, status }: LandDocumentsPanelProps) {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<DocumentFormValues>();
  const [formError, setFormError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const canAdd = status === 'Draft' || status === 'Rejected';

  const documentsQuery = useQuery({
    queryKey: ['land', landId, 'documents'],
    queryFn: () => fetchDocuments(landId),
  });

  const typesQuery = useQuery({
    queryKey: ['reference', 'documentTypes'],
    queryFn: fetchDocumentTypes,
    staleTime: 5 * 60_000,
  });

  const uploadMutation = useMutation({
    mutationFn: (values: DocumentFormValues) => uploadDocument(landId, values),
    onSuccess: async () => {
      form.resetFields();
      setFormError(null);
      message.success(t('lands.documents.added'));
      await queryClient.invalidateQueries({ queryKey: ['land', landId, 'documents'] });
    },
    onError: (error: unknown) => {
      setFormError(resolveLandErrorMessage(error, t));
    },
  });

  const columns = useMemo<TableColumnsType<LandDocumentDto>>(
    () => [
      {
        title: t('lands.documents.documentType'),
        dataIndex: 'documentTypeName',
        key: 'documentTypeName',
      },
      {
        title: t('lands.documents.pathLabel'),
        dataIndex: 'filePath',
        key: 'filePath',
      },
      {
        title: t('lands.documents.version'),
        dataIndex: 'version',
        key: 'version',
      },
      {
        title: t('lands.documents.status'),
        dataIndex: 'status',
        key: 'status',
        render: (value: string) => <DocumentStatusTag status={value} />,
      },
      {
        title: t('lands.documents.uploadedAt'),
        dataIndex: 'uploadedAt',
        key: 'uploadedAt',
        render: (value: string | null) => formatDateTime(value, locale),
      },
    ],
    [t, locale],
  );

  const typeOptions = (typesQuery.data ?? []).map((type) => ({
    value: type.id,
    label: type.isRequiredForVerification
      ? `${type.name} (${t('lands.documents.requiredBadge')})`
      : type.name,
  }));

  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Alert
        type="info"
        showIcon
        title={t('lands.documents.referenceNotice')}
        description={t('lands.documents.noDeleteNotice')}
      />

      {documentsQuery.isError ? (
        <Alert
          type="error"
          showIcon
          role="alert"
          title={resolveLandErrorMessage(documentsQuery.error, t)}
          action={
            <Button size="small" onClick={() => void documentsQuery.refetch()}>
              {t('common.retry')}
            </Button>
          }
        />
      ) : (
        <Table<LandDocumentDto>
          rowKey="id"
          columns={columns}
          dataSource={documentsQuery.data ?? []}
          loading={documentsQuery.isLoading}
          locale={{ emptyText: t('lands.documents.empty') }}
          pagination={false}
          size="small"
        />
      )}

      {canAdd ? (
        <Form<DocumentFormValues>
          form={form}
          layout="vertical"
          onFinish={(values) => uploadMutation.mutate(values)}
          disabled={uploadMutation.isPending}
        >
          {formError && (
            <Alert type="error" showIcon role="alert" title={formError} style={{ marginBottom: 12 }} />
          )}
          <Space align="start" wrap>
            <Form.Item
              name="documentTypeId"
              label={t('lands.documents.documentType')}
              rules={[{ required: true, message: t('lands.documents.typeRequired') }]}
              style={{ minWidth: 240, marginBottom: 12 }}
            >
              <Select
                options={typeOptions}
                loading={typesQuery.isLoading}
                placeholder={t('lands.documents.typePlaceholder')}
              />
            </Form.Item>
            <Form.Item
              name="filePath"
              label={t('lands.documents.pathLabel')}
              rules={[
                { required: true, message: t('lands.documents.pathRequired') },
                { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
              ]}
              style={{ minWidth: 320, marginBottom: 12 }}
            >
              <Input placeholder={t('lands.documents.pathPlaceholder')} />
            </Form.Item>
            <Form.Item label=" " colon={false} style={{ marginBottom: 12 }}>
              <Button
                type="primary"
                htmlType="submit"
                loading={uploadMutation.isPending}
                icon={<Plus size={16} aria-hidden="true" />}
              >
                {uploadMutation.isPending ? t('lands.documents.adding') : t('lands.documents.add')}
              </Button>
            </Form.Item>
          </Space>
        </Form>
      ) : (
        <Alert type="warning" showIcon title={t('lands.documents.frozen')} />
      )}
    </Space>
  );
}
