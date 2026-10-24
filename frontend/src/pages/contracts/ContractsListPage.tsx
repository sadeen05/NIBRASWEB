import { useQuery } from '@tanstack/react-query';
import { Alert, Button, Card, Select, Space, Table, Typography } from 'antd';
import type { TableColumnsType } from 'antd';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';

import { fetchContracts } from '@/api/contracts';
import { resolveApiMessage } from '@/api/knownMessages';
import { ContractStatusTag } from '@/components/ContractStatusTag';
import { formatDateTime, formatDecimal } from '@/pages/lands/landFormat';
import { CONTRACT_STATUSES, type ContractDto } from '@/types/contracts';

const PAGE_SIZE = 20;

export function ContractsListPage() {
  const { t, i18n } = useTranslation();
  const [pageNumber, setPageNumber] = useState(1);
  const [status, setStatus] = useState<string>('all');
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';

  const contractsQuery = useQuery({
    queryKey: ['contracts', { pageNumber, pageSize: PAGE_SIZE, status }],
    queryFn: () =>
      fetchContracts({
        pageNumber,
        pageSize: PAGE_SIZE,
        status: status === 'all' ? undefined : status,
      }),
  });

  const columns = useMemo<TableColumnsType<ContractDto>>(
    () => [
      {
        title: t('contracts.list.contractId'),
        dataIndex: 'id',
        key: 'id',
        render: (value: number) => <Link to={`/app/contracts/${value}`}>#{value}</Link>,
      },
      {
        title: t('contracts.list.land'),
        dataIndex: 'landLabel',
        key: 'landLabel',
        render: (value: string) => value || '—',
      },
      { title: t('contracts.list.investor'), dataIndex: 'investorName', key: 'investorName' },
      { title: t('contracts.list.landlord'), dataIndex: 'landlordName', key: 'landlordName' },
      {
        title: t('contracts.list.share'),
        dataIndex: 'landlordSharePct',
        key: 'landlordSharePct',
        render: (value: number | null) => (value === null ? '—' : formatDecimal(value, locale)),
      },
      {
        title: t('contracts.list.status'),
        dataIndex: 'status',
        key: 'status',
        render: (value: string) => <ContractStatusTag status={value} />,
      },
      {
        title: t('contracts.list.createdAt'),
        dataIndex: 'createdAt',
        key: 'createdAt',
        render: (value: string | null) => formatDateTime(value, locale),
      },
      {
        title: t('lands.list.actions'),
        key: 'actions',
        render: (_value, contract) => (
          <Link to={`/app/contracts/${contract.id}`}>{t('common.view')}</Link>
        ),
      },
    ],
    [t, locale],
  );

  const statusOptions = [
    { value: 'all', label: t('contracts.list.allStatuses') },
    ...CONTRACT_STATUSES.map((value) => ({ value, label: t(`contracts.status.${value}`) })),
  ];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div>
        <Typography.Title level={1} style={{ margin: 0 }}>
          {t('contracts.title')}
        </Typography.Title>
        <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
          {t('contracts.subtitle')}
        </Typography.Paragraph>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            value={status}
            options={statusOptions}
            style={{ minWidth: 220 }}
            aria-label={t('contracts.list.filterStatus')}
            onChange={(value) => {
              setStatus(value);
              setPageNumber(1);
            }}
          />
        </Space>

        {contractsQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveApiMessage(contractsQuery.error, t)}
            action={
              <Button size="small" onClick={() => void contractsQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        ) : (
          <Table<ContractDto>
            rowKey="id"
            columns={columns}
            dataSource={contractsQuery.data?.items ?? []}
            loading={contractsQuery.isLoading}
            locale={{ emptyText: t('contracts.empty') }}
            pagination={{
              current: contractsQuery.data?.pageNumber ?? pageNumber,
              pageSize: contractsQuery.data?.pageSize ?? PAGE_SIZE,
              total: contractsQuery.data?.totalCount ?? 0,
              showSizeChanger: false,
              onChange: (page) => setPageNumber(page),
            }}
          />
        )}
      </Card>
    </Space>
  );
}
