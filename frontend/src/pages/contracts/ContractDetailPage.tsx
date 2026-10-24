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
  Tag,
  Timeline,
  Typography,
} from 'antd';
import { ArrowLeft, Check, PenLine, X } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import {
  consentToContractRevision,
  decideContractRevisionAsAdmin,
  decideContractRevisionAsSuperAdmin,
  fetchContract,
  fetchContractRevisions,
  fetchContractReviews,
  fetchContractVersions,
  requestContractRevision,
  reviewContract,
  signContract,
  terminateContract,
} from '@/api/contracts';
import { isApiError, LocalizedMessageError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import { useAuth } from '@/auth/useAuth';
import { ContractStatusTag } from '@/components/ContractStatusTag';
import { FullPageState } from '@/components/FullPageState';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import type { ContractRevisionDto, ContractVersionTermsDto } from '@/types/contracts';

interface ReasonFormValues {
  reason: string;
}

interface RevisionFormValues {
  reason: string;
  landlordSharePct: number;
  durationYears?: number | null;
  startDate?: string | null;
  installationCost?: number | null;
  solarCellCapacityKw?: number | null;
  expectedAnnualRevenue?: number | null;
  effectiveCostPerKw?: number | null;
  paybackPeriodMonths?: number | null;
  noticePeriodDays?: number | null;
}

const OPEN_REVISION_STATUSES = [
  'PendingPartyConsent',
  'PendingAdminReview',
  'PendingSuperAdminReview',
] as const;

export function ContractDetailPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { id: idParam } = useParams();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [terminateForm] = Form.useForm<ReasonFormValues>();
  const [revisionForm] = Form.useForm<RevisionFormValues>();
  const [revisionRejectForm] = Form.useForm<ReasonFormValues>();
  const [escalateForm] = Form.useForm<ReasonFormValues>();
  const [superAdminForm] = Form.useForm<ReasonFormValues>();
  const [rejectOpen, setRejectOpen] = useState(false);
  const [terminateOpen, setTerminateOpen] = useState(false);
  const [revisionRequestOpen, setRevisionRequestOpen] = useState(false);
  const [revisionRejectOpen, setRevisionRejectOpen] = useState(false);
  const [escalateOpen, setEscalateOpen] = useState(false);
  const [superAdminDecision, setSuperAdminDecision] = useState<'Continue' | 'Cancel' | null>(null);
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

  const versionsQuery = useQuery({
    queryKey: ['contract', id, 'versions'],
    queryFn: () => fetchContractVersions(id),
    enabled: validId,
  });

  const revisionsQuery = useQuery({
    queryKey: ['contract', id, 'revisions'],
    queryFn: () => fetchContractRevisions(id),
    enabled: validId,
  });

  const versions = versionsQuery.data ?? [];
  const drafts = versions
    .filter((version) => version.versionKind === 'Draft')
    .sort((left, right) => left.versionNumber - right.versionNumber);
  const latestDraft = drafts.length > 0 ? drafts[drafts.length - 1] : undefined;
  const versionsByRowId = new Map(
    versions.map((version) => [version.id, version.versionNumber] as const),
  );
  const revisions = revisionsQuery.data ?? [];
  const openRevision = revisions.find((revision) =>
    (OPEN_REVISION_STATUSES as readonly string[]).includes(revision.status),
  );
  const myConsent = openRevision?.consents.find((consent) => consent.partyUserId === user?.id);

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
    mutationFn: (values: ReasonFormValues) =>
      terminateContract(id, { reason: values.reason.trim() }),
    onSuccess: async () => {
      setTerminateOpen(false);
      terminateForm.resetFields();
      setActionError(null);
      message.success(t('contracts.terminated'));
      await refresh();
    },
    onError: onActionError,
  });

  const requestRevisionMutation = useMutation({
    mutationFn: (values: RevisionFormValues) => {
      if (!latestDraft) {
        throw new LocalizedMessageError(t('contracts.errors.revisionNoDraft'));
      }
      return requestContractRevision(id, {
        expectedDraftVersionId: latestDraft.id,
        reason: values.reason.trim(),
        landlordSharePct: values.landlordSharePct,
        durationYears: values.durationYears ?? null,
        startDate: values.startDate || null,
        installationCost: values.installationCost ?? null,
        solarCellCapacityKw: values.solarCellCapacityKw ?? null,
        expectedAnnualRevenue: values.expectedAnnualRevenue ?? null,
        effectiveCostPerKw: values.effectiveCostPerKw ?? null,
        paybackPeriodMonths: values.paybackPeriodMonths ?? null,
        noticePeriodDays: values.noticePeriodDays ?? null,
      });
    },
    onSuccess: async () => {
      setRevisionRequestOpen(false);
      revisionForm.resetFields();
      setActionError(null);
      message.success(t('contracts.revision.requested'));
      await refresh();
    },
    onError: onActionError,
  });

  const approveRevisionMutation = useMutation({
    mutationFn: () => {
      if (!openRevision || !latestDraft) {
        throw new LocalizedMessageError(t('contracts.errors.revisionNoDraft'));
      }
      return consentToContractRevision(id, openRevision.id, {
        expectedDraftVersionId: latestDraft.id,
        approved: true,
      });
    },
    onSuccess: async () => {
      setActionError(null);
      message.success(t('contracts.revision.consentRecorded'));
      await refresh();
    },
    onError: onActionError,
  });

  const rejectRevisionMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => {
      if (!openRevision || !latestDraft) {
        throw new LocalizedMessageError(t('contracts.errors.revisionNoDraft'));
      }
      return consentToContractRevision(id, openRevision.id, {
        expectedDraftVersionId: latestDraft.id,
        approved: false,
        reason: values.reason.trim(),
      });
    },
    onSuccess: async () => {
      setRevisionRejectOpen(false);
      revisionRejectForm.resetFields();
      setActionError(null);
      message.success(t('contracts.revision.consentRecorded'));
      await refresh();
    },
    onError: onActionError,
  });

  const adminRevisionMutation = useMutation({
    mutationFn: (input: { decision: 'Approve' | 'Escalate'; reason?: string }) => {
      if (!openRevision || !latestDraft) {
        throw new LocalizedMessageError(t('contracts.errors.revisionNoDraft'));
      }
      return decideContractRevisionAsAdmin(id, openRevision.id, {
        expectedDraftVersionId: latestDraft.id,
        decision: input.decision,
        reason: input.reason?.trim() || null,
      });
    },
    onSuccess: async (_revision, input) => {
      setEscalateOpen(false);
      escalateForm.resetFields();
      setActionError(null);
      message.success(
        input.decision === 'Escalate'
          ? t('contracts.revision.escalated')
          : t('contracts.revision.adminApproved'),
      );
      await refresh();
    },
    onError: onActionError,
  });

  const superAdminRevisionMutation = useMutation({
    mutationFn: (values: ReasonFormValues) => {
      if (!openRevision || !latestDraft || !superAdminDecision) {
        throw new LocalizedMessageError(t('contracts.errors.revisionNoDraft'));
      }
      return decideContractRevisionAsSuperAdmin(id, openRevision.id, {
        expectedDraftVersionId: latestDraft.id,
        decision: superAdminDecision,
        reason: values.reason.trim(),
      });
    },
    onSuccess: async () => {
      const wasCancel = superAdminDecision === 'Cancel';
      setSuperAdminDecision(null);
      superAdminForm.resetFields();
      setActionError(null);
      message.success(
        wasCancel ? t('contracts.revision.cancelled') : t('contracts.revision.continued'),
      );
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
  const isSuperAdmin = role === 'SuperAdmin';
  const isInvestorParty = user?.id === contract.investorId;
  const isLandlordParty = user?.id === contract.landlordId;
  const isParty = isInvestorParty || isLandlordParty;
  const pending = contract.status === 'PendingSignatures';
  const fullySigned = contract.investorSignedAt !== null && contract.landlordSignedAt !== null;
  const revisionInProgress = openRevision !== undefined;

  const mySignatureDone = isInvestorParty
    ? contract.investorSignedAt !== null
    : isLandlordParty
      ? contract.landlordSignedAt !== null
      : true;
  const canSign = pending && isParty && !mySignatureDone && !revisionInProgress;
  const canReview = isAdmin && pending && !revisionInProgress;
  const canTerminate = isAdmin && contract.status === 'Active';
  const canRequestRevision =
    pending &&
    isParty &&
    !revisionInProgress &&
    !fullySigned &&
    contract.adminReviewedAt === null &&
    latestDraft !== undefined;
  const canConsent = openRevision?.status === 'PendingPartyConsent' && isParty && !myConsent;
  const canAdminDecide = openRevision?.status === 'PendingAdminReview' && isAdmin;
  const canSuperAdminDecide =
    openRevision?.status === 'PendingSuperAdminReview' && isSuperAdmin;

  const translateOr = (key: string, fallback: string) => {
    const translated = t(key);
    return translated === key ? fallback : translated;
  };

  const revisionStatusLabel = (status: string) =>
    translateOr(`contracts.revision.status.${status}`, status);

  const revisionDecisionLabel = (decision: string | null) =>
    decision
      ? translateOr(`contracts.revision.decision${decision}`, decision)
      : '';

  const versionEventLabel = (eventType: string | null) =>
    eventType
      ? translateOr(`contracts.versionEvents.${eventType}`, eventType)
      : t('contracts.versionEvent');

  const termsItems = (terms: ContractVersionTermsDto) => [
    {
      key: 'share',
      label: t('offers.form.landlordSharePct'),
      children:
        terms.landlordSharePct === null ? '—' : formatDecimal(terms.landlordSharePct, locale),
    },
    { key: 'duration', label: t('offers.form.durationYears'), children: terms.durationYears ?? '—' },
    { key: 'start', label: t('offers.form.startDate'), children: formatDateTime(terms.startDate, locale) },
    {
      key: 'install',
      label: t('offers.form.installationCost'),
      children:
        terms.installationCost === null ? '—' : formatDecimal(terms.installationCost, locale),
    },
    {
      key: 'capacity',
      label: t('offers.form.solarCellCapacityKw'),
      children:
        terms.solarCellCapacityKw === null
          ? '—'
          : formatDecimal(terms.solarCellCapacityKw, locale),
    },
    {
      key: 'revenue',
      label: t('offers.form.expectedAnnualRevenue'),
      children:
        terms.expectedAnnualRevenue === null
          ? '—'
          : formatDecimal(terms.expectedAnnualRevenue, locale),
    },
    {
      key: 'effective',
      label: t('offers.form.effectiveCostPerKw'),
      children:
        terms.effectiveCostPerKw === null
          ? '—'
          : formatDecimal(terms.effectiveCostPerKw, locale),
    },
    {
      key: 'payback',
      label: t('offers.form.paybackPeriodMonths'),
      children:
        terms.paybackPeriodMonths === null
          ? '—'
          : formatDecimal(terms.paybackPeriodMonths, locale),
    },
    { key: 'notice', label: t('contracts.noticePeriodDays'), children: terms.noticePeriodDays },
  ];

  const renderRevision = (revision: ContractRevisionDto) => {
    const color =
      revision.status === 'Approved'
        ? 'green'
        : revision.status === 'Rejected' || revision.status === 'Cancelled'
          ? 'red'
          : 'gold';

    return {
      key: revision.id,
      color,
      title: (
        <Space wrap>
          <Tag color={color}>{revisionStatusLabel(revision.status)}</Tag>
          <Typography.Text strong>
            {t('contracts.revision.revisionNumber', { id: revision.id })}
          </Typography.Text>
          <Typography.Text type="secondary">
            {formatDateTime(revision.proposedAt, locale)}
          </Typography.Text>
        </Space>
      ),
      content: (
        <div>
          <div>
            {t('contracts.revision.proposedBy')}: {revision.proposedByName}
            {revision.proposedByRole ? ` · ${revision.proposedByRole}` : ''}
          </div>
          <div>
            {t('contracts.revision.baseDraft', {
              number: revision.baseDraftVersionNumber,
            })}
          </div>
          <Typography.Paragraph style={{ marginTop: 8 }}>
            {revision.reason}
          </Typography.Paragraph>

          {revision.proposedTerms && (
            <Descriptions
              column={1}
              size="small"
              bordered
              title={t('contracts.revision.proposedTerms')}
              items={termsItems(revision.proposedTerms)}
            />
          )}

          <div style={{ marginTop: 8 }}>
            <Typography.Text strong>{t('contracts.revision.consentTitle')}</Typography.Text>
            <div>
              {revision.consents.length === 0 ? (
                <Tag>{t('contracts.revision.waitingForParties')}</Tag>
              ) : (
                <Space orientation="vertical" size={2}>
                  {revision.consents.map((consent) => (
                    <div key={consent.id}>
                      <Tag color={consent.decision === 'Approved' ? 'green' : 'red'}>
                        {consent.decision === 'Approved'
                          ? t('contracts.revision.partyApproved')
                          : t('contracts.revision.partyRejected')}
                      </Tag>
                      <Typography.Text>
                        {consent.partyName} · {consent.partyRole} ·{' '}
                        {formatDateTime(consent.createdAt, locale)}
                      </Typography.Text>
                      {consent.reason && (
                        <Typography.Text type="secondary"> — {consent.reason}</Typography.Text>
                      )}
                    </div>
                  ))}
                </Space>
              )}
            </div>
          </div>

          {revision.adminReviewedAt && (
            <div style={{ marginTop: 8 }}>
              <Tag color={revision.adminDecision === 'Escalated' ? 'orange' : 'green'}>
                {revisionDecisionLabel(revision.adminDecision)}
              </Tag>
              <Typography.Text>
                {revision.adminReviewedByName}
                {revision.adminReviewedByRole ? ` · ${revision.adminReviewedByRole}` : ''} ·{' '}
                {formatDateTime(revision.adminReviewedAt, locale)}
              </Typography.Text>
              {revision.escalationReason && (
                <div>
                  <Typography.Text type="secondary">
                    {t('contracts.revision.escalateReason')}: {revision.escalationReason}
                  </Typography.Text>
                </div>
              )}
            </div>
          )}

          {revision.superAdminDecidedAt && (
            <div style={{ marginTop: 8 }}>
              <Tag color={revision.superAdminDecision === 'Cancelled' ? 'red' : 'green'}>
                {revisionDecisionLabel(revision.superAdminDecision)}
              </Tag>
              <Typography.Text>
                {revision.superAdminDecidedByName}
                {revision.superAdminDecidedByRole
                  ? ` · ${revision.superAdminDecidedByRole}`
                  : ''}{' '}
                · {formatDateTime(revision.superAdminDecidedAt, locale)}
              </Typography.Text>
              {revision.superAdminReason && (
                <div>
                  <Typography.Text type="secondary">{revision.superAdminReason}</Typography.Text>
                </div>
              )}
            </div>
          )}

          {revision.newDraftVersionNumber !== null && (
            <div style={{ marginTop: 8 }}>
              <Tag color="blue">
                {t('contracts.revision.newDraft', {
                  number: revision.newDraftVersionNumber,
                })}
              </Tag>
              {revision.newOfferVersionId !== null && (
                <Tag>{t('contracts.revision.newOfferVersion', { id: revision.newOfferVersionId })}</Tag>
              )}
            </div>
          )}
        </div>
      ),
    };
  };

  const openRevisionRequest = () => {
    setActionError(null);
    revisionForm.setFieldsValue({
      reason: '',
      landlordSharePct: contract.landlordSharePct ?? undefined,
      durationYears: contract.durationYears ?? undefined,
      startDate: contract.startDate ?? undefined,
      installationCost: contract.installationCost ?? undefined,
      solarCellCapacityKw: contract.solarCellCapacityKw ?? undefined,
      expectedAnnualRevenue: contract.expectedAnnualRevenue ?? undefined,
      effectiveCostPerKw: contract.effectiveCostPerKw ?? undefined,
      paybackPeriodMonths: contract.paybackPeriodMonths ?? undefined,
      noticePeriodDays: contract.noticePeriodDays,
    });
    setRevisionRequestOpen(true);
  };

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

  const confirmRevisionConsent = () =>
    modal.confirm({
      title: t('contracts.revision.confirmApproveTitle'),
      content: t('contracts.revision.confirmApproveBody'),
      okText: t('contracts.revision.approveRevision'),
      cancelText: t('common.cancel'),
      onOk: () => approveRevisionMutation.mutateAsync(),
    });

  const confirmAdminApproveRevision = () =>
    modal.confirm({
      title: t('contracts.revision.confirmApproveTitle'),
      content: t('contracts.revision.adminBody'),
      okText: t('contracts.revision.adminApprove'),
      cancelText: t('common.cancel'),
      onOk: () => adminRevisionMutation.mutateAsync({ decision: 'Approve' }),
    });

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to="/app/contracts" className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('contracts.backToList')}
          </Link>
          <Typography.Title level={1} style={{ margin: '8px 0 0' }}>
            {t('contracts.detailTitle')} #{contract.id}{' '}
            <ContractStatusTag status={contract.status} />
          </Typography.Title>
        </div>
        <Space wrap>
          {canSign && (
            <Button
              type="primary"
              icon={<PenLine size={16} aria-hidden="true" />}
              loading={signMutation.isPending}
              onClick={confirmSign}
            >
              {t('contracts.sign')}
            </Button>
          )}
          {canReview && (
            <Button
              type="primary"
              icon={<Check size={16} aria-hidden="true" />}
              loading={reviewMutation.isPending}
              onClick={confirmApprove}
            >
              {t('contracts.approve')}
            </Button>
          )}
          {canReview && (
            <Button
              danger
              icon={<X size={16} aria-hidden="true" />}
              onClick={() => {
                setActionError(null);
                setRejectOpen(true);
              }}
            >
              {t('contracts.reject')}
            </Button>
          )}
          {canTerminate && (
            <Button
              danger
              onClick={() => {
                setActionError(null);
                setTerminateOpen(true);
              }}
            >
              {t('contracts.terminate')}
            </Button>
          )}
        </Space>
      </div>

      {actionError && <Alert type="error" showIcon role="alert" title={actionError} />}
      {revisionInProgress && (
        <Alert
          type="warning"
          showIcon
          role="alert"
          title={t('contracts.revision.openNotice')}
        />
      )}

      <Card title={t('contracts.summary')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            {
              key: 'offer',
              label: t('contracts.list.offerId'),
              children: <Link to={`/app/offers/${contract.offerId}`}>#{contract.offerId}</Link>,
            },
            {
              key: 'version',
              label: t('trace.acceptedVersion'),
              children: contract.offerVersionId ?? '—',
            },
            ...(latestDraft
              ? [
                  {
                    key: 'currentDraft',
                    label: t('contracts.currentDraft', {
                      number: latestDraft.versionNumber,
                    }),
                    children: t('contracts.versionDraft', {
                      number: latestDraft.versionNumber,
                    }),
                  },
                ]
              : []),
            {
              key: 'criterion',
              label: t('criteria.current', { id: contract.criterionId ?? '—' }),
              children: t('trace.immutableContract'),
            },
            { key: 'land', label: t('contracts.list.land'), children: contract.landLabel || '—' },
            { key: 'investor', label: t('contracts.list.investor'), children: contract.investorName || '—' },
            { key: 'landlord', label: t('contracts.list.landlord'), children: contract.landlordName || '—' },
            {
              key: 'share',
              label: t('offers.form.landlordSharePct'),
              children:
                contract.landlordSharePct === null
                  ? '—'
                  : formatDecimal(contract.landlordSharePct, locale),
            },
            {
              key: 'duration',
              label: t('offers.form.durationYears'),
              children: contract.durationYears ?? '—',
            },
            { key: 'start', label: t('offers.form.startDate'), children: contract.startDate ?? '—' },
            {
              key: 'install',
              label: t('offers.form.installationCost'),
              children:
                contract.installationCost === null
                  ? '—'
                  : formatDecimal(contract.installationCost, locale),
            },
            {
              key: 'capacity',
              label: t('offers.form.solarCellCapacityKw'),
              children:
                contract.solarCellCapacityKw === null
                  ? '—'
                  : formatDecimal(contract.solarCellCapacityKw, locale),
            },
            {
              key: 'revenue',
              label: t('offers.form.expectedAnnualRevenue'),
              children:
                contract.expectedAnnualRevenue === null
                  ? '—'
                  : formatDecimal(contract.expectedAnnualRevenue, locale),
            },
            {
              key: 'effective',
              label: t('offers.form.effectiveCostPerKw'),
              children:
                contract.effectiveCostPerKw === null
                  ? '—'
                  : formatDecimal(contract.effectiveCostPerKw, locale),
            },
            {
              key: 'payback',
              label: t('offers.form.paybackPeriodMonths'),
              children:
                contract.paybackPeriodMonths === null
                  ? '—'
                  : formatDecimal(contract.paybackPeriodMonths, locale),
            },
            {
              key: 'notice',
              label: t('contracts.noticePeriodDays'),
              children: contract.noticePeriodDays,
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

      <Card
        title={t('contracts.revision.title')}
        extra={
          <Space wrap>
            {canRequestRevision && (
              <Button icon={<PenLine size={16} aria-hidden="true" />} onClick={openRevisionRequest}>
                {t('contracts.revision.request')}
              </Button>
            )}
            {canConsent && (
              <>
                <Button
                  type="primary"
                  icon={<Check size={16} aria-hidden="true" />}
                  loading={approveRevisionMutation.isPending}
                  onClick={confirmRevisionConsent}
                >
                  {t('contracts.revision.approveRevision')}
                </Button>
                <Button
                  danger
                  icon={<X size={16} aria-hidden="true" />}
                  onClick={() => {
                    setActionError(null);
                    setRevisionRejectOpen(true);
                  }}
                >
                  {t('contracts.revision.rejectRevision')}
                </Button>
              </>
            )}
            {canAdminDecide && (
              <>
                <Button
                  type="primary"
                  icon={<Check size={16} aria-hidden="true" />}
                  loading={adminRevisionMutation.isPending}
                  onClick={confirmAdminApproveRevision}
                >
                  {t('contracts.revision.adminApprove')}
                </Button>
                <Button
                  danger
                  onClick={() => {
                    setActionError(null);
                    setEscalateOpen(true);
                  }}
                >
                  {t('contracts.revision.adminEscalate')}
                </Button>
              </>
            )}
            {canSuperAdminDecide && (
              <>
                <Button
                  type="primary"
                  icon={<Check size={16} aria-hidden="true" />}
                  onClick={() => {
                    setActionError(null);
                    superAdminForm.resetFields();
                    setSuperAdminDecision('Continue');
                  }}
                >
                  {t('contracts.revision.superAdminContinue')}
                </Button>
                <Button
                  danger
                  icon={<X size={16} aria-hidden="true" />}
                  onClick={() => {
                    setActionError(null);
                    superAdminForm.resetFields();
                    setSuperAdminDecision('Cancel');
                  }}
                >
                  {t('contracts.revision.superAdminCancel')}
                </Button>
              </>
            )}
          </Space>
        }
      >
        <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
          {t('contracts.revision.subtitle')}
        </Typography.Paragraph>
        {revisionsQuery.isLoading ? (
          <Spin />
        ) : revisionsQuery.isError ? (
          <Alert
            type="warning"
            showIcon
            role="alert"
            title={resolveApiMessage(revisionsQuery.error, t)}
          />
        ) : revisions.length === 0 ? (
          <Typography.Text type="secondary">{t('contracts.revision.none')}</Typography.Text>
        ) : (
          <Timeline items={revisions.map(renderRevision)} />
        )}
      </Card>

      <Card title={t('contracts.reviews')}>
        {reviewsQuery.isLoading ? (
          <Spin />
        ) : reviewsQuery.isError ? (
          <Alert
            type="warning"
            showIcon
            role="alert"
            title={resolveApiMessage(reviewsQuery.error, t)}
          />
        ) : (reviewsQuery.data?.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('contracts.noReviews')}</Typography.Text>
        ) : (
          <Timeline
            items={(reviewsQuery.data ?? []).map((review) => {
              const negative =
                review.decision === 'Rejected' ||
                review.decision === 'Terminated' ||
                review.decision === 'RevisionCancelled';
              return {
                key: review.id,
                color: negative ? 'red' : review.decision === 'RevisionEscalated' ? 'orange' : 'green',
                title: (
                  <Space wrap>
                    <Tag>{translateOr(`contracts.reviewDecisions.${review.decision}`, review.decision)}</Tag>
                    <Typography.Text type="secondary">
                      {formatDateTime(review.createdAt, locale)}
                    </Typography.Text>
                  </Space>
                ),
                content: (
                  <div>
                    <div>
                      #{review.reviewerId} · {review.reviewerName}
                    </div>
                    {review.reason && (
                      <Typography.Text type="secondary">{review.reason}</Typography.Text>
                    )}
                  </div>
                ),
              };
            })}
          />
        )}
      </Card>

      <Card title={t('contracts.versions')}>
        {versionsQuery.isLoading ? (
          <Spin />
        ) : versionsQuery.isError ? (
          <Alert
            type="warning"
            showIcon
            role="alert"
            title={resolveApiMessage(versionsQuery.error, t)}
          />
        ) : versions.length === 0 ? (
          <Typography.Text type="secondary">{t('contracts.noVersionsLegacy')}</Typography.Text>
        ) : (
          <Timeline
            items={versions.map((version) => {
              const negative =
                version.eventType === 'Rejected' ||
                version.eventType === 'Terminated' ||
                version.eventType === 'RevisionCancelled' ||
                version.eventType === 'RevisionPartyRejected';
              return {
                key: version.id,
                color: version.versionKind === 'Draft' ? 'blue' : negative ? 'red' : 'green',
                title: (
                  <Space wrap>
                    <Tag>
                      {version.versionKind === 'Draft'
                        ? t('contracts.versionDraft', { number: version.versionNumber })
                        : versionEventLabel(version.eventType)}
                    </Tag>
                    <Tag>{translateOr(`contracts.status.${version.status}`, version.status)}</Tag>
                    <Typography.Text type="secondary">
                      {formatDateTime(version.createdAt, locale)}
                    </Typography.Text>
                  </Space>
                ),
                content: (
                  <div>
                    <div>
                      {version.createdById !== null ? `#${version.createdById}` : '—'}
                      {version.createdByName ? ` · ${version.createdByName}` : ''}
                      {version.createdByRole ? ` · ${version.createdByRole}` : ''}
                    </div>
                    {version.versionKind === 'Event' && version.targetVersionId !== null && (
                      <Typography.Text type="secondary">
                        {t('contracts.versionTarget', {
                          number:
                            versionsByRowId.get(version.targetVersionId) ??
                            version.targetVersionId,
                        })}
                      </Typography.Text>
                    )}
                    {version.versionKind === 'Draft' && version.previousVersionId !== null && (
                      <div>
                        <Typography.Text type="secondary">
                          {t('contracts.previousDraft', {
                            number:
                              versionsByRowId.get(version.previousVersionId) ??
                              version.previousVersionId,
                          })}
                        </Typography.Text>
                      </div>
                    )}
                    {version.reason && (
                      <Typography.Text type="secondary">{version.reason}</Typography.Text>
                    )}
                    {version.versionKind === 'Draft' && version.terms && (
                      <Descriptions
                        column={1}
                        size="small"
                        style={{ marginTop: 8 }}
                        items={termsItems(version.terms)}
                      />
                    )}
                  </div>
                ),
              };
            })}
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
        <Form<ReasonFormValues>
          form={rejectForm}
          layout="vertical"
          onFinish={(values) => rejectMutation.mutate(values)}
        >
          <Form.Item
            name="reason"
            label={t('common.reason')}
            rules={[
              { required: true, message: t('common.reasonRequired') },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
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
        <Form<ReasonFormValues>
          form={terminateForm}
          layout="vertical"
          onFinish={(values) => terminateMutation.mutate(values)}
        >
          <Form.Item
            name="reason"
            label={t('common.reason')}
            rules={[
              { required: true, message: t('common.reasonRequired') },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={revisionRequestOpen}
        title={t('contracts.revision.requestTitle')}
        okText={t('contracts.revision.submitRequest')}
        cancelText={t('common.cancel')}
        width={720}
        confirmLoading={requestRevisionMutation.isPending}
        onCancel={() => setRevisionRequestOpen(false)}
        onOk={() => revisionForm.submit()}
        destroyOnHidden
      >
        <Alert
          type="info"
          showIcon
          title={t('contracts.revision.requestBody')}
          style={{ marginBottom: 12 }}
        />
        <Form<RevisionFormValues>
          form={revisionForm}
          layout="vertical"
          onFinish={(values) => requestRevisionMutation.mutate(values)}
        >
          <Form.Item
            name="landlordSharePct"
            label={t('offers.form.landlordSharePct')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { type: 'number', min: 0.01, max: 100, message: t('offers.form.shareRange') },
            ]}
          >
            <InputNumber min={0.01} max={100} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="durationYears" label={t('offers.form.durationYears')}>
            <InputNumber min={1} max={100} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="startDate" label={t('offers.form.startDate')}>
            <Input type="date" />
          </Form.Item>
          <Form.Item name="installationCost" label={t('offers.form.installationCost')}>
            <InputNumber min={0} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="solarCellCapacityKw" label={t('offers.form.solarCellCapacityKw')}>
            <InputNumber min={0.01} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="expectedAnnualRevenue" label={t('offers.form.expectedAnnualRevenue')}>
            <InputNumber min={0} step={0.001} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="effectiveCostPerKw" label={t('offers.form.effectiveCostPerKw')}>
            <InputNumber min={0} step={0.001} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="paybackPeriodMonths" label={t('offers.form.paybackPeriodMonths')}>
            <InputNumber min={0} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="noticePeriodDays" label={t('contracts.noticePeriodDays')}>
            <InputNumber min={0} max={3650} precision={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="reason"
            label={t('contracts.revision.reason')}
            rules={[
              { required: true, message: t('contracts.revision.reasonRequired') },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={revisionRejectOpen}
        title={t('contracts.revision.rejectRevision')}
        okText={t('contracts.revision.rejectRevision')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={rejectRevisionMutation.isPending}
        onCancel={() => setRevisionRejectOpen(false)}
        onOk={() => revisionRejectForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues>
          form={revisionRejectForm}
          layout="vertical"
          onFinish={(values) => rejectRevisionMutation.mutate(values)}
        >
          <Form.Item
            name="reason"
            label={t('contracts.revision.rejectReason')}
            rules={[
              { required: true, message: t('common.reasonRequired') },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={escalateOpen}
        title={t('contracts.revision.escalateTitle')}
        okText={t('contracts.revision.adminEscalate')}
        okButtonProps={{ danger: true }}
        cancelText={t('common.cancel')}
        confirmLoading={adminRevisionMutation.isPending}
        onCancel={() => setEscalateOpen(false)}
        onOk={() => escalateForm.submit()}
        destroyOnHidden
      >
        <Form<ReasonFormValues>
          form={escalateForm}
          layout="vertical"
          onFinish={(values) =>
            adminRevisionMutation.mutate({ decision: 'Escalate', reason: values.reason })
          }
        >
          <Form.Item
            name="reason"
            label={t('contracts.revision.escalateReason')}
            rules={[
              { required: true, message: t('contracts.revision.escalateReasonRequired') },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={superAdminDecision !== null}
        title={
          superAdminDecision === 'Cancel'
            ? t('contracts.revision.cancelTitle')
            : t('contracts.revision.continueTitle')
        }
        okText={
          superAdminDecision === 'Cancel'
            ? t('contracts.revision.superAdminCancel')
            : t('contracts.revision.superAdminContinue')
        }
        okButtonProps={{ danger: superAdminDecision === 'Cancel' }}
        cancelText={t('common.cancel')}
        confirmLoading={superAdminRevisionMutation.isPending}
        onCancel={() => {
          setSuperAdminDecision(null);
          superAdminForm.resetFields();
        }}
        onOk={() => superAdminForm.submit()}
        destroyOnHidden
      >
        <Alert
          type={superAdminDecision === 'Cancel' ? 'warning' : 'info'}
          showIcon
          title={
            superAdminDecision === 'Cancel'
              ? t('contracts.revision.cancelBody')
              : t('contracts.revision.continueBody')
          }
          style={{ marginBottom: 12 }}
        />
        <Form<ReasonFormValues>
          form={superAdminForm}
          layout="vertical"
          onFinish={(values) => superAdminRevisionMutation.mutate(values)}
        >
          <Form.Item
            name="reason"
            label={
              superAdminDecision === 'Cancel'
                ? t('contracts.revision.cancelReason')
                : t('contracts.revision.escalateReason')
            }
            rules={[
              {
                required: true,
                message:
                  superAdminDecision === 'Cancel'
                    ? t('contracts.revision.cancelReasonRequired')
                    : t('common.reasonRequired'),
              },
              { max: 500, message: t('lands.form.maxLength', { max: 500 }) },
            ]}
          >
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>
    </Space>
  );
}
