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
  Select,
  Space,
  Spin,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { Check, History, Trash2, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { resolveApiMessage } from '@/api/knownMessages';
import {
  approveUser,
  rejectUserApproval,
  resendUserMail,
  createUser,
  deleteUser,
  fetchRoles,
  fetchUserApproval,
  fetchUsers,
} from '@/api/users';
import { useAuth } from '@/auth/useAuth';
import { formatDateTime } from '@/pages/lands/landFormat';
import { formatApprovalUtc } from '@/utils/approvalTime';
import {
  ROLE_NAMES,
  type UserApprovalHistoryEntryDto,
  type UserDto,
} from '@/types/users';

const PAGE_SIZE = 20;

interface CreateUserFormValues {
  fullName: string;
  email: string;
  phone: string;
  password: string;
  roleId: number;
  companyName?: string | null;
  companyRegistrationNumber?: string | null;
}

export function AdminUsersPage() {
  const { t, i18n } = useTranslation();
  const { message, modal } = AntApp.useApp();
  const { user: currentUser } = useAuth();
  const queryClient = useQueryClient();
  const [pageNumber, setPageNumber] = useState(1);
  const [role, setRole] = useState<string>('all');
  const [approval, setApproval] = useState<'all' | 'approved' | 'unapproved'>('all');
  const [createOpen, setCreateOpen] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [approvalTarget, setApprovalTarget] = useState<UserDto | null>(null);
  const [rejectTarget, setRejectTarget] = useState<UserDto | null>(null);
  const [rejectForm] = Form.useForm<{ reason: string }>();
  const [form] = Form.useForm<CreateUserFormValues>();
  const locale = i18n.resolvedLanguage ?? i18n.language ?? 'en';
  const isSuperAdmin = currentUser?.role === 'SuperAdmin';

  const usersQuery = useQuery({
    queryKey: ['users', { pageNumber, pageSize: PAGE_SIZE, role, approval }],
    queryFn: () =>
      fetchUsers({
        pageNumber,
        pageSize: PAGE_SIZE,
        role: role === 'all' ? undefined : role,
        isApproved: approval === 'all' ? undefined : approval === 'approved',
      }),
  });

  const rolesQuery = useQuery({
    queryKey: ['roles'],
    queryFn: fetchRoles,
    staleTime: 10 * 60_000,
  });

  const approvalQuery = useQuery({
    queryKey: ['user', approvalTarget?.id, 'approval'],
    queryFn: () => fetchUserApproval(approvalTarget!.id),
    enabled: approvalTarget !== null,
  });

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['users'] });
  };

  const approveMutation = useMutation({
    mutationFn: (id: number) => approveUser(id),
    onSuccess: async (_result, id) => {
      message.success(t('users.approved'));
      await Promise.all([
        refresh(),
        queryClient.invalidateQueries({ queryKey: ['user', id, 'approval'] }),
      ]);
    },
    onError: (error: unknown) => message.error(resolveApiMessage(error, t)),
  });

  const rejectMutation = useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => rejectUserApproval(id, reason),
    onSuccess: async (_result, { id }) => {
      setRejectTarget(null); rejectForm.resetFields(); message.success(t('approvalDecision.saved'));
      await Promise.all([refresh(), queryClient.invalidateQueries({ queryKey: ['user', id, 'approval'] })]);
    },
    onError: (error: unknown) => message.error(resolveApiMessage(error, t)),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: number) => deleteUser(id),
    onSuccess: async () => {
      message.success(t('users.deleted'));
      await refresh();
    },
    onError: (error: unknown) => message.error(resolveApiMessage(error, t)),
  });

  const resendMutation = useMutation({
    mutationFn: (target: UserDto) => resendUserMail(target.id, target.role === 'Admin'),
    onSuccess: () => message.success(t('mailDelivery.sent')),
    onError: (error: unknown) => message.error(resolveApiMessage(error, t)),
  });
  const createMutation = useMutation({
    mutationFn: (values: CreateUserFormValues) =>
      createUser({
        fullName: values.fullName.trim(),
        email: values.email.trim(),
        phone: values.phone.trim(),
        password: values.password,
        roleId: values.roleId,
        companyName: values.companyName?.trim() || null,
        companyRegistrationNumber: values.companyRegistrationNumber?.trim() || null,
      }),
    onSuccess: async (result) => {
      setCreateOpen(false);
      form.resetFields();
      setCreateError(null);
      if (result.invitation === 'failed' || result.verification === 'failed') message.warning(t('mailDelivery.failed'));
      else message.success(t('users.created'));
      await refresh();
    },
    onError: (error: unknown) => setCreateError(resolveApiMessage(error, t)),
  });

  const creatableRoles = (rolesQuery.data ?? []).filter(
    (entry) =>
      entry.name === 'Landlord' ||
      entry.name === 'Investor' ||
      (isSuperAdmin && entry.name === 'Admin'),
  );

  const confirmApprove = (target: UserDto) =>
    modal.confirm({
      title: t('users.confirmApproveTitle', { name: target.fullName }),
      content: t('users.confirmApproveBody'),
      okText: t('users.approve'),
      cancelText: t('common.cancel'),
      onOk: () => approveMutation.mutateAsync(target.id),
    });

  const confirmDelete = (target: UserDto) =>
    modal.confirm({
      title: t('users.confirmDeleteTitle', { name: target.fullName }),
      content: t('users.confirmDeleteBody'),
      okText: t('common.delete'),
      okButtonProps: { danger: true },
      cancelText: t('common.cancel'),
      onOk: () => deleteMutation.mutateAsync(target.id),
    });

  // The backend enforces the same eligibility. The UI shows why an approve action is blocked
  // instead of offering a button that would fail with a server error.
  const approvalBlockedReason = (target: UserDto): string | null => {
    if (target.role !== 'Investor') {
      return t('users.approval.notInvestor');
    }
    if (target.emailVerified === false) {
      return t('users.approval.emailUnverified');
    }
    if (!target.companyName?.trim() || !target.companyRegistrationNumber?.trim()) {
      return t('users.approval.companyMissing');
    }
    return null;
  };

  const historyColumns: TableColumnsType<UserApprovalHistoryEntryDto> = [
    { title: t('approvalDecision.decision'), key: 'decision', render: (_value, entry) =>
      t(entry.eventType === 'AccountApprovalRejected' ? 'approvalDecision.rejected' : 'approvalDecision.approved') },
    { title: t('users.approval.actorId'), dataIndex: 'actorId', key: 'actorId', render: (id: number | null) => id ?? '—' },
    {
      title: t('users.approval.actor'),
      key: 'actor',
      render: (_value, entry) => <Space orientation="vertical" size={0}>
        <span>{entry.actorName ?? (entry.actorId === null ? t('users.approval.systemActor') : '—')}</span>
        {!entry.actorSnapshotRecorded && <Typography.Text type="secondary">{t('criteria.legacyActor')}</Typography.Text>}
      </Space>,
    },
    {
      title: t('users.approval.actorRole'),
      key: 'actorRole',
      render: (_value, entry) => entry.actorRole ?? '—',
    },
    {
      title: t('mailDelivery.eventTime'),
      dataIndex: 'approvedAt',
      key: 'approvedAt',
      render: (value: string) => formatApprovalUtc(value, locale),
    },
    {
      title: t('users.approval.details'),
      dataIndex: 'details',
      key: 'details',
      render: (value: string | null) => value ?? '—',
    },
  ];

  const columns: TableColumnsType<UserDto> = [
    { title: t('users.list.name'), dataIndex: 'fullName', key: 'fullName' },
    { title: t('users.list.email'), dataIndex: 'email', key: 'email' },
    { title: t('users.list.phone'), dataIndex: 'phone', key: 'phone' },
    {
      title: t('users.list.role'),
      dataIndex: 'role',
      key: 'role',
      render: (value: string) => <Tag>{t(`roles.${value}`, { defaultValue: value })}</Tag>,
    },
    {
      title: t('users.list.verification'),
      key: 'verification',
      render: (_value, target) =>
        target.emailVerified === false ? (
          <Tag color="gold">{t('users.verification.unverified')}</Tag>
        ) : target.emailVerified === true ? (
          <Tag color="green">{t('users.verification.verified')}</Tag>
        ) : (
          <Tag>{t('users.verification.legacy')}</Tag>
        ),
    },
    {
      title: t('users.list.approved'),
      dataIndex: 'isApproved',
      key: 'isApproved',
      render: (value: boolean) =>
        value ? (
          <Tag color="green">{t('users.approvedYes')}</Tag>
        ) : (
          <Tag color="gold">{t('users.approvedNo')}</Tag>
        ),
    },
    {
      title: t('users.list.approvedBy'),
      key: 'approvedBy',
      render: (_value, target) =>
        target.approvedByName ? (
          <Space orientation="vertical" size={0}>
            <span>{target.approvedByName}</span>
            <Typography.Text type="secondary">
              {formatApprovalUtc(target.approvedAt ?? null, locale)}
              {target.approvedActorSnapshotRecorded === false && <span> · {t('criteria.legacyActor')}</span>}
            </Typography.Text>
          </Space>
        ) : (
          '—'
        ),
    },
    {
      title: t('users.list.company'),
      dataIndex: 'companyName',
      key: 'companyName',
      render: (value: string | null) => value ?? '—',
    },
    {
      title: t('users.list.createdAt'),
      dataIndex: 'createdAt',
      key: 'createdAt',
      render: (value: string) => formatDateTime(value, locale),
    },
    {
      title: t('lands.list.actions'),
      key: 'actions',
      render: (_value, target) => {
        const blockedReason = approvalBlockedReason(target);
        return (
          <Space wrap>
            {target.role === 'Investor' && target.id !== currentUser?.id && (
              <Tooltip title={blockedReason ?? undefined}>
                <span>
                  <Button
                    size="small"
                    type="primary"
                    icon={<Check size={14} aria-hidden="true" />}
                    disabled={blockedReason !== null}
                    onClick={() => confirmApprove(target)}
                  >
                    {t('users.approve')}
                  </Button>
                </span>
              </Tooltip>
            )}
            {target.role === 'Investor' && target.id !== currentUser?.id && <Button size="small" danger onClick={() => setRejectTarget(target)}>
              {t('approvalDecision.reject')}
            </Button>}
            {target.role === 'Investor' && (
              <Button
                size="small"
                icon={<History size={14} aria-hidden="true" />}
                onClick={() => setApprovalTarget(target)}
              >
                {t('users.approval.open')}
              </Button>
            )}
            {isSuperAdmin && target.emailVerified === false && ['Admin','Investor','Landlord'].includes(target.role) &&
              <Button size="small" loading={resendMutation.isPending && resendMutation.variables?.id === target.id}
                onClick={() => resendMutation.mutate(target)}>{t('mailDelivery.resend')}</Button>}
            {target.role !== 'SuperAdmin' && target.id !== currentUser?.id && (isSuperAdmin || ['Landlord','Investor'].includes(target.role)) && (
              <Button
                size="small"
                danger
                icon={<Trash2 size={14} aria-hidden="true" />}
                onClick={() => confirmDelete(target)}
              >
                {t('common.delete')}
              </Button>
            )}
          </Space>
        );
      },
    },
  ];

  const roleOptions = [
    { value: 'all', label: t('users.list.allRoles') },
    ...ROLE_NAMES.map((value) => ({ value, label: t(`roles.${value}`) })),
  ];

  const approvalOptions = [
    { value: 'all', label: t('users.list.allApproval') },
    { value: 'approved', label: t('users.approvedYes') },
    { value: 'unapproved', label: t('users.approvedNo') },
  ];

  return (
    <Space orientation="vertical" size="large" style={{ width: '100%' }}>
      <div className="page-header">
        <div>
          <Typography.Title level={1} style={{ margin: 0 }}>
            {t('users.title')}
          </Typography.Title>
          <Typography.Paragraph style={{ margin: '4px 0 0', color: 'var(--nb-text-secondary)' }}>
            {t('users.subtitle')}
          </Typography.Paragraph>
        </div>
        <Button
          type="primary"
          icon={<UserPlus size={16} aria-hidden="true" />}
          onClick={() => {
            setCreateError(null);
            setCreateOpen(true);
          }}
        >
          {t('users.create')}
        </Button>
      </div>

      <Card styles={{ body: { paddingTop: 16 } }}>
        <Space wrap style={{ marginBottom: 16 }}>
          <Select
            value={role}
            options={roleOptions}
            style={{ minWidth: 200 }}
            aria-label={t('users.list.filterRole')}
            onChange={(value) => {
              setRole(value);
              setPageNumber(1);
            }}
          />
          <Select
            value={approval}
            options={approvalOptions}
            style={{ minWidth: 200 }}
            aria-label={t('users.list.filterApproval')}
            onChange={(value) => {
              setApproval(value);
              setPageNumber(1);
            }}
          />
        </Space>

        {usersQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveApiMessage(usersQuery.error, t)}
            action={
              <Button size="small" onClick={() => void usersQuery.refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        ) : (
          <Table<UserDto>
            rowKey="id"
            columns={columns}
            dataSource={usersQuery.data?.items ?? []}
            loading={usersQuery.isLoading}
            locale={{ emptyText: t('users.empty') }}
            scroll={{ x: 'max-content' }}
            pagination={{
              current: usersQuery.data?.pageNumber ?? pageNumber,
              pageSize: usersQuery.data?.pageSize ?? PAGE_SIZE,
              total: usersQuery.data?.totalCount ?? 0,
              showSizeChanger: false,
              onChange: (page) => setPageNumber(page),
            }}
          />
        )}
      </Card>

      <Modal open={rejectTarget !== null} title={t('approvalDecision.reject')}
        okText={t('approvalDecision.reject')} cancelText={t('common.cancel')}
        confirmLoading={rejectMutation.isPending} onCancel={() => setRejectTarget(null)}
        onOk={() => rejectForm.submit()} destroyOnHidden>
        <Form form={rejectForm} layout="vertical" onFinish={({ reason }) => {
          if (rejectTarget) rejectMutation.mutate({ id: rejectTarget.id, reason: reason.trim() });
        }}>
          <Form.Item name="reason" label={t('common.reason')}
            rules={[{ required: true, whitespace: true, message: t('common.reasonRequired') }, { max: 500 }]}>
            <Input.TextArea rows={3} maxLength={500} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        open={createOpen}
        title={t('users.create')}
        okText={t('users.create')}
        cancelText={t('common.cancel')}
        confirmLoading={createMutation.isPending}
        onCancel={() => setCreateOpen(false)}
        onOk={() => form.submit()}
        destroyOnHidden
      >
        {createError && <Alert type="error" showIcon role="alert" title={createError} style={{ marginBottom: 12 }} />}
        <Form<CreateUserFormValues> form={form} layout="vertical" onFinish={(values) => createMutation.mutate(values)}>
          <Form.Item
            name="fullName"
            label={t('users.form.fullName')}
            rules={[{ required: true, message: t('lands.form.required') }, { max: 100, message: t('lands.form.maxLength', { max: 100 }) }]}
          >
            <Input maxLength={100} />
          </Form.Item>
          <Form.Item
            name="email"
            label={t('users.form.email')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { type: 'email', message: t('login.emailInvalid') },
              { max: 255, message: t('lands.form.maxLength', { max: 255 }) },
            ]}
          >
            <Input type="email" maxLength={255} />
          </Form.Item>
          <Form.Item
            name="phone"
            label={t('users.form.phone')}
            rules={[{ required: true, message: t('lands.form.required') }, { max: 20, message: t('lands.form.maxLength', { max: 20 }) }]}
          >
            <Input maxLength={20} />
          </Form.Item>
          <Form.Item
            name="password"
            label={t('users.form.password')}
            rules={[
              { required: true, message: t('lands.form.required') },
              { min: 10, message: t('users.form.passwordMin') },
              { max: 128, message: t('lands.form.maxLength', { max: 128 }) },
            ]}
          >
            <Input.Password maxLength={128} />
          </Form.Item>
          <Form.Item
            name="roleId"
            label={t('users.form.role')}
            rules={[{ required: true, message: t('lands.form.required') }]}
          >
            <Select
              options={creatableRoles.map((entry) => ({
                value: entry.id,
                label: t(`roles.${entry.name}`, { defaultValue: entry.name }),
              }))}
              loading={rolesQuery.isLoading}
            />
          </Form.Item>
          <Form.Item name="companyName" label={t('users.form.companyName')} rules={[{ max: 200, message: t('lands.form.maxLength', { max: 200 }) }]}>
            <Input maxLength={200} />
          </Form.Item>
          <Form.Item
            name="companyRegistrationNumber"
            label={t('users.form.companyRegistrationNumber')}
            rules={[{ max: 100, message: t('lands.form.maxLength', { max: 100 }) }]}
          >
            <Input maxLength={100} />
          </Form.Item>
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('users.form.investorCompanyHint')}
          </Typography.Paragraph>
        </Form>
      </Modal>

      <Modal
        open={approvalTarget !== null}
        title={t('users.approval.title', { name: approvalTarget?.fullName ?? '' })}
        width={800}
        footer={
          <Button onClick={() => setApprovalTarget(null)}>{t('common.close')}</Button>
        }
        onCancel={() => setApprovalTarget(null)}
        destroyOnHidden
      >
        {approvalQuery.isLoading ? (
          <Spin />
        ) : approvalQuery.isError ? (
          <Alert
            type="error"
            showIcon
            role="alert"
            title={resolveApiMessage(approvalQuery.error, t)}
          />
        ) : approvalQuery.data ? (
          <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
            <Descriptions column={2} size="small" bordered>
              <Descriptions.Item label={t('users.approval.role')}>
                {t(`roles.${approvalQuery.data.role}`, { defaultValue: approvalQuery.data.role })}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.emailVerification')}>
                {approvalQuery.data.emailVerified === false ? (
                  <Tag color="gold">{t('users.verification.unverified')}</Tag>
                ) : approvalQuery.data.emailVerified === true ? (
                  <Tag color="green">{t('users.verification.verified')}</Tag>
                ) : (
                  <Tag>{t('users.verification.legacy')}</Tag>
                )}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.companyDetails')}>
                {approvalQuery.data.companyDetailsPresent ? (
                  <Tag color="green">{t('users.approval.present')}</Tag>
                ) : (
                  <Tag color="gold">{t('users.approval.missing')}</Tag>
                )}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.eligible')}>
                {approvalQuery.data.eligibleForApproval ? (
                  <Tag color="green">{t('users.approval.eligibleYes')}</Tag>
                ) : (
                  <Tag color="gold">{t('users.approval.eligibleNo')}</Tag>
                )}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.actorId')}>
                {approvalQuery.data.approvedById ?? '—'}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.latestApprover')}>
                {approvalQuery.data.approvedByName ?? '—'}
                {approvalQuery.data.approvedByRole
                  ? ` (${approvalQuery.data.approvedByRole})`
                  : ''}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.list.approved')}>
                {t(approvalQuery.data.isApproved ? 'users.approvedYes' : 'users.approvedNo')}
              </Descriptions.Item>
              <Descriptions.Item label={t('users.approval.latestApprovedAt')}>
                {formatApprovalUtc(approvalQuery.data.approvedAt, locale)}
              </Descriptions.Item>
            </Descriptions>

            {approvalQuery.data.approvedById !== null && !approvalQuery.data.approvedActorSnapshotRecorded &&
              <Alert type="warning" showIcon title={t('criteria.legacyActor')} />}
            {!approvalQuery.data.isApproved && !approvalQuery.data.eligibleForApproval && (
              <Alert
                type="warning"
                showIcon
                title={t('users.approval.notEligibleBody')}
              />
            )}

            <div>
              <Typography.Title level={2} className="nb-section-title" style={{ marginTop: 0 }}>
                {t('users.approval.historyTitle')}
              </Typography.Title>
              <Table<UserApprovalHistoryEntryDto>
                rowKey="eventId"
                size="small"
                columns={historyColumns}
                dataSource={approvalQuery.data.history}
                pagination={false}
                locale={{ emptyText: t('users.approval.historyEmpty') }}
                scroll={{ x: 'max-content' }}
              />
            </div>
          </Space>
        ) : null}
      </Modal>
    </Space>
  );
}
