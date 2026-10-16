import { Tag } from 'antd';
import { useTranslation } from 'react-i18next';

import { OFFER_STATUS_NAMES } from '@/types/offers';

const COLORS: Record<number, string> = {
  1: 'gold',
  2: 'gold',
  3: 'blue',
  4: 'green',
  5: 'red',
  6: 'red',
  7: 'red',
  8: 'default',
  9: 'default',
};

/** Renders an offer status (integer enum) with its localized label. */
export function OfferStatusTag({ status }: { status: number }) {
  const { t } = useTranslation();
  const name = OFFER_STATUS_NAMES[status];
  const label = name ? t(`offers.status.${name}`) : String(status);
  return <Tag color={COLORS[status] ?? 'default'}>{label}</Tag>;
}
