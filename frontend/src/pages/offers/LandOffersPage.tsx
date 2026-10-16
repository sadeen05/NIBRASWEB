import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { ArrowLeft } from 'lucide-react';
import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';

import { resolveApiMessage } from '@/api/knownMessages';
import { fetchOffersForLand } from '@/api/offers';
import { useAuth } from '@/auth/useAuth';
import { OfferStatusTag } from '@/components/OfferStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import type { OfferDto } from '@/types/offers';

/** Offers received on one land. Land owner or admin only (server-enforced). */
export function LandOffersPage() {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { id: idParam } = useParams();
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const landId = Number(idParam);
  const validId = Number.isInteger(landId) && landId > 0;

  // /app/lands/{id} is landlord-only, so an admin returns to the admin review page.
  const backTarget =
    user?.role === 'Admin' || user?.role === 'SuperAdmin'
      ? `/app/admin/lands/${landId}`
      : `/app/lands/${landId}`;

  const offersQuery = useQuery({
    queryKey: ['land-offers', landId],
    queryFn: () => fetchOffersForLand(landId),
    enabled: validId,
  });

  const columns = useMemo<TableColumnsType<OfferDto>>(
    () => [
      {
        title: t('offers.list.offerId'),
        dataIndex: 'id',
        key: 'id',
        render: (value: number) => <Link to={`/app/offers/${value}`}>#{value}</Link>,
      },
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
        render: (_value, offer) => <Link to={`/app/offers/${offer.id}`}>{t('common.view')}</Link>,
      },
    ],
    [t, locale],
  );

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Link to={backTarget} className="back-link">
          <ArrowLeft size={16} aria-hidden="true" />
          {t('offers.backToLand')}
        </Link>
        <Typography.Title level={3} style={{ margin: '8px 0 0' }}>
          {t('offers.landOffersTitle')}
        </Typography.Title>
      </div>

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
            locale={{ emptyText: t('offers.emptyForLand') }}
            pagination={false}
          />
        )}
      </Card>
    </Space>
  );
}
