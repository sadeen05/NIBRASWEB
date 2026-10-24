import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App as AntApp, Button, Card, Form, Input, Modal, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { Check, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';

import { resolveApiMessage } from '@/api/knownMessages';
import { adminApproveOffer, adminRejectOffer, fetchPendingAdminApproval } from '@/api/offers';
import { OfferStatusTag } from '@/components/OfferStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import type { OfferDto } from '@/types/offers';

interface ReasonFormValues {
  reason: string;
}

/** Admin queue of offers awaiting approval (approving places the land under contract). */
export function AdminOffersPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [rejectId, setRejectId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const offersQuery = useQuery({
    queryKey: ['pending-offers'],
    queryFn: fetchPendingAdminApproval,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['pending-offers'] });
    await queryClient.invalidateQueries({ queryKey: ['lands'] });
    await queryClient.invalidateQueries({ queryKey: ['admin-lands'] });
  };

  const onActionError = (error: unknown) => setActionError(resolveApiMessage(error, t));

  const approveMutation = useMutation({
    mutationFn: (offerId: number) => adminApproveOffer(offerId),
    onSuccess: async (offer) => {
      setActionError(null);
      message.success(t('offers.adminApproved'));
      await refresh();
      // The approved offer leaves this queue; open it so the admin can create the
      // contract (the only remaining lifecycle step available for an accepted offer).
      navigate(`/app/offers/${offer.id}`);
    },
    onError: onActionError,
  });

  const rejectMutation = useMutation({
    mutationFn: (input: { offerId: number; reason: string }) =>
      adminRejectOffer(input.offerId, { reason: input.reason }),
    onSuccess: async () => {
      setRejectId(null);
      rejectForm.resetFields();
      setActionError(null);
      message.success(t('offers.adminRejected'));
      await refresh();
    },
    onError: onActionError,
  });

  const confirmApprove = (offerId: number) =>
    modal.confirm({
      title: t('offers.confirmAdminApproveTitle'),
      content: t('offers.confirmAdminApproveBody'),
      okText: t('offers.adminApprove'),
      cancelText: t('common.cancel'),
      onOk: () => approveMutation.mutateAsync(offerId),
    });

  const columns: TableColumnsType<OfferDto> = [
    {
      title: t('offers.list.offerId'),
      dataIndex: 'id',
      key: 'id',
      render: (value: number) => <Link to={`/app/offers/${value}`}>#{value}</Link>,
    },
    { title: t('offers.list.landId'), dataIndex: 'landId', key: 'landId' },
    {
      title: t('offers.list.investor'),
      dataIndex: 'investorName',
      key: 'investorName',
      render: (value: string | null) => value ?? '—',
    },
    {
      title: t('offers.list.amount'),
      dataIndex: 'currentAmount',
      key: 'currentAmount',
      render: (value: number) => formatDecimal(value, locale),
    },
    {
      title: t('offers.list.capacity'),
      dataIndex: 'requiredCapacityMw',
      key: 'requiredCapacityMw',
      render: (value: number) => formatDecimal(value, locale),
    },
    {
      title: t('offers.list.status'),
      dataIndex: 'status',
      key: 'status',
      render: (value: number) => <OfferStatusTag status={value} />,
    },
    {
      title: t('offers.list.updatedAt'),
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      render: (value: string | null) => formatDateTime(value, locale),
    },
    {
      title: t('lands.list.actions'),
      key: 'actions',
      render: (_value, offer) => (
        <Space>
          <Link to={`/app/offers/${offer.id}`}>{t('common.view')}</Link>
          <Button
            size="small"
            type="primary"
            icon={<Check size={14} aria-hidden="true" />}
            onClick={() => confirmApprove(offer.id)}
          >
            {t('offers.adminApprove')}
          </Button>
          <Button
            size="small"
            danger
            icon={<X size={14} aria-hidden="true" />}
            onClick={() => {
              setActionError(null);
              setRejectId(offer.id);
            }}
          >
            {t('offers.adminReject')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Typography.Title level={1} style={{ margin: 0 }}>
          {t('admin.offerQueueTitle')}
        </Typography.Title>
        <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
          {t('admin.offerQueueSubtitle')}
        </Typography.Paragraph>
      </div>

      {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}

      <Card styles={{ body: { paddingTop: 16 } }}>
        {offersQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveApiMessage(offersQuery.error, t)}
            action={
              <Button size="small" onClick={() => void offersQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        ) : (
          <Table<OfferDto>
            rowKey="id"
            columns={columns}
            dataSource={offersQuery.data ?? []}
            loading={offersQuery.isLoading}
            locale={{ emptyText: t('admin.offerQueueEmpty') }}
            pagination={false}
          />
        )}
      </Card>

      <Modal
        open={rejectId !== null}
        title={t('offers.adminReject')}
        okText={t('offers.adminReject')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={rejectMutation.isPending}
        onCancel={() => setRejectId(null)}
        onOk={() => rejectForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues>
          form={rejectForm}
          layout="vertical"
          onFinish={(values) => rejectMutation.mutate({ offerId: rejectId ?? 0, reason: values.reason.trim() })}
        >
          <Form.Item
            name="reason"
            label={t('common.reason')}
            rules={[{ required: true, message: t('common.reasonRequired') }, { max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
