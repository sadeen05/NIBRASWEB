import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Descriptions,
  Space,
  Spin,
  Tag,
  Timeline,
  Typography,
} from 'antd';
import { ArrowLeft, CheckCircle2, HandCoins, Pencil, Send, Undo2 } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import { isApiError } from '@/api/errors';
import { fetchEligibility, fetchHistory, fetchLand, reviseLand, submitLand } from '@/api/lands';
import { FullPageState } from '@/components/FullPageState';
import { LandStatusTag } from '@/components/LandStatusTag';
import { LandDocumentsPanel } from '@/pages/lands/LandDocumentsPanel';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';

const RESULT_COLORS: Record<string, string> = {
  Passed: 'green',
  Failed: 'red',
  NotEnforced: 'default',
};

export function LandDetailPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { id: idParam } = useParams();
  const queryClient = useQueryClient();
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const id = Number(idParam);
  const validId = Number.isInteger(id) && id > 0;

  const landQuery = useQuery({
    queryKey: ['land', id],
    queryFn: () => fetchLand(id),
    enabled: validId,
  });

  const eligibilityQuery = useQuery({
    queryKey: ['land', id, 'eligibility'],
    queryFn: () => fetchEligibility(id),
    enabled: validId,
  });

  const historyQuery = useQuery({
    queryKey: ['land', id, 'history'],
    queryFn: () => fetchHistory(id),
    enabled: validId,
  });

  const refreshLand = async () => {
    await queryClient.invalidateQueries({ queryKey: ['lands'] });
    await queryClient.invalidateQueries({ queryKey: ['land', id] });
  };

  const submitMutation = useMutation({
    mutationFn: () => submitLand(id),
    onSuccess: async (data) => {
      message.success(data?.message || t('lands.actions.submitted'));
      await refreshLand();
    },
    onError: (error: unknown) => {
      message.error(resolveLandErrorMessage(error, t));
    },
  });

  const reviseMutation = useMutation({
    mutationFn: () => reviseLand(id),
    onSuccess: async (data) => {
      message.success(data?.message || t('lands.actions.revised'));
      await refreshLand();
    },
    onError: (error: unknown) => {
      message.error(resolveLandErrorMessage(error, t));
    },
  });

  const criterionLabel = useMemo(
    () => (criterion: string) => {
      const key = `lands.criteria.${criterion}`;
      const translated = t(key);
      return translated === key ? criterion : translated;
    },
    [t],
  );

  if (!validId) {
    return (
      <FullPageState
        variant="info"
        code="404"
        title={t('lands.detail.notFoundTitle')}
        description={t('lands.detail.notFoundBody')}
        action={
          <Link to="/app/lands">
            <Button type="primary">{t('lands.backToList')}</Button>
          </Link>
        }
      />
    );
  }

  if (landQuery.isLoading) {
    return (
      <FullPageState
        variant="loading"
        title={t('common.loading')}
        description={t('lands.detail.loading')}
      />
    );
  }

  if (landQuery.isError) {
    const status = isApiError(landQuery.error) ? landQuery.error.status : null;
    if (status === 403) {
      return (
        <FullPageState
          variant="info"
          code="403"
          title={t('errors.forbiddenTitle')}
          description={t('lands.detail.forbiddenBody')}
          action={
            <Link to="/app/lands">
              <Button type="primary">{t('lands.backToList')}</Button>
            </Link>
          }
        />
      );
    }
    if (status === 404) {
      return (
        <FullPageState
          variant="info"
          code="404"
          title={t('lands.detail.notFoundTitle')}
          description={t('lands.detail.notFoundBody')}
          action={
            <Link to="/app/lands">
              <Button type="primary">{t('lands.backToList')}</Button>
            </Link>
          }
        />
      );
    }
    return (
      <Alert
        type="error"
        showIcon
        role="alert"
        title={resolveLandErrorMessage(landQuery.error, t)}
        action={
          <Button size="small" onClick={() => void landQuery.refetch()}>
            {t('common.retry')}
          </Button>
        }
      />
    );
  }

  const land = landQuery.data;
  if (!land) {
    return null;
  }

  const canEdit = land.status === 'Draft' || land.status === 'Rejected';
  const canSubmit = land.status === 'Draft';
  const canRevise = land.status === 'Rejected';

  const history = [...(historyQuery.data ?? [])].sort(
    (a, b) => Date.parse(a.changedAt) - Date.parse(b.changedAt),
  );

  const confirmSubmit = () => {
    modal.confirm({
      title: t('lands.actions.submitConfirmTitle'),
      content: t('lands.actions.submitConfirmBody'),
      okText: t('lands.actions.submit'),
      cancelText: t('common.cancel'),
      onOk: () => submitMutation.mutateAsync(),
    });
  };

  const confirmRevise = () => {
    modal.confirm({
      title: t('lands.actions.reviseConfirmTitle'),
      content: t('lands.actions.reviseConfirmBody'),
      okText: t('lands.actions.revise'),
      cancelText: t('common.cancel'),
      onOk: () => reviseMutation.mutateAsync(),
    });
  };

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Link to="/app/lands" className="back-link">
            <ArrowLeft size={16} aria-hidden="true" />
            {t('lands.backToList')}
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
            <Button icon={<HandCoins size={16} aria-hidden="true" />}>
              {t('offers.landOffersTitle')}
            </Button>
          </Link>
          {canEdit && (
            <Link to={`/app/lands/${land.id}/edit`}>
              <Button icon={<Pencil size={16} aria-hidden="true" />}>{t('common.edit')}</Button>
            </Link>
          )}
          {canSubmit && (
            <Button
              type="primary"
              icon={<Send size={16} aria-hidden="true" />}
              loading={submitMutation.isPending}
              onClick={confirmSubmit}
            >
              {t('lands.actions.submit')}
            </Button>
          )}
          {canRevise && (
            <Button
              icon={<Undo2 size={16} aria-hidden="true" />}
              loading={reviseMutation.isPending}
              onClick={confirmRevise}
            >
              {t('lands.actions.revise')}
            </Button>
          )}
        </Space>
      </div>

      <Card title={t('lands.detail.sectionDetails')}>
        <Descriptions
          column={{ xs: 1, sm: 1, md: 2 }}
          size="small"
          bordered
          items={[
            { key: 'landNumber', label: t('lands.form.landNumber'), children: land.landNumber },
            { key: 'region', label: t('lands.form.region'), children: land.regionName },
            { key: 'grid', label: t('lands.form.grid'), children: land.gridId },
            { key: 'village', label: t('lands.form.village'), children: land.village ?? '—' },
            { key: 'basin', label: t('lands.form.basin'), children: land.basin ?? '—' },
            {
              key: 'parcelNumber',
              label: t('lands.form.parcelNumber'),
              children: land.parcelNumber ?? '—',
            },
            {
              key: 'areaDonum',
              label: t('lands.form.areaDonum'),
              children: formatDecimal(land.areaDonum, locale),
            },
            {
              key: 'slopePercentage',
              label: t('lands.form.slopePercentage'),
              children: formatDecimal(land.slopePercentage, locale),
            },
            {
              key: 'distanceToGridKm',
              label: t('lands.form.distanceToGridKm'),
              children: formatDecimal(land.distanceToGridKm, locale),
            },
            {
              key: 'solarIrradiance',
              label: t('lands.form.solarIrradiance'),
              children: formatDecimal(land.solarIrradiance, locale),
            },
            {
              key: 'elevationM',
              label: t('lands.form.elevationM'),
              children: formatDecimal(land.elevationM, locale),
            },
            {
              key: 'dataVerified',
              label: t('lands.detail.dataVerified'),
              children: land.dataVerifiedByAdmin ? (
                <Tag color="green" icon={<CheckCircle2 size={14} aria-hidden="true" />}>
                  {t('lands.detail.verified')}
                </Tag>
              ) : (
                <Tag>{t('lands.detail.notVerified')}</Tag>
              ),
            },
            {
              key: 'criterion',
              label: t('lands.detail.verifiedAgainst'),
              children: land.verifiedAgainstCriterionId ?? '—',
            },
          ]}
        />
      </Card>

      <Card title={t('lands.detail.sectionEligibility')}>
        {eligibilityQuery.isLoading ? (
          <Spin />
        ) : eligibilityQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveLandErrorMessage(eligibilityQuery.error, t)}
          />
        ) : (eligibilityQuery.data?.results.length ?? 0) === 0 ? (
          <Typography.Text type="secondary">{t('lands.detail.eligibilityEmpty')}</Typography.Text>
        ) : (
          <Descriptions
            column={1}
            size="small"
            bordered
            items={(eligibilityQuery.data?.results ?? []).map((result) => ({
              key: result.criterion,
              label: criterionLabel(result.criterion),
              children: (
                <Space wrap>
                  <Tag color={RESULT_COLORS[result.result] ?? 'default'}>
                    {t(`lands.criteriaResult.${result.result}`, { defaultValue: result.result })}
                  </Tag>
                  <span>
                    {t('lands.detail.required')}: {result.required ?? '—'} ·{' '}
                    {t('lands.detail.actual')}: {formatDecimal(result.actual, locale)}
                  </span>
                </Space>
              ),
            }))}
          />
        )}
      </Card>

      <Card title={t('lands.detail.sectionHistory')}>
        {historyQuery.isLoading ? (
          <Spin />
        ) : historyQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveLandErrorMessage(historyQuery.error, t)}
          />
        ) : history.length === 0 ? (
          <Typography.Text type="secondary">{t('lands.detail.historyEmpty')}</Typography.Text>
        ) : (
          <Timeline
            items={history.map((entry) => ({
              key: entry.id,
              color: entry.statusName === 'Rejected' ? 'red' : 'green',
              title: (
                <Space wrap>
                  <LandStatusTag status={entry.statusName} />
                  <Typography.Text type="secondary">
                    {formatDateTime(entry.changedAt, locale)}
                  </Typography.Text>
                </Space>
              ),
              content: (
                <div>
                  <div>{entry.changeByName}</div>
                  {entry.reason && (
                    <Typography.Text type="secondary">{entry.reason}</Typography.Text>
                  )}
                </div>
              ),
            }))}
          />
        )}
      </Card>

      <Card title={t('lands.detail.sectionDocuments')}>
        <LandDocumentsPanel landId={land.id} status={land.status} />
      </Card>
    </Space>
  );
}
