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
  Tag,
  Timeline,
  Typography,
} from 'antd';
import { ArrowLeft, Check, PenLine, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import {
  fetchContract,
  fetchContractReviews,
  reviewContract,
  signContract,
  terminateContract,
} from '@/api/contracts';
import { isApiError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import { useAuth } from '@/auth/useAuth';
import { ContractStatusTag } from '@/components/ContractStatusTag';
import { FullPageState } from '@/components/FullPageState';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';

interface ReasonFormValues {
  reason: string;
}

export function ContractDetailPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { id: idParam } = useParams();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [terminateForm] = Form.useForm<ReasonFormValues>();
  const [rejectOpen, setRejectOpen] = useState(false);
  const [terminateOpen, setTerminateOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0;

  const contractQuery = useQuery({
    queryKey: ['contract', id],
    queryFn: () => fetchContract(id),
    enabled: validId,
  });

  const reviewsQuery = useQuery({
    queryKey: ['contract', id, 'reviews'],
    queryFn: () => fetchContractReviews(id),
    enabled: validId,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['contract', id] });
    await queryClient.invalidateQueries({ queryKey: ['contracts'] });
  };

  const onActionError = (error: unknown) => setActionError(resolveApiMessage(error, t));

  const signMutation = useMutation({
    mutationFn: () => signContract(id),
    onSuccess: async () => {
      setActionError(null);
      message.success(t('contracts.signed'));
      await refresh();
    },
    onError: onActionError,
  });

  const reviewMutation = useMutation({
    mutationFn: (approve: boolean) => reviewContract(id, { approve }),
    onSuccess: async () => {
      setActionError(null);
      message.success(t('contracts.reviewed'));
      await refresh();
    },
    onError: onActionError,
  });

  const rejectMutation = useMutation({
    mutationFn: (values: ReasonFormValues) =>
      reviewContract(id, { approve: false, reason: values.reason.trim() }),
    onSuccess: async () => {
      setRejectOpen(false);
      rejectForm.resetFields();
      setActionError(null);
      message.success(t('contracts.rejected'));
      await refresh();
    },
    onError: onActionError,
  });

  const terminateMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => terminateContract(id, { reason: values.reason.trim() }),
    onSuccess: async () => {
      setTerminateOpen(false);
      terminateForm.resetFields();
      setActionError(null);
      message.success(t('contracts.terminated'));
      await refresh();
    },
    onError: onActionError,
  });

  if (!validId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('contracts.errors.notFound')}
        description={t('errors.notFoundBody')}
        action={
          <Link to="/app/contracts">
            <Button type="primary">{t('contracts.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (contractQuery.isLoading) {
    return <FullPageState variant="loading" title={t('common.loading')} />;
  }

  if (contractQuery.isError) {
    const status = isApiError(contractQuery.error) ? contractQuery.error.status : null;
    return (
      <FullPageState
        variant="info"
        code={status === 403 ? '403' : '404'}
        title={status === 403 ? t('errors.forbiddenTitle') : t('contracts.errors.notFound')}
        description={status === 403 ? t('contracts.errors.forbidden') : t('errors.notFoundBody')}
        action={
          <Link to="/app/contracts">
            <Button type="primary">{t('contracts.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  const contract = contractQuery.data;
  if (!contract) {
    return null;
  }

  const role = user?.role;
  const isAdmin = role === 'Admin' || role === 'SuperAdmin';
  const isInvestorParty = user?.id === contract.investorId;
  const isLandlordParty = user?.id === contract.landlordId;
  const pending = contract.status === 'PendingSignatures';

  const mySignatureDone = isInvestorParty
    ? contract.investorSignedAt !== null
    : isLandlordParty
      ? contract.landlordSignedAt !== null
      : true;
  const canSign = pending && (isInvestorParty || isLandlordParty) && !mySignatureDone;
  const canReview = isAdmin && pending;
  const canTerminate = isAdmin && contract.status === 'Active';

  const confirmSign = () =>
    modal.confirm({
      title: t('contracts.confirmSignTitle'),
      content: t('contracts.confirmSignBody'),
      okText: t('contracts.sign'),
      cancelText: t('common.cancel'),
      onOk: () => signMutation.mutateAsync(),
    });

  const confirmApprove = () =>
    modal.confirm({
      title: t('contracts.confirmApproveTitle'),
      content: t('contracts.confirmApproveBody'),
      okText: t('contracts.approve'),
      cancelText: t('common.cancel'),
      onOk: () => reviewMutation.mutateAsync(true),
    });

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to="/app/contracts" className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('contracts.backToList')}
          </Link>
          <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
            {t('contracts.detailTitle')} #{contract.id} <ContractStatusTag status={contract.status} />
          </Typography.Title>
        </div>
        <Space wrap>
          {canSign && (
            <Button type="primary" icon={<PenLine size={16} aria-hidden="true" />} loading={signMutation.isPending} onClick={confirmSign}>
              {t('contracts.sign')}
            </Button>
          )}
          {canReview && (
            <Button type="primary" icon={<Check size={16} aria-hidden="true" />} loading={reviewMutation.isPending} onClick={confirmApprove}>
              {t('contracts.approve')}
            </Button>
          )}
          {canReview && (
            <Button danger icon={<X size={16} aria-hidden="true" />} onClick={() => { setActionError(null); setRejectOpen(true); }}>
              {t('contracts.reject')}
            </Button>
          )}
          {canTerminate && (
            <Button danger onClick={() => { setActionError(null); setTerminateOpen(true); }}>
              {t('contracts.terminate')}
            </Button>
          )}
        </Space>
      </div>

      {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}

      <Card title={t('contracts.summary')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'offer', label: t('contracts.list.offerId'), children: `#${contract.offerId}` },
            { key: 'land', label: t('contracts.list.land'), children: contract.landLabel || '—' },
            { key: 'investor', label: t('contracts.list.investor'), children: contract.investorName || '—' },
            { key: 'landlord', label: t('contracts.list.landlord'), children: contract.landlordName || '—' },
            {
              key: 'share',
              label: t('offers.form.landlordSharePct'),
              children: contract.landlordSharePct === null ? '—' : formatDecimal(contract.landlordSharePct, locale),
            },
            { key: 'duration', label: t('offers.form.durationYears'), children: contract.durationYears ?? '—' },
            { key: 'start', label: t('offers.form.startDate'), children: contract.startDate ?? '—' },
            {
              key: 'install',
              label: t('offers.form.installationCost'),
              children: contract.installationCost === null ? '—' : formatDecimal(contract.installationCost, locale),
            },
            {
              key: 'revenue',
              label: t('offers.form.expectedAnnualRevenue'),
              children: contract.expectedAnnualRevenue === null ? '—' : formatDecimal(contract.expectedAnnualRevenue, locale),
            },
            {
              key: 'payback',
              label: t('offers.form.paybackPeriodMonths'),
              children: contract.paybackPeriodMonths === null ? '—' : formatDecimal(contract.paybackPeriodMonths, locale),
            },
            { key: 'created', label: t('contracts.list.createdAt'), children: formatDateTime(contract.createdAt, locale) },
          ]}
        />
      </Card>

      <Card title={t('contracts.signatures')}>
        <Space orientation="vertical" size="small">
          <div>
            <Typography.Text>{t('contracts.investorSignature')}: </Typography.Text>
            {contract.investorSignedAt ? (
              <Tag color="green">{formatDateTime(contract.investorSignedAt, locale)}</Tag>
            ) : (
              <Tag>{t('contracts.notSigned')}</Tag>
            )}
          </div>
          <div>
            <Typography.Text>{t('contracts.landlordSignature')}: </Typography.Text>
            {contract.landlordSignedAt ? (
              <Tag color="green">{formatDateTime(contract.landlordSignedAt, locale)}</Tag>
            ) : (
              <Tag>{t('contracts.notSigned')}</Tag>
            )}
          </div>
          <div>
            <Typography.Text>{t('contracts.adminReview')}: </Typography.Text>
            {contract.adminReviewedAt ? (
              <Tag color="green">{formatDateTime(contract.adminReviewedAt, locale)}</Tag>
            ) : (
              <Tag>{t('contracts.notReviewed')}</Tag>
            )}
          </div>
        </Space>
      </Card>

      <Card title={t('contracts.reviews')}>
        {reviewsQuery.isLoading ? (
          <Spin />
        ) : reviewsQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(reviewsQuery.error, t)} />
        ) : (reviewsQuery.data?.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('contracts.noReviews')}</Typography.Text>
        ) : (
          <Timeline
            items={(reviewsQuery.data ?? []).map((review) => ({
              key: review.id,
              color: review.decision === 'Rejected' ? 'red' : review.decision === 'Terminated' ? 'gray' : 'green',
              title: (
                <Space wrap>
                  <Tag>{review.decision}</Tag>
                  <Typography.Text type="secondary">{formatDateTime(review.createdAt, locale)}</Typography.Text>
                </Space>
              ),
              content: (
                <div>
                  <div>{review.reviewerName}</div>
                  {review.reason && <Typography.Text type="secondary">{review.reason}</Typography.Text>}
                </div>
              ),
            }))}
          />
        )}
      </Card>

      <Modal
        open={rejectOpen}
        title={t('contracts.reject')}
        okText={t('contracts.reject')}
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
        open={terminateOpen}
        title={t('contracts.terminate')}
        okText={t('contracts.terminate')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={terminateMutation.isPending}
        onCancel={() => setTerminateOpen(false)}
        onOk={() => terminateForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues> form={terminateForm} layout="vertical" onFinish={(values) => terminateMutation.mutate(values)}>
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
