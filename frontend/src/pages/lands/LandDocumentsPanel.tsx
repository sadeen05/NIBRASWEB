import { useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Form,
  Select,
  Space,
  Table,
  Tag,
  Typography,
  Upload,
} from 'antd';
import type { TableColumnsType } from 'antd';
import type { UploadFile } from 'antd/es/upload/interface';
import { Download, FileUp, Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';

import { downloadDocument, uploadDocument } from '@/api/lands';
import { formatDateTime, formatFileSize } from '@/pages/lands/landFormat';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import { useLandEvidence } from '@/pages/lands/useLandEvidence';
import type { LandDocumentDto } from '@/types/lands';
import { saveBlob } from '@/utils/saveBlob';

interface DocumentFormValues {
  documentTypeId: number;
  file: UploadFile[];
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

const ACCEPTED_DOCUMENT_TYPES =
  '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png';

/**
 * A document that was reviewed and then invalidated (the land data changed afterwards)
 * is Pending *with* a review timestamp. Approving/rejecting always writes the final
 * status in the same request, so this combination only means "the review no longer
 * covers the current data and must be repeated".
 */
function needsReReview(document: LandDocumentDto): boolean {
  return document.status === 'Pending' && document.reviewedAt !== null;
}

function DocumentStatusTag({ status }: { status: string }) {
  const { t } = useTranslation();
  const key = DOC_STATUS_KEYS[status];
  return <Tag color={DOC_STATUS_COLORS[status] ?? 'default'}>{key ? t(key) : status}</Tag>;
}

interface LandDocumentsPanelProps {
  landId: number;
  /** Only `Draft` and `Rejected` lands accept new document files. */
  status: string;
}

export function LandDocumentsPanel({ landId, status }: LandDocumentsPanelProps) {
  const { t, i18n } = useTranslation();
  const { message } = AntApp.useApp();
  const queryClient = useQueryClient();
  const [form] = Form.useForm<DocumentFormValues>();
  const [formError, setFormError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const {
    documents,
    outstandingTypeIds,
    outstandingEvidence,
    documentsQuery,
    typesQuery,
  } = useLandEvidence(landId);

  const isApprovedLand = status === 'Verified' || status === 'UnderContract';
  // Evidence may be added while the land is Draft or Rejected, or as the owner's correction of
  // outstanding evidence on an approved land (the server allows exactly these and re-checks the
  // owner, the state and the corrected type on every upload).
  const canAdd =
    status === 'Draft' || status === 'Rejected' || (isApprovedLand && outstandingEvidence);

  const uploadMutation = useMutation({
    mutationFn: (values: { documentTypeId: number; file: File }) =>
      uploadDocument(landId, values),
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

  const downloadMutation = useMutation({
    mutationFn: (document: LandDocumentDto) => downloadDocument(landId, document.id),
    onSuccess: (blob, document) => {
      saveBlob(blob, document.originalFileName ?? 'document');
    },
    onError: (error: unknown) => {
      void message.error(
        resolveLandErrorMessage(error, t) || t('lands.documents.downloadFailed'),
      );
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
        title: t('lands.documents.fileName'),
        key: 'fileName',
        render: (_value, document) =>
          document.originalFileName ?? (
            <Typography.Text type="secondary">{t('lands.documents.legacyRow')}</Typography.Text>
          ),
      },
      {
        title: t('lands.documents.fileType'),
        key: 'contentType',
        render: (_value, document) => document.contentType ?? '—',
      },
      {
        title: t('lands.documents.fileSize'),
        key: 'fileSize',
        render: (_value, document) => formatFileSize(document.fileSizeBytes),
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
        render: (value: string, document) => (
          <Space size="small" wrap>
            <DocumentStatusTag status={value} />
            {needsReReview(document) && (
              <Tag color="orange">{t('lands.documents.reReviewTag')}</Tag>
            )}
          </Space>
        ),
      },
      {
        title: t('lands.documents.uploadedAt'),
        dataIndex: 'uploadedAt',
        key: 'uploadedAt',
        render: (value: string | null) => formatDateTime(value, locale),
      },
      {
        title: t('lands.documents.lastReview'),
        key: 'lastReview',
        render: (_value, document) =>
          document.reviewedAt ? (
            <Space orientation="vertical" size={0}>
              <Typography.Text type="secondary">
                {formatDateTime(document.reviewedAt, locale)}
              </Typography.Text>
              {document.reviewNote && <Typography.Text>{document.reviewNote}</Typography.Text>}
            </Space>
          ) : (
            '—'
          ),
      },
      {
        title: t('lands.documents.download'),
        key: 'download',
        render: (_value, document) =>
          document.hasFile ? (
            <Button
              size="small"
              icon={<Download size={15} aria-hidden="true" />}
              loading={downloadMutation.isPending && downloadMutation.variables?.id === document.id}
              onClick={() => downloadMutation.mutate(document)}
            >
              {t('lands.documents.download')}
            </Button>
          ) : (
            <Typography.Text type="secondary">{t('lands.documents.notAvailable')}</Typography.Text>
          ),
      },
    ],
    [t, locale, downloadMutation],
  );

  // On an approved land the owner may only correct the outstanding required types, mirroring
  // the server guard; on a Draft/Rejected land every type is selectable.
  const typeOptions = (typesQuery.data ?? [])
    .filter((type) => !isApprovedLand || outstandingTypeIds.has(type.id))
    .map((type) => ({
      value: type.id,
      label: type.isRequiredForVerification
        ? `${type.name} (${t('lands.documents.requiredBadge')})`
        : type.name,
    }));

  // Documents whose review was invalidated because the land data changed afterwards.
  // Nothing is lost (the earlier review stays on the row) and no new upload is needed:
  // the administrator simply has to review the evidence again.
  const reReviewDocuments = documents.filter(needsReReview);

  const submit = (values: DocumentFormValues) => {
    const file = values.file?.[0]?.originFileObj;
    if (!file) {
      setFormError(t('lands.documents.fileRequired'));
      return;
    }
    uploadMutation.mutate({ documentTypeId: values.documentTypeId, file });
  };

  return (
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Alert
        type="info"
        showIcon
        title={t('lands.documents.uploadNotice')}
        description={t('lands.documents.downloadNotice')}
      />

      {isApprovedLand && outstandingEvidence && (
        <Alert
          type="warning"
          showIcon
          role="status"
          title={t('lands.documents.correctionTitle')}
          description={t('lands.documents.correctionBody')}
        />
      )}

      {reReviewDocuments.length > 0 && (
        <Alert
          type="warning"
          showIcon
          title={t('lands.documents.reReviewNotice', {
            documents: reReviewDocuments.map((document) => document.documentTypeName).join(', '),
          })}
        />
      )}

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
          scroll={{ x: 'max-content' }}
          size="small"
        />
      )}

      {canAdd ? (
        <Form<DocumentFormValues>
          form={form}
          layout="vertical"
          onFinish={submit}
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
              name="file"
              label={t('lands.documents.fileLabel')}
              valuePropName="fileList"
              getValueFromEvent={(event) =>
                Array.isArray(event) ? event : event?.fileList
              }
              rules={[{ required: true, message: t('lands.documents.fileRequired') }]}
              extra={t('lands.documents.fileHint')}
              style={{ minWidth: 320, marginBottom: 12 }}
            >
              <Upload
                accept={ACCEPTED_DOCUMENT_TYPES}
                beforeUpload={() => false}
                maxCount={1}
              >
                <Button icon={<FileUp size={16} aria-hidden="true" />}>
                  {t('lands.documents.chooseFile')}
                </Button>
              </Upload>
            </Form.Item>
            <Form.Item label={null} colon={false} style={{ marginBottom: 12 }}>
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
