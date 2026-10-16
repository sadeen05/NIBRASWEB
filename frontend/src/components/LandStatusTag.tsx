import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';

import { LAND_STATUSES, type LandStatus } from '@/types/lands';

const STATUS_COLORS: Record<LandStatus, string> = {
  Draft: 'default',
  PendingVerification: 'gold',
  Verified: 'green',
  Rejected: 'red',
  UnderContract: 'blue',
};

/**
 * Renders a land status with its localized label. Unknown values fall back to
 * the raw server string so a new backend status is never hidden.
 */
export function LandStatusTag({ status }: { status: string }) {
  const { t } = useTranslation();
  const isKnown = (LAND_STATUSES as readonly string[]).includes(status);
  const color = isKnown ? STATUS_COLORS[status as LandStatus] : 'default';
  const label = isKnown ? t(`lands.status.${status}`) : status;

  return <Tag color={color}>{label}</Tag>;
}
