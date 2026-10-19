import { FileText, Search, Sprout } from 'lucide-react';
import { useTranslation } from 'react-i18next';

import { AppLogo } from '@/components/AppLogo';

/**
 * Marketing aside shared by the public auth screens (sign in / register). It is
 * kept in one place so both screens carry the identical public-surface design
 * and responsive behaviour.
 */
export function AuthAside() {
  const { t } = useTranslation();

  return (
    <section className="login-aside">
      <AppLogo />
      <h1 className="aside-title">{t('login.asideTitle')}</h1>
      <ul className="aside-points">
        <li className="aside-point">
          <Sprout className="aside-point__icon" size={18} aria-hidden="true" />
          <span>{t('login.pointRegister')}</span>
        </li>
        <li className="aside-point">
          <Search className="aside-point__icon" size={18} aria-hidden="true" />
          <span>{t('login.pointDiscover')}</span>
        </li>
        <li className="aside-point">
          <FileText className="aside-point__icon" size={18} aria-hidden="true" />
          <span>{t('login.pointContracts')}</span>
        </li>
      </ul>
    </section>
  );
}
