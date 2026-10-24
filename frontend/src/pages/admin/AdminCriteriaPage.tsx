import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Alert, App, Button, Card, Descriptions, Form, Input, InputNumber, Space, Spin, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { createCriteria, fetchCriteriaHistory, type CreateCriteriaRequest } from '@/api/criteria';
import { resolveApiMessage } from '@/api/knownMessages';
import { useAuth } from '@/auth/useAuth';
import { formatApprovalUtc } from '@/utils/approvalTime';

const fields = [
  { name: 'minAreaDonum', min: 0, max: 99999999.99 },
  { name: 'maxSlopePct', min: 0, max: 100 },
  { name: 'maxGridDistanceKm', min: 0, max: 1000000 },
  { name: 'minSolarIrradiance', min: 0, max: 12 },
  { name: 'minElevationM', min: -1000, max: 10000 },
] as const;

export function AdminCriteriaPage() {
  const { t, i18n } = useTranslation();
  const { user } = useAuth();
  const { message } = App.useApp();
  const client = useQueryClient();
  const [form] = Form.useForm<CreateCriteriaRequest>();
  const query = useQuery({ queryKey: ['criteria-history'], queryFn: fetchCriteriaHistory });
  const current = query.data?.[0];
  const mutation = useMutation({
    mutationFn: createCriteria,
    onSuccess: async () => {
      form.resetFields();
      message.success(t('criteria.saved'));
      await client.invalidateQueries({ queryKey: ['criteria-history'] });
      await client.invalidateQueries({ queryKey: ['criteria'] });
    },
  });
  const locale = i18n.resolvedLanguage ?? 'en';
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    <Typography.Title level={1}>{t('criteria.title')}</Typography.Title>
    <Alert type="info" showIcon title={t('criteria.pinned')} />
    <Button loading={query.isFetching} onClick={() => { mutation.reset(); void query.refetch(); }}>{t('common.retry')}</Button>
    {query.isLoading ? <Spin /> : query.isError ?
      <Alert type="error" showIcon title={resolveApiMessage(query.error, t)} action={
        <Button onClick={() => void query.refetch()}>{t('common.retry')}</Button>} /> :
      <>
        {user?.role === 'SuperAdmin' && <Card title={t('criteria.create')}>
          <Typography.Paragraph>{t('criteria.current', { id: current?.id ?? 0 })}</Typography.Paragraph>
          {mutation.isError && <Alert type="error" showIcon title={resolveApiMessage(mutation.error, t)} />}
          <Form key={current?.id ?? 0} form={form} layout="vertical" initialValues={{ ...current, reason: '' }}
            onFinish={(values) => mutation.mutate({
              ...Object.fromEntries(fields.map(f => [f.name, values[f.name] ?? null])) as Omit<CreateCriteriaRequest, 'reason' | 'expectedCurrentId'>,
              reason: values.reason.trim(), expectedCurrentId: current?.id ?? 0,
            })}>
            {fields.map(field => <Form.Item key={field.name} name={field.name} label={t('criteria.fields.' + field.name)}>
              <InputNumber min={field.min} max={field.max} precision={2} style={{ width: '100%' }} />
            </Form.Item>)}
            <Form.Item name="reason" label={t('common.reason')}
              rules={[{ required: true, whitespace: true, message: t('common.reasonRequired') }, { max: 500 }]}>
              <Input.TextArea maxLength={500} rows={3} />
            </Form.Item>
            <Button type="primary" htmlType="submit" loading={mutation.isPending}>{t('criteria.create')}</Button>
          </Form>
        </Card>}
        <Card title={t('criteria.history')}>
          {!query.data?.length && <Typography.Text>{t('criteria.empty')}</Typography.Text>}
          {query.data?.map(row => <Card key={row.id} size="small" style={{ marginBottom: 16 }}
            title={t('criteria.current', { id: row.id })}>
            <Descriptions column={2} size="small" items={[
              { key: 'previous', label: t('criteria.previous'), children: row.previousCriterionId ?? t('criteria.unknown') },
              { key: 'time', label: t('criteria.time'), children: formatApprovalUtc(row.updatedAt, locale) },
              { key: 'actor', label: t('criteria.actor'), children: '#' + row.updatedById + ' · ' + row.updatedByName + ' · ' + (row.updatedByRole ?? '—') },
              { key: 'reason', label: t('common.reason'), children: row.reason },
            ]} />
            {!row.actorSnapshotRecorded && <Alert type="warning" showIcon title={t('criteria.legacyActor')} />}
            <Table rowKey="field" size="small" pagination={false} dataSource={row.changes ?? []} columns={[
              { title: t('criteria.field'), dataIndex: 'field', render: (field: string) => t('criteria.fields.' + field) },
              { title: t('criteria.old'), dataIndex: 'oldValue', render: (v: number | null) => row.previousSnapshot ? (v ?? '—') : t('criteria.unknown') },
              { title: t('criteria.new'), dataIndex: 'newValue', render: (v: number | null) => v ?? '—' },
            ]} />
          </Card>)}
        </Card>
      </>}
  </Space>;
}
