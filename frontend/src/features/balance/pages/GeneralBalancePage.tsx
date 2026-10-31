import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { usePermission } from '@/shared/auth/hooks';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { GeneralBalanceHistory } from '../components/GeneralBalanceHistory';
import { GeneralBalanceSection } from '../components/GeneralBalanceSection';
import { useGeneralSnapshots } from '../queries';

// Bilan général (décisions du 31 octobre 2026), page à part du bilan par caisse : Nouakchott en MRO + Dakar converti au
// taux du bilan, capital général, « Expliquer la variation », puis ses photos (courbe + liste). Calcul lourd : seulement sur
// cet écran. Sauvegarder et saisir le capital général : balance.manage, en ligne.

export function GeneralBalancePage() {
  const { t } = useTranslation();
  const canManage = usePermission('balance.manage');
  const online = useOnlineStatus();
  const snapshots = useGeneralSnapshots();
  const agencies = useAgencies();
  const cityOf = (currency: 'MRO' | 'XOF') => {
    const agency = agencies.data?.find((a) => a.currency === currency);
    return agency ? t(`agencies.${agency.code}`, { defaultValue: agency.name }) : currency;
  };

  return (
    <div className="mx-auto flex w-full max-w-4xl flex-col gap-4">
      {!online && <Notice tone="info">{t('balance.offline')}</Notice>}
      <GeneralBalanceSection manageable={canManage && online} online={online} nouakchottName={cityOf('MRO')} dakarName={cityOf('XOF')} />
      {snapshots.data && <GeneralBalanceHistory snapshots={snapshots.data} />}
      <Link to="/balance" className="inline-flex min-h-11 items-center gap-2 font-semibold text-fg underline">
        <Icon name="cash" />
        {t('balance.toByCaisse')}
      </Link>
    </div>
  );
}
