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
  Space,
  Spin,
  Timeline,
  Table,
  Typography,
} from 'antd';
import { ArrowLeft, Check, FileSignature, HandCoins, Undo2, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { createContract } from '@/api/contracts';
import { isApiError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import {
  acceptOffer,
  adminApproveOffer,
  adminRejectOffer,
  counterOffer,
  fetchNegotiationHistory,
  fetchOffer,
  fetchOfferVersions,
  rejectOffer,
  withdrawOffer,
} from '@/api/offers';
import { formatApprovalUtc } from '@/utils/approvalTime';
import { useAuth } from '@/auth/useAuth';
import { FullPageState } from '@/components/FullPageState';
import { OfferStatusTag } from '@/components/OfferStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import {
  CONNECTION_MECHANISM_NAMES,
  OFFER_ACTION_NAMES,
  OFFER_STATUS,
} from '@/types/offers';

interface CounterFormValues {
  newAmount: number;
  message?: string | null;
}

interface ReasonFormValues {
  reason: string;
}

export function OfferDetailPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { id: idParam } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const [counterForm] = Form.useForm<CounterFormValues>();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [adminRejectForm] = Form.useForm<ReasonFormValues>();
  const [counterOpen, setCounterOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [adminRejectOpen, setAdminRejectOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0;

  // "Back" must land on a list the current role can actually open: /app/offers is
  // investor-only, so an admin or landlord would otherwise hit a 403 dead-end.
  const backTarget =
    user?.role === 'Admin' || user?.role === 'SuperAdmin'
      ? '/app/admin/offers'
      : user?.role === 'Landlord'
        ? '/app/lands'
        : '/app/offers';

  const offerQuery = useQuery({
    queryKey: ['offer', id],
    queryFn: () => fetchOffer(id),
    enabled: validId,
  });

  const versionsQuery = useQuery({
    queryKey: ['offer', id, 'versions'], queryFn: () => fetchOfferVersions(id), enabled: validId,
  });
  const historyQuery = useQuery({
    queryKey: ['offer', id, 'history'],
    queryFn: () => fetchNegotiationHistory(id),
    enabled: validId,
  });

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['offer', id] });
    await queryClient.invalidateQueries({ queryKey: ['my-offers'] });
    await queryClient.invalidateQueries({ queryKey: ['land-offers'] });
    await queryClient.invalidateQueries({ queryKey: ['pending-offers'] });
  };

  const onActionError = (error: unknown) => setActionError(resolveApiMessage(error, t));
  const onActionSuccess = async (serverMessage: string) => {
    setActionError(null);
    message.success(serverMessage);
    await invalidate();
  };

  const counterMutation = useMutation({
    mutationFn: (values: CounterFormValues) =>
      counterOffer(id, { newAmount: values.newAmount, message: values.message?.trim() || null }),
    onSuccess: async () => {
      setCounterOpen(false);
      counterForm.resetFields();
      await onActionSuccess(t('offers.countered'));
    },
    onError: onActionError,
  });

  const acceptMutation = useMutation({
    mutationFn: () => acceptOffer(id),
    onSuccess: async () => {
      await onActionSuccess(t('offers.accepted'));
    },
    onError: onActionError,
  });

  const rejectMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => rejectOffer(id, { reason: values.reason.trim() }),
    onSuccess: async () => {
      setRejectOpen(false);
      rejectForm.resetFields();
      await onActionSuccess(t('offers.rejected'));
    },
    onError: onActionError,
  });

  const withdrawMutation = useMutation({
    mutationFn: () => withdrawOffer(id),
    onSuccess: async () => {
      await onActionSuccess(t('offers.withdrawn'));
    },
    onError: onActionError,
  });

  const adminApproveMutation = useMutation({
    mutationFn: () => adminApproveOffer(id),
    onSuccess: async () => {
      await onActionSuccess(t('offers.adminApproved'));
    },
    onError: onActionError,
  });

  const adminRejectMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => adminRejectOffer(id, { reason: values.reason.trim() }),
    onSuccess: async () => {
      setAdminRejectOpen(false);
      adminRejectForm.resetFields();
      await onActionSuccess(t('offers.adminRejected'));
    },
    onError: onActionError,
  });

  const contractMutation = useMutation({
    mutationFn: () => createContract({ offerId: id }),
    onSuccess: async (contract) => {
      setActionError(null);
      message.success(t('contracts.created'));
      await queryClient.invalidateQueries({ queryKey: ['contracts'] });
      await invalidate();
      navigate(`/app/contracts/${contract.id}`);
    },
    onError: onActionError,
  });

  if (!validId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('offers.errors.notFound')}
        description={t('errors.notFoundBody')}
        action={
          <Link to={backTarget}>
            <Button type="primary">{t('offers.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (offerQuery.isLoading) {
    return <FullPageState variant="loading" title={t('common.loading')} />;
  }

  if (offerQuery.isError) {
    const status = isApiError(offerQuery.error) ? offerQuery.error.status : null;
    return (
      <FullPageState
        variant="info"
        code={status === 403 ? '403' : '404'}
        title={status === 403 ? t('errors.forbiddenTitle') : t('offers.errors.notFound')}
        description={status === 403 ? t('offers.errors.forbidden') : t('errors.notFoundBody')}
        action={
          <Link to={backTarget}>
            <Button type="primary">{t('offers.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  const offer = offerQuery.data;
  if (!offer) {
    return null;
  }

  const role = user?.role;
  const isInvestor = role === 'Investor';
  const isLandlord = role === 'Landlord';
  const isAdmin = role === 'Admin' || role === 'SuperAdmin';

  const awaitingLandlord = offer.status === OFFER_STATUS.PendingLandlordResponse;
  const awaitingInvestor = offer.status === OFFER_STATUS.PendingInvestorResponse;
  const canRespond = (awaitingLandlord && isLandlord) || (awaitingInvestor && isInvestor);
  const canWithdraw = isInvestor && (awaitingLandlord || awaitingInvestor);
  const canAdminDecide = isAdmin && offer.status === OFFER_STATUS.PendingAdminApproval;
  const canCreateContract = isAdmin && offer.status === OFFER_STATUS.Accepted;

  const version = offer.latestVersion;
  const mechanismName = CONNECTION_MECHANISM_NAMES[offer.connectionMechanism];

  const confirmAccept = () =>
    modal.confirm({
      title: t('offers.confirmAcceptTitle'),
      content: t('offers.confirmAcceptBody'),
      okText: t('offers.accept'),
      cancelText: t('common.cancel'),
      onOk: () => acceptMutation.mutateAsync(),
    });

  const confirmWithdraw = () =>
    modal.confirm({
      title: t('offers.confirmWithdrawTitle'),
      content: t('offers.confirmWithdrawBody'),
      okText: t('offers.withdraw'),
      cancelText: t('common.cancel'),
      onOk: () => withdrawMutation.mutateAsync(),
    });

  const confirmAdminApprove = () =>
    modal.confirm({
      title: t('offers.confirmAdminApproveTitle'),
      content: t('offers.confirmAdminApproveBody'),
      okText: t('offers.adminApprove'),
      cancelText: t('common.cancel'),
      onOk: () => adminApproveMutation.mutateAsync(),
    });

  const confirmCreateContract = () =>
    modal.confirm({
      title: t('contracts.confirmCreateTitle'),
      content: t('contracts.confirmCreateBody'),
      okText: t('contracts.create'),
      cancelText: t('common.cancel'),
      onOk: () => contractMutation.mutateAsync(),
    });

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to={backTarget} className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('offers.backToList')}
          </Link>
          <Typography.Title level={1} style={{ margin: '8px 0 0' }}>
            {t('offers.detailTitle')} #{offer.id} <OfferStatusTag status={offer.status} />
          </Typography.Title>
        </div>
        <Space wrap>
          {canRespond && (
            <Button icon={<HandCoins size={16} aria-hidden="true" />} onClick={() => { setActionError(null); setCounterOpen(true); }}>
              {t('offers.counter')}
            </Button>
          )}
          {canRespond && (
            <Button type="primary" icon={<Check size={16} aria-hidden="true" />} loading={acceptMutation.isPending} onClick={confirmAccept}>
              {t('offers.accept')}
            </Button>
          )}
          {canRespond && (
            <Button danger icon={<X size={16} aria-hidden="true" />} onClick={() => { setActionError(null); setRejectOpen(true); }}>
              {t('offers.reject')}
            </Button>
          )}
          {canWithdraw && (
            <Button icon={<Undo2 size={16} aria-hidden="true" />} loading={withdrawMutation.isPending} onClick={confirmWithdraw}>
              {t('offers.withdraw')}
            </Button>
          )}
          {canAdminDecide && (
            <Button type="primary" icon={<Check size={16} aria-hidden="true" />} loading={adminApproveMutation.isPending} onClick={confirmAdminApprove}>
              {t('offers.adminApprove')}
            </Button>
          )}
          {canAdminDecide && (
            <Button danger icon={<X size={16} aria-hidden="true" />} onClick={() => { setActionError(null); setAdminRejectOpen(true); }}>
              {t('offers.adminReject')}
            </Button>
          )}
          {canCreateContract && (
            <Button type="primary" icon={<FileSignature size={16} aria-hidden="true" />} loading={contractMutation.isPending} onClick={confirmCreateContract}>
              {t('contracts.create')}
            </Button>
          )}
        </Space>
      </div>

      {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}

      <Card title={t('offers.summary')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'id', label: t('offers.list.offerId'), children: `#${offer.id}` },
            { key: 'land', label: t('offers.list.landId'), children: offer.landId },
            {
              key: 'investor',
              label: t('offers.list.investor'),
              children: offer.investorName ?? '—',
            },
            {
              key: 'landlord',
              label: t('offers.list.landlord'),
              children: offer.landLandlordName ?? '—',
            },
            {
              key: 'amount',
              label: t('offers.list.amount'),
              children: formatDecimal(offer.currentAmount, locale),
            },
            {
              key: 'capacity',
              label: t('offers.list.capacity'),
              children: formatDecimal(offer.requiredCapacityMw, locale),
            },
            {
              key: 'mechanism',
              label: t('offers.form.connectionMechanism'),
              children: mechanismName ? t(`offers.mechanism.${mechanismName}`) : '—',
            },
            {
              key: 'created',
              label: t('offers.list.createdAt'),
              children: formatDateTime(offer.createdAt, locale),
            },
            {
              key: 'updated',
              label: t('offers.list.updatedAt'),
              children: formatDateTime(offer.updatedAt, locale),
            },
          ]}
        />
      </Card>

      {version && (
        <Card title={t('offers.latestVersion', { version: version.versionNumber })}>
          <Descriptions
            column={{ xs: 1, sm: 1, md: 2 }}
            size="small"
            bordered
            items={[
              {
                key: 'share',
                label: t('offers.form.landlordSharePct'),
                children: version.landlordSharePct === null ? '—' : formatDecimal(version.landlordSharePct, locale),
              },
              {
                key: 'duration',
                label: t('offers.form.durationYears'),
                children: version.durationYears ?? '—',
              },
              { key: 'start', label: t('offers.form.startDate'), children: version.startDate ?? '—' },
              {
                key: 'install',
                label: t('offers.form.installationCost'),
                children: version.installationCost === null ? '—' : formatDecimal(version.installationCost, locale),
              },
              {
                key: 'solarCell',
                label: t('offers.form.solarCellCapacityKw'),
                children: version.solarCellCapacityKw === null ? '—' : formatDecimal(version.solarCellCapacityKw, locale),
              },
              {
                key: 'revenue',
                label: t('offers.form.expectedAnnualRevenue'),
                children: version.expectedAnnualRevenue === null ? '—' : formatDecimal(version.expectedAnnualRevenue, locale),
              },
              {
                key: 'effective',
                label: t('offers.form.effectiveCostPerKw'),
                children: version.effectiveCostPerKw === null ? '—' : formatDecimal(version.effectiveCostPerKw, locale),
              },
              {
                key: 'payback',
                label: t('offers.form.paybackPeriodMonths'),
                children: version.paybackPeriodMonths === null ? '—' : formatDecimal(version.paybackPeriodMonths, locale),
              },
              {
                key: 'proposedBy',
                label: t('offers.proposedBy'),
                children: `${version.createdByName} (${version.createdByRole})`,
              },
              {
                key: 'createdAt',
                label: t('offers.list.createdAt'),
                children: formatDateTime(version.createdAt, locale),
              },
            ]}
          />
          {version.message && (
            <Typography.Paragraph style={{ marginTop: 12, marginBottom: 0 }}>
              {version.message}
            </Typography.Paragraph>
          )}
        </Card>
      )}

      <Card title={t('trace.versions')}>
        {versionsQuery.isLoading ? <Spin /> : versionsQuery.isError ?
          <Alert type="error" showIcon title={resolveApiMessage(versionsQuery.error, t)} /> :
          <Table rowKey="id" pagination={false} dataSource={versionsQuery.data ?? []} scroll={{ x: 'max-content' }}
            locale={{ emptyText: t('trace.empty') }} columns={[
              { title: t('trace.record'), dataIndex: 'id' },
              { title: t('trace.version'), dataIndex: 'versionNumber' },
              { title: t('users.approval.actor'), key: 'actor', render: (_v, row) => '#' + row.createdById + ' · ' + row.createdByName + ' (' + row.createdByRole + ')' },
              { title: t('offers.form.landlordSharePct'), dataIndex: 'landlordSharePct' },
              { title: t('offers.form.durationYears'), dataIndex: 'durationYears' },
              { title: t('offers.form.startDate'), dataIndex: 'startDate', render: (v: string | null) => formatDateTime(v, locale) },
              { title: t('offers.form.installationCost'), dataIndex: 'installationCost' },
              { title: t('offers.form.solarCellCapacityKw'), dataIndex: 'solarCellCapacityKw' },
              { title: t('offers.form.expectedAnnualRevenue'), dataIndex: 'expectedAnnualRevenue' },
              { title: t('offers.form.effectiveCostPerKw'), dataIndex: 'effectiveCostPerKw' },
              { title: t('offers.form.paybackPeriodMonths'), dataIndex: 'paybackPeriodMonths' },
              { title: t('offers.form.message'), dataIndex: 'message' },
              { title: t('offers.list.createdAt'), dataIndex: 'createdAt', render: (v: string | null) => formatApprovalUtc(v, locale) },
            ]} />}
      </Card>

      <Card title={t('offers.negotiationHistory')}>
        {historyQuery.isLoading ? (
          <Spin />
        ) : historyQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(historyQuery.error, t)} />
        ) : (historyQuery.data?.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('offers.historyEmpty')}</Typography.Text>
        ) : (
          <Timeline
            items={(historyQuery.data ?? []).map((entry) => {
              const actionName = OFFER_ACTION_NAMES[entry.actionType];
              return {
                key: entry.id,
                color: 'green',
                title: (
                  <Space wrap>
                    <strong>
                      {actionName ? t(`offers.action.${actionName}`) : String(entry.actionType)}
                    </strong>
                    <Typography.Text type="secondary">
                      {formatDateTime(entry.createdAt, locale)}
                    </Typography.Text>
                  </Space>
                ),
                content: (
                  <div>
                    <div>
                      #{entry.actorId} · {entry.actorName ?? '—'}
                      {entry.actorRole ? ` (${entry.actorRole})` : ''}
                      {entry.amount !== null ? ` · ${formatDecimal(entry.amount, locale)}` : ''}
                    </div>
                    {entry.message && (
                      <Typography.Text type="secondary">{entry.message}</Typography.Text>
                    )}
                  </div>
                ),
              };
            })}
          />
        )}
      </Card>

      <Modal
        open={counterOpen}
        title={t('offers.counter')}
        okText={t('offers.counterSubmit')}
        cancelText={t('common.cancel')}
        confirmLoading={counterMutation.isPending}
        onCancel={() => setCounterOpen(false)}
        onOk={() => counterForm.submit()}
        destroyOnHidden
      >
        <Form<CounterFormValues> form={counterForm} layout="vertical" onFinish={(values) => counterMutation.mutate(values)}>
          <Form.Item
            name="newAmount"
            label={t('offers.form.newAmount')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { type: 'number', min: 0.01, max: 999.99, message: t('offers.form.shareRange') },
            ]}
          >
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="message" label={t('offers.form.message')} rules={[{ max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}>
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={rejectOpen}
        title={t('offers.reject')}
        okText={t('offers.reject')}
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
        open={adminRejectOpen}
        title={t('offers.adminReject')}
        okText={t('offers.adminReject')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={adminRejectMutation.isPending}
        onCancel={() => setAdminRejectOpen(false)}
        onOk={() => adminRejectForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues> form={adminRejectForm} layout="vertical" onFinish={(values) => adminRejectMutation.mutate(values)}>
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
