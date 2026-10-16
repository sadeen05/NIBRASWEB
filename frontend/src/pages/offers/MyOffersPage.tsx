import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Select, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { resolveApiMessage } from '@/api/knownMessages';
import { fetchMyOffers } from '@/api/offers';
import { OfferStatusTag } from '@/components/OfferStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import { OFFER_STATUS_NAMES, type OfferDto } from '@/types/offers';

const STATUS_VALUES = Object.keys(OFFER_STATUS_NAMES).map((key) => Number(key));

export function MyOffersPage() {
  const { t, i18n } = useTranslation();
  const [statusFilter, setStatusFilter] = useState<string>('all');
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const offersQuery = useQuery({
    queryKey: ['my-offers', statusFilter],
    queryFn: () => fetchMyOffers(statusFilter === 'all' ? undefined : statusFilter),
  });

  const columns = useMemo<TableColumnsType<OfferDto>>(
    () => [
      {
        title: t('offers.list.offerId'),
        dataIndex: 'id',
        key: 'id',
        render: (value: number) => <Link to={`/app/offers/${value}`}>#{value}</Link>,
      },
      { title: t('offers.list.landId'), dataIndex: 'landId', key: 'landId' },
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
        render: (_value, offer) => <Link to={`/app/offers/${offer.id}`}>{t('common.view')}</Link>,
      },
    ],
    [t, locale],
  );

  const statusOptions = [
    { value: 'all', label: t('offers.list.allStatuses') },
    ...STATUS_VALUES.map((value) => ({
      value: OFFER_STATUS_NAMES[value],
      label: t(`offers.status.${OFFER_STATUS_NAMES[value]}`),
    })),
  ];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {t('offers.myTitle')}
        </Typography.Title>
        <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
          {t('offers.mySubtitle')}
        </Typography.Paragraph>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            value={statusFilter}
            options={statusOptions}
            style={{ minWidth: 220 }}
            aria-label={t('offers.list.filterStatus')}
            onChange={(value) => setStatusFilter(value)}
          />
        </Space>

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
            locale={{ emptyText: t('offers.empty') }}
            pagination={false}
          />
        )}
      </Card>
    </Space>
  );
}
