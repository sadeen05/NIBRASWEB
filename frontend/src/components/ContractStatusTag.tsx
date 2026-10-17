import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';

import { CONTRACT_STATUSES } from '@/types/contracts';

const COLORS: Record<string, string> = {
  PendingSignatures: 'gold',
  Active: 'green',
  Terminated: 'default',
  Rejected: 'red',
};

/** Renders a contract status (string enum) with its localized label. */
export function ContractStatusTag({ status }: { status: string }) {
  const { t } = useTranslation();
  const isKnown = (CONTRACT_STATUSES as readonly string[]).includes(status);
  const label = isKnown ? t(`contracts.status.${status}`) : status;
  return <Tag color={COLORS[status] ?? 'default'}>{label}</Tag>;
}
