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
  InputNumber,
  Modal,
  Row,
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Timeline,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { ArrowLeft, Check, Download, HandCoins, Save, X } from 'lucide-react';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { resolveApiMessage } from '@/api/knownMessages';
import {
  assessLand,
  downloadDocument,
  fetchAssessments,
  fetchDocuments,
  fetchEligibility,
  fetchHistory,
  fetchLand,
  reviewDocument,
  rejectLand,
  verifyLand,
} from '@/api/lands';
import { fetchDocumentTypes, fetchGrids, fetchRegions } from '@/api/reference';
import { useAuth } from '@/auth/useAuth';
import { FullPageState } from '@/components/FullPageState';
import { LandStatusTag } from '@/components/LandStatusTag';
import { criterionLabelWithUnit, criterionThresholdText } from '@/pages/lands/criteriaFormat';
import { gridDisplayText } from '@/pages/lands/gridFormat';
import { formatDateTime, formatDecimal, formatFileSize } from '@/pages/lands/landFormat';
import type { AssessLandRequest, LandAssessmentDto, LandDocumentDto } from '@/types/lands';
import { saveBlob } from '@/utils/saveBlob';

const RESULT_COLORS: Record<string, string> = {
  Passed: 'green',
  Failed: 'red',
  NotAssessed: 'orange',
  NotEnforced: 'default',
};

const DOC_STATUS_COLORS: Record<string, string> = {
  Pending: 'gold',
  Approved: 'green',
  Rejected: 'red',
};

/** One line of the pre-decision readiness check. */
interface ReadinessFinding {
  key: string;
  text: string;
  /** `true` (default) when the finding blocks a verification decision. */
  blocked?: boolean;
}

/**
 * A monitored record that was reviewed at least once and then invalidated (the land
 * data or its assessment changed afterwards) is Pending *with* a review timestamp.
 * That combination only ever means "re-review required", because approving or
 * rejecting a document always sets the final status in the same write.
 */
function needsReReview(document: LandDocumentDto): boolean {
  return document.status === 'Pending' && document.reviewedAt !== null;
}

function ReadinessLine({ blocked, text }: { blocked: boolean; text: string }) {
  return (
    <Space size="small" align="start">
      {blocked ? (
        <X size={14} aria-hidden="true" color="#cf1322" />
      ) : (
        <Check size={14} aria-hidden="true" color="#389e0d" />
      )}
      <Typography.Text>{text}</Typography.Text>
    </Space>
  );
}

interface ReasonFormValues {
  reason: string;
}

interface NoteFormValues {
  note: string;
}

interface AssessmentFormValues {
  regionId?: number;
  gridId?: number;
  slopePercentage?: number;
  distanceToGridKm?: number;
  solarIrradiance?: number;
  elevationM?: number;
  note?: string;
}

export function AdminLandReviewPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { user } = useAuth();
  const { id: idParam } = useParams();
  const queryClient = useQueryClient();
  const [rejectForm] = Form.useForm<ReasonFormValues>();
  const [noteForm] = Form.useForm<NoteFormValues>();
  const [assessmentForm] = Form.useForm<AssessmentFormValues>();
  const [rejectOpen, setRejectOpen] = useState(false);
  const [rejectDocId, setRejectDocId] = useState<number | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const isArabic = locale.startsWith('ar');
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
  const assessmentsQuery = useQuery({
    queryKey: ['land', id, 'assessments'],
    queryFn: () => fetchAssessments(id),
    enabled: validId,
  });
  const regionsQuery = useQuery({
    queryKey: ['reference', 'regions'],
    queryFn: fetchRegions,
    staleTime: 5 * 60_000,
  });
  const gridsQuery = useQuery({
    queryKey: ['reference', 'grids'],
    queryFn: fetchGrids,
    staleTime: 5 * 60_000,
  });
  // Required document types drive the readiness check, so the admin does not have
  // to deduce which evidence is still missing.
  const typesQuery = useQuery({
    queryKey: ['reference', 'documentTypes'],
    queryFn: fetchDocumentTypes,
    staleTime: 5 * 60_000,
  });

  const selectedRegionId = Form.useWatch('regionId', assessmentForm);

  const regionOptions = useMemo(
    () =>
      (regionsQuery.data ?? []).map((region) => ({
        value: region.id,
        label: isArabic ? region.nameAr : region.nameEn,
      })),
    [regionsQuery.data, isArabic],
  );

  const assessmentGridOptions = useMemo(
    () =>
      (gridsQuery.data ?? [])
        .filter((grid) => grid.regionId === selectedRegionId)
        .map((grid) => ({ value: grid.id, label: grid.name })),
    [gridsQuery.data, selectedRegionId],
  );

  // The land and the assessment snapshots store the grid as an id; the reference list
  // carries its name, so the name is shown whenever the list has one.
  const gridText = useCallback(
    (gridId: number | null | undefined) => gridDisplayText(gridsQuery.data, gridId) ?? '—',
    [gridsQuery.data],
  );

  // The assessment snapshot stores the region id as well; the region reference list carries
  // its localized name, and the id stays visible only when the list has no entry for it.
  const regionText = useCallback(
    (regionId: number | null | undefined) => {
      if (regionId === null || regionId === undefined) {
        return '—';
      }
      const region = (regionsQuery.data ?? []).find((entry) => entry.id === regionId);
      if (!region) {
        return `#${regionId}`;
      }
      return isArabic ? region.nameAr : region.nameEn;
    },
    [regionsQuery.data, isArabic],
  );

  useEffect(() => {
    const current = landQuery.data;
    if (!current) return;
    assessmentForm.setFieldsValue({
      regionId: current.regionId ?? undefined,
      gridId: current.gridId ?? undefined,
      slopePercentage: current.slopePercentage ?? undefined,
      distanceToGridKm: current.distanceToGridKm ?? undefined,
      solarIrradiance: current.solarIrradiance ?? undefined,
      elevationM: current.elevationM ?? undefined,
      note: '',
    });
  }, [landQuery.data, assessmentForm]);

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

  const downloadMutation = useMutation({
    mutationFn: (document: LandDocumentDto) => downloadDocument(id, document.id),
    onSuccess: (blob, document) => {
      saveBlob(blob, document.originalFileName ?? 'document');
    },
    onError: onActionError,
  });

  const assessmentMutation = useMutation({
    mutationFn: (values: AssessmentFormValues) => {
      const note = values.note?.trim() ?? '';
      const payload: AssessLandRequest = {
        regionId: values.regionId ?? null,
        gridId: values.gridId ?? null,
        slopePercentage: values.slopePercentage ?? null,
        distanceToGridKm: values.distanceToGridKm ?? null,
        solarIrradiance: values.solarIrradiance ?? null,
        elevationM: values.elevationM ?? null,
        note: note.length > 0 ? note : null,
      };
      return assessLand(id, payload);
    },
    onSuccess: async () => {
      setActionError(null);
      message.success(t('admin.assessmentSaved'));
      await queryClient.invalidateQueries({ queryKey: ['land', id] });
      await queryClient.invalidateQueries({ queryKey: ['land', id, 'eligibility'] });
      await queryClient.invalidateQueries({ queryKey: ['land', id, 'assessments'] });
    },
    onError: onActionError,
  });

  const onAssessmentFinish = (values: AssessmentFormValues) => {
    const hasRegion = values.regionId !== undefined && values.regionId !== null;
    const hasGrid = values.gridId !== undefined && values.gridId !== null;
    if (hasRegion !== hasGrid) {
      setActionError(t('admin.assessmentRegionGridPair'));
      return;
    }
    setActionError(null);
    assessmentMutation.mutate(values);
  };

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

  const isSuperAdmin = user?.role === 'SuperAdmin';
  // A first submission is decided by Admin or SuperAdmin; a land corrected and
  // resubmitted after a rejection is decided by a SuperAdmin only, mirroring the
  // service-level authority check (the backend re-checks every decision).
  const canDecide =
    land.status === 'PendingVerification' ||
    (land.status === 'PendingSuperAdminReview' && isSuperAdmin);
  // Assessment mirrors the service rules: a verified land's evidence basis is frozen, and
  // land data (region, grid, technical measurements) may only be changed by a SuperAdmin,
  // so a regular Admin never gets the assessment form.
  const assessable = land.status !== 'Verified' && land.status !== 'UnderContract';
  const canAssess = assessable && isSuperAdmin;

  // ---------------------------------------------------------------------------
  // Automated validation, grouped exactly as the server applies it:
  //   1 invalid/incomplete input  2 failed eligibility criteria
  //   3 missing measurements      4 document evidence
  // It is guidance for the reviewer, never a substitute for the decision: the
  // server re-checks all four when the decision is submitted. A measurement that
  // has not been assessed is reported as missing, never as zero, and an uploaded
  // file is never counted as correct merely because it exists.
  // ---------------------------------------------------------------------------
  const requiredDocumentTypes = (typesQuery.data ?? []).filter(
    (type) => type.isRequiredForVerification,
  );
  // Loading and failure are different situations and must read differently: while the
  // reference list is still in flight there is nothing to report yet, so the section
  // shows a neutral spinner instead of claiming the evidence could not be checked.
  // Both states still count as blockers, because the evidence cannot be confirmed until
  // the required document types are known.
  const documentTypesLoading = typesQuery.isLoading;
  const documentTypesUnavailable = typesQuery.isError;
  const documentTypesUnconfirmed = documentTypesLoading || documentTypesUnavailable;

  const latestDocumentByType = new Map<number, LandDocumentDto>();
  for (const document of documentsQuery.data ?? []) {
    const current = latestDocumentByType.get(document.documentTypeId);
    if (!current || document.version > current.version) {
      latestDocumentByType.set(document.documentTypeId, document);
    }
  }

  // A document may be reviewed while the land is not yet approved. On an approved land it may
  // be re-reviewed only while the land's required evidence is outstanding (an approved change
  // request invalidated an approval, a SuperAdmin rejected a required document and the owner
  // uploaded a corrected version, or a required type has no uploaded version at all) and only for
  // the latest version of its type; that re-review belongs to the SuperAdmin, who owns the
  // approved-land workflow. This mirrors the service-level evidence rule exactly; the server
  // re-checks every review.
  const outstandingRequiredEvidence = requiredDocumentTypes.some((type) => {
    const latest = latestDocumentByType.get(type.id);
    return !latest || latest.status !== 'Approved';
  });
  const isLatestVersion = (document: LandDocumentDto): boolean =>
    latestDocumentByType.get(document.documentTypeId)?.id === document.id;
  const canReviewDocument = (document: LandDocumentDto): boolean =>
    land.status === 'Verified' || land.status === 'UnderContract'
      ? isSuperAdmin && outstandingRequiredEvidence && isLatestVersion(document)
      : !(land.status === 'PendingSuperAdminReview' && !isSuperAdmin);

  const missingDataFields = [
    !land.village ? t('lands.form.village') : null,
    !land.basin ? t('lands.form.basin') : null,
    !land.parcelNumber ? t('lands.form.parcelNumber') : null,
  ].filter((value): value is string => value !== null);

  const dataFindings: ReadinessFinding[] =
    missingDataFields.length > 0
      ? [
          {
            key: 'data',
            text: t('admin.readinessDataMissing', { fields: missingDataFields.join(', ') }),
          },
        ]
      : [];

  const eligibilityResults = eligibilityQuery.data?.results ?? [];

  const failedFindings: ReadinessFinding[] = eligibilityResults
    .filter((result) => result.result === 'Failed')
    .map((result) => ({
      key: `failed-${result.criterion}`,
      text: t('admin.readinessEligibilityFailed', {
        criterion: criterionLabelWithUnit(t, result.criterion),
        actual: formatDecimal(result.actual, locale),
        requirement: criterionThresholdText(t, locale, result.criterion, result.required),
      }),
    }));

  const unassessedCriteria = eligibilityResults.filter((result) => result.result === 'NotAssessed');
  const measurementFindings: ReadinessFinding[] = [];
  if (!land.regionId || !land.gridId) {
    measurementFindings.push({ key: 'region-grid', text: t('admin.readinessRegionGridMissing') });
  }
  if (unassessedCriteria.length > 0) {
    measurementFindings.push({
      key: 'unassessed',
      text: t('admin.readinessAssessmentMissing', {
        criteria: unassessedCriteria
          .map((result) => criterionLabelWithUnit(t, result.criterion))
          .join(', '),
      }),
    });
  }

  const documentFindings: ReadinessFinding[] = requiredDocumentTypes.map((type) => {
    const latest = latestDocumentByType.get(type.id);
    if (!latest) {
      return {
        key: `doc-${type.id}`,
        blocked: true,
        text: t('admin.readinessDocMissing', { type: type.name }),
      };
    }
    if (latest.status === 'Approved') {
      return {
        key: `doc-${type.id}`,
        blocked: false,
        text: t('admin.readinessDocApproved', { type: type.name, version: latest.version }),
      };
    }
    if (latest.status === 'Rejected') {
      return {
        key: `doc-${type.id}`,
        blocked: true,
        text: t('admin.readinessDocRejected', { type: type.name, version: latest.version }),
      };
    }
    return {
      key: `doc-${type.id}`,
      blocked: true,
      text: needsReReview(latest)
        ? t('admin.readinessDocReReview', { type: type.name, version: latest.version })
        : t('admin.readinessDocPending', { type: type.name, version: latest.version }),
    };
  });

  const blockersWithoutApprovedDocument = requiredDocumentTypes.filter(
    (type) => latestDocumentByType.get(type.id)?.status !== 'Approved',
  ).length;
  const noRequiredDocumentTypes = !documentTypesUnconfirmed && requiredDocumentTypes.length === 0;

  const blockerCount =
    dataFindings.length +
    failedFindings.length +
    measurementFindings.length +
    blockersWithoutApprovedDocument +
    (noRequiredDocumentTypes ? 1 : 0) +
    (documentTypesUnconfirmed ? 1 : 0) +
    (land.status === 'PendingVerification' || land.status === 'PendingSuperAdminReview' ? 0 : 1);

  const readyForDecision = blockerCount === 0;

  const documentColumns: TableColumnsType<LandDocumentDto> = [
    { title: t('lands.documents.documentType'), dataIndex: 'documentTypeName', key: 'documentTypeName' },
    {
      title: t('lands.documents.fileName'),
      key: 'fileName',
      render: (_value, document) =>
        document.originalFileName ? (
          <Space orientation="vertical" size={0}>
            <span>{document.originalFileName}</span>
            <Typography.Text type="secondary">
              {document.contentType ?? '—'} · {formatFileSize(document.fileSizeBytes)}
            </Typography.Text>
          </Space>
        ) : (
          <Typography.Text type="secondary">{t('lands.documents.legacyRow')}</Typography.Text>
        ),
    },
    { title: t('lands.documents.version'), dataIndex: 'version', key: 'version' },
    {
      title: t('lands.documents.status'),
      dataIndex: 'status',
      key: 'status',
      render: (value: string, document) => (
        <Space size="small" wrap>
          <Tag color={DOC_STATUS_COLORS[value] ?? 'default'}>
            {t(`lands.documents.status${value}`, { defaultValue: value })}
          </Tag>
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
      // The decision record kept on the row: who reviewed it, when, and the reason
      // that was written at the time (required when rejecting).
      title: t('lands.documents.lastReview'),
      key: 'lastReview',
      render: (_value, document) =>
        document.reviewedAt ? (
          <Space orientation="vertical" size={0}>
            <Typography.Text type="secondary">
              {t('lands.documents.reviewer', { id: document.reviewedById ?? '—' })} ·{' '}
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
            icon={<Download size={14} aria-hidden="true" />}
            loading={downloadMutation.isPending && downloadMutation.variables?.id === document.id}
            onClick={() => downloadMutation.mutate(document)}
          >
            {t('lands.documents.download')}
          </Button>
        ) : (
          <Typography.Text type="secondary">{t('lands.documents.notAvailable')}</Typography.Text>
        ),
    },
    {
      title: t('lands.list.actions'),
      key: 'actions',
      render: (_value, document) =>
        canReviewDocument(document) ? (
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

  const assessmentColumns: TableColumnsType<LandAssessmentDto> = [
    {
      title: t('admin.assessmentAt'),
      dataIndex: 'assessedAt',
      key: 'assessedAt',
      render: (value: string) => formatDateTime(value, locale),
    },
    { title: t('admin.assessmentBy'), dataIndex: 'assessedByName', key: 'assessedByName' },
    { title: t('lands.form.region'), key: 'region', render: (_v, a) => regionText(a.regionId) },
    { title: t('lands.form.grid'), key: 'grid', render: (_v, a) => gridText(a.gridId) },
    {
      title: t('lands.form.slopePercentage'),
      key: 'slope',
      render: (_v, a) => formatDecimal(a.slopePercentage, locale),
    },
    {
      title: t('lands.form.distanceToGridKm'),
      key: 'distance',
      render: (_v, a) => formatDecimal(a.distanceToGridKm, locale),
    },
    {
      title: t('lands.form.solarIrradiance'),
      key: 'solar',
      render: (_v, a) => formatDecimal(a.solarIrradiance, locale),
    },
    {
      title: t('lands.form.elevationM'),
      key: 'elevation',
      render: (_v, a) => formatDecimal(a.elevationM, locale),
    },
    {
      title: t('admin.assessmentNote'),
      dataIndex: 'note',
      key: 'note',
      render: (value: string | null) => value ?? '—',
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
          <Typography.Title level={1} style={{ margin: '8px 0 0' }}>
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

      {land.status === 'PendingSuperAdminReview' && (
        <Alert type="info" showIcon role="status" title={t('admin.resubmissionNotice')} />
      )}

      {/*
        A land under contract is already verified and can no longer be verified or rejected,
        so a readiness checklist for a verification decision would report a misleading
        "not ready for verification" state. The server refuses such a decision anyway.
      */}
      {land.status === 'UnderContract' ? (
        <Alert
          type="info"
          showIcon
          title={t('admin.readinessNotApplicableUnderContractTitle')}
          description={t('admin.readinessNotApplicableUnderContractBody')}
        />
      ) : (
        <Card title={t('admin.readinessTitle')}>
          <Alert
            type={readyForDecision ? 'success' : 'warning'}
            showIcon
            title={
              readyForDecision
                ? t('admin.readinessReady')
                : t('admin.readinessBlocked', { count: blockerCount })
            }
            description={t('admin.readinessDisclaimer')}
            style={{ marginBottom: 16 }}
          />

          <Space orientation="vertical" size="small" style={{ width: '100%' }}>
            <Space orientation="vertical" size={2} style={{ width: '100%' }}>
              <Typography.Text strong>{t('admin.readinessSectionData')}</Typography.Text>
              {dataFindings.length === 0 ? (
                <ReadinessLine blocked={false} text={t('admin.readinessNoBlockers')} />
              ) : (
                dataFindings.map((finding) => (
                  <ReadinessLine key={finding.key} blocked text={finding.text} />
                ))
              )}
            </Space>

            <Space orientation="vertical" size={2} style={{ width: '100%' }}>
              <Typography.Text strong>{t('admin.readinessSectionEligibility')}</Typography.Text>
              {failedFindings.length === 0 ? (
                <ReadinessLine blocked={false} text={t('admin.readinessEligibilityOk')} />
              ) : (
                failedFindings.map((finding) => (
                  <ReadinessLine key={finding.key} blocked text={finding.text} />
                ))
              )}
            </Space>

            <Space orientation="vertical" size={2} style={{ width: '100%' }}>
              <Typography.Text strong>{t('admin.readinessSectionAssessment')}</Typography.Text>
              {measurementFindings.length === 0 ? (
                <ReadinessLine blocked={false} text={t('admin.readinessAssessmentOk')} />
              ) : (
                measurementFindings.map((finding) => (
                  <ReadinessLine key={finding.key} blocked text={finding.text} />
                ))
              )}
            </Space>

            <Space orientation="vertical" size={2} style={{ width: '100%' }}>
              <Typography.Text strong>{t('admin.readinessSectionDocuments')}</Typography.Text>
              {documentTypesLoading ? (
                <Space size="small" align="start">
                  <Spin size="small" />
                  <Typography.Text type="secondary">{t('common.loading')}</Typography.Text>
                </Space>
              ) : documentTypesUnavailable ? (
                <ReadinessLine blocked text={t('admin.readinessDocumentsUnavailable')} />
              ) : noRequiredDocumentTypes ? (
                <ReadinessLine blocked text={t('admin.noRequiredDocumentTypes')} />
              ) : (
                documentFindings.map((finding) => (
                  <ReadinessLine
                    key={finding.key}
                    blocked={finding.blocked ?? true}
                    text={finding.text}
                  />
                ))
              )}
            </Space>

            {land.status !== 'PendingVerification' && land.status !== 'PendingSuperAdminReview' && (
              <ReadinessLine blocked={false} text={t('admin.readinessStatusNotPending')} />
            )}
          </Space>
        </Card>
      )}

      <Card title={t('lands.detail.sectionDetails')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'governorate', label: t('lands.form.governorate'), children: land.governorateName ?? '—' },
            { key: 'region', label: t('lands.form.region'), children: land.regionName ?? '—' },
            { key: 'registered', label: t('trace.registered'), children: formatDateTime(land.registeredAt, locale) },
            { key: 'grid', label: t('lands.form.grid'), children: gridText(land.gridId) },
            { key: 'village', label: t('lands.form.village'), children: land.village ?? '—' },
            { key: 'basin', label: t('lands.form.basin'), children: land.basin ?? '—' },
            { key: 'parcel', label: t('lands.form.parcelNumber'), children: land.parcelNumber ?? '—' },
            { key: 'area', label: t('lands.form.areaDonum'), children: formatDecimal(land.areaDonum, locale) },
            { key: 'description', label: t('lands.form.description'), children: land.description ?? '—' },
            { key: 'slope', label: t('lands.form.slopePercentage'), children: formatDecimal(land.slopePercentage, locale) },
            { key: 'distance', label: t('lands.form.distanceToGridKm'), children: formatDecimal(land.distanceToGridKm, locale) },
            { key: 'solar', label: t('lands.form.solarIrradiance'), children: formatDecimal(land.solarIrradiance, locale) },
            { key: 'elevation', label: t('lands.form.elevationM'), children: formatDecimal(land.elevationM, locale) },
          ]}
        />
      </Card>

      <Card title={t('admin.assessmentTitle')}>
        {canAssess ? (
          <>
            <Typography.Paragraph type="secondary" style={{ marginTop: 0 }}>
              {t('admin.assessmentHint')}
            </Typography.Paragraph>
            <Form<AssessmentFormValues>
              form={assessmentForm}
              layout="vertical"
              onFinish={onAssessmentFinish}
              disabled={assessmentMutation.isPending}
            >
              <Row gutter={16}>
                <Col xs={24} md={12}>
                  <Form.Item name="regionId" label={t('lands.form.region')}>
                    <Select
                      options={regionOptions}
                      loading={regionsQuery.isLoading}
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      placeholder={t('admin.assessmentRegionPlaceholder')}
                      onChange={() => assessmentForm.setFieldValue('gridId', undefined)}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item name="gridId" label={t('lands.form.grid')}>
                    <Select
                      options={assessmentGridOptions}
                      loading={gridsQuery.isLoading}
                      allowClear
                      showSearch
                      optionFilterProp="label"
                      placeholder={
                        selectedRegionId
                          ? t('lands.form.gridPlaceholder')
                          : t('admin.assessmentSelectRegionFirst')
                      }
                      disabled={!selectedRegionId}
                    />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="slopePercentage"
                    label={t('lands.form.slopePercentage')}
                    rules={[
                      {
                        type: 'number' as const,
                        min: 0,
                        max: 100,
                        message: t('lands.form.range', { min: 0, max: 100 }),
                      },
                    ]}
                  >
                    <InputNumber min={0} max={100} step={0.01} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="distanceToGridKm"
                    label={t('lands.form.distanceToGridKm')}
                    rules={[
                      {
                        type: 'number' as const,
                        min: 0,
                        max: 1000000,
                        message: t('lands.form.range', { min: 0, max: 1000000 }),
                      },
                    ]}
                  >
                    <InputNumber min={0} max={1000000} step={0.01} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="solarIrradiance"
                    label={t('lands.form.solarIrradiance')}
                    rules={[
                      {
                        type: 'number' as const,
                        min: 0,
                        max: 12,
                        message: t('lands.form.range', { min: 0, max: 12 }),
                      },
                    ]}
                  >
                    <InputNumber min={0} max={12} step={0.01} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24} md={12}>
                  <Form.Item
                    name="elevationM"
                    label={t('lands.form.elevationM')}
                    rules={[
                      {
                        type: 'number' as const,
                        min: -1000,
                        max: 10000,
                        message: t('lands.form.range', { min: -1000, max: 10000 }),
                      },
                    ]}
                  >
                    <InputNumber min={-1000} max={10000} step={1} style={{ width: '100%' }} />
                  </Form.Item>
                </Col>
                <Col xs={24}>
                  <Form.Item
                    name="note"
                    label={t('admin.assessmentNote')}
                    rules={[{ max: 500, message: t('lands.form.maxLength', { max: 500 }) }]}
                  >
                    <Input.TextArea rows={2} maxLength={500} />
                  </Form.Item>
                </Col>
              </Row>
              <Form.Item style={{ marginBottom: 0 }}>
                <Button
                  type="primary"
                  htmlType="submit"
                  loading={assessmentMutation.isPending}
                  icon={<Save size={16} aria-hidden="true" />}
                >
                  {assessmentMutation.isPending ? t('common.saving') : t('admin.assessmentSave')}
                </Button>
              </Form.Item>
            </Form>
          </>
        ) : (
          <Alert
            type="info"
            showIcon
            title={t(assessable ? 'admin.assessmentSuperAdminOnly' : 'admin.assessmentFrozen')}
          />
        )}

        <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 24 }}>
          {t('admin.assessmentHistoryTitle')}
        </Typography.Title>
        {assessmentsQuery.isLoading ? (
          <Spin />
        ) : assessmentsQuery.isError ? (
          <Alert type="warning" showIcon role="alert" title={resolveApiMessage(assessmentsQuery.error, t)} />
        ) : (assessmentsQuery.data?.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('admin.assessmentHistoryEmpty')}</Typography.Text>
        ) : (
          <Table<LandAssessmentDto>
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={assessmentsQuery.data ?? []}
            columns={assessmentColumns}
            expandable={{ expandedRowRender: (entry) => <Space orientation="vertical">
              <span>{t('criteria.current', { id: entry.criterionId ?? t('criteria.unknown') })}</span>
              <span>{t('criteria.fields.minAreaDonum')}: {entry.areaDonumSnapshot ?? '—'}</span>
              {entry.criteriaSnapshot && Object.entries(entry.criteriaSnapshot)
                .filter(([key]) => ['minAreaDonum','maxSlopePct','maxGridDistanceKm','minSolarIrradiance','minElevationM'].includes(key))
                .map(([key,value]) => <span key={key}>{t('criteria.fields.' + key)}: {value ?? '—'}</span>)}
            </Space> }}
          />
        )}
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
              label: criterionLabelWithUnit(t, result.criterion),
              children: (
                <Space wrap>
                  <Tag color={RESULT_COLORS[result.result] ?? 'default'}>
                    {t(`lands.criteriaResult.${result.result}`, { defaultValue: result.result })}
                  </Tag>
                  <span>
                    {t('lands.detail.required')}:{' '}
                    {criterionThresholdText(t, locale, result.criterion, result.required)} ·{' '}
                    {t('lands.detail.actual')}: {formatDecimal(result.actual, locale)}
                  </span>
                </Space>
              ),
            }))}
          />
        )}
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('admin.eligibilityNeverPasses')}
        </Typography.Paragraph>
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
            scroll={{ x: 'max-content' }}
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
                    <div>#{entry.changedById} · {entry.changeByName}</div>
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
