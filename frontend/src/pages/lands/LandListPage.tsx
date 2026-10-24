import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Select, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { Plus } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate } from 'react-router-dom';

import { fetchLands } from '@/api/lands';
import { LandStatusTag } from '@/components/LandStatusTag';
import { resolveLandErrorMessage } from '@/pages/lands/landMessages';
import { LAND_STATUSES, type LandDto, type LandStatus } from '@/types/lands';

const PAGE_SIZE = 20;

export function LandListPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [pageNumber, setPageNumber] = useState(1);
  const [status, setStatus] = useState<LandStatus | 'all'>('all');

  const landsQuery = useQuery({
    queryKey: ['lands', { pageNumber, pageSize: PAGE_SIZE, status }],
    queryFn: () =>
      fetchLands({
        pageNumber,
        pageSize: PAGE_SIZE,
        status: status === 'all' ? undefined : status,
      }),
  });

  const columns = useMemo<TableColumnsType<LandDto>>(
    () => [
      {
        title: t('lands.list.landNumber'),
        dataIndex: 'landNumber',
        key: 'landNumber',
        render: (_value, land) => <Link to={`/app/lands/${land.id}`}>{land.landNumber}</Link>,
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
        render: (_value, land) => (
          <Link to={`/app/lands/${land.id}`}>{t('common.view')}</Link>
        ),
      },
    ],
    [t],
  );

  const statusOptions = useMemo(
    () => [
      { value: 'all', label: t('lands.list.allStatuses') },
      ...LAND_STATUSES.map((value) => ({ value, label: t(`lands.status.${value}`) })),
    ],
    [t],
  );

  const lands = landsQuery.data?.items ?? [];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Typography.Title level={1} style={{ margin: 0 }}>
            {t('lands.title')}
          </Typography.Title>
          <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
            {t('lands.subtitle')}
          </Typography.Paragraph>
        </div>
        <Button
          type="primary"
          icon={<Plus size={16} aria-hidden="true" />}
          onClick={() => navigate('/app/lands/new')}
        >
          {t('lands.newLand')}
        </Button>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select<LandStatus | 'all'>
            value={status}
            options={statusOptions}
            style={{ minWidth: 200 }}
            aria-label={t('lands.list.filterStatus')}
            onChange={(value) => {
              setStatus(value);
              setPageNumber(1);
            }}
          />
        </Space>

        {landsQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveLandErrorMessage(landsQuery.error, t)}
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
            dataSource={lands}
            loading={landsQuery.isLoading}
            locale={{ emptyText: t('lands.list.empty') }}
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
