import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { fetchLands } from '@/api/lands';
import { resolveApiMessage } from '@/api/knownMessages';
import { LandStatusTag } from '@/components/LandStatusTag';
import { formatDecimal } from '@/pages/lands/landFormat';
import type { LandDto } from '@/types/lands';

const PAGE_SIZE = 20;

/**
 * Investor land discovery. `GET /api/Lands` already restricts an approved
 * investor to Verified lands on the server, so no client-side status filter is
 * applied here.
 */
export function DiscoverLandsPage() {
  const { t, i18n } = useTranslation();
  const [pageNumber, setPageNumber] = useState(1);
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const landsQuery = useQuery({
    queryKey: ['discover-lands', { pageNumber, pageSize: PAGE_SIZE }],
    queryFn: () => fetchLands({ pageNumber, pageSize: PAGE_SIZE }),
  });

  const columns = useMemo<TableColumnsType<LandDto>>(
    () => [
      {
        title: t('lands.list.landNumber'),
        dataIndex: 'landNumber',
        key: 'landNumber',
        render: (_value, land) => <Link to={`/app/discover/${land.id}`}>{land.landNumber}</Link>,
      },
      {
        title: t('lands.list.governorate'),
        key: 'governorateName',
        render: (_value, land) => land.governorateName ?? '—',
      },
      {
        title: t('lands.list.region'),
        key: 'regionName',
        render: (_value, land) => land.regionName ?? '—',
      },
      {
        title: t('lands.list.location'),
        key: 'location',
        render: (_value, land) =>
          [land.village, land.basin, land.parcelNumber].filter(Boolean).join(' · ') || '—',
      },
      {
        title: t('lands.list.area'),
        dataIndex: 'areaDonum',
        key: 'areaDonum',
        render: (value: number) => formatDecimal(value, locale),
      },
      {
        title: t('lands.list.status'),
        dataIndex: 'status',
        key: 'status',
        render: (value: string) => <LandStatusTag status={value} />,
      },
      {
        title: t('lands.list.actions'),
        key: 'actions',
        render: (_value, land) => <Link to={`/app/discover/${land.id}`}>{t('common.view')}</Link>,
      },
    ],
    [t, locale],
  );

  const isForbidden =
    landsQuery.isError &&
    typeof landsQuery.error === 'object' &&
    landsQuery.error !== null &&
    'kind' in landsQuery.error &&
    (landsQuery.error as { kind?: string }).kind === 'forbidden';

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Typography.Title level={1} style={{ margin: 0 }}>
          {t('discover.title')}
        </Typography.Title>
        <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
          {t('discover.subtitle')}
        </Typography.Paragraph>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        {landsQuery.isError ? (
          <Alert
            type="warning"
            showIcon
            role="alert"
            title={
              isForbidden ? t('discover.notApprovedTitle') : resolveApiMessage(landsQuery.error, t)
            }
            description={isForbidden ? t('discover.notApprovedBody') : undefined}
            action={
              <Button size="small" onClick={() => void landsQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        ) : (
          <Table<LandDto>
            rowKey="id"
            columns={columns}
            dataSource={landsQuery.data?.items ?? []}
            loading={landsQuery.isLoading}
            locale={{ emptyText: t('discover.empty') }}
            pagination={{
              current: landsQuery.data?.pageNumber ?? pageNumber,
              pageSize: landsQuery.data?.pageSize ?? PAGE_SIZE,
              total: landsQuery.data?.totalCount ?? 0,
              showSizeChanger: false,
              onChange: (page) => setPageNumber(page),
            }}
          />
        )}
      </Card>
    </Space>
  );
}
