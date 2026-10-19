import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  App as AntApp,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { TableColumnsType } from 'antd';
import { Check, Trash2, UserPlus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { resolveApiMessage } from '@/api/knownMessages';
import { approveUser, createUser, deleteUser, fetchRoles, fetchUsers } from '@/api/users';
import { useAuth } from '@/auth/useAuth';
import { formatDateTime } from '@/pages/lands/landFormat';
import { ROLE_NAMES, type UserDto } from '@/types/users';

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

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['users'] });
  };

  const approveMutation = useMutation({
    mutationFn: (id: number) => approveUser(id),
    onSuccess: async () => {
      message.success(t('users.approved'));
      await refresh();
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
    onSuccess: async () => {
      setCreateOpen(false);
      form.resetFields();
      setCreateError(null);
      message.success(t('users.created'));
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
      render: (_value, target) => (
        <Space>
          {!target.isApproved && (
            <Button
              size="small"
              type="primary"
              icon={<Check size={14} aria-hidden="true" />}
              onClick={() => confirmApprove(target)}
            >
              {t('users.approve')}
            </Button>
          )}
          {target.role !== 'SuperAdmin' && target.id !== currentUser?.id && (
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
      ),
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
          <Typography.Title level={3} style={{ margin: 0 }}>
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
    </Space>
  );
}
