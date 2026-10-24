import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { scopedAgencyId, useAgencyScope, type AgencyRef } from '@/shared/agency/agencyScope';
import { formatAmount } from '@/shared/format/money';
import { AgencyBadge } from '@/shared/ui/AgencyBadge';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { CreateAccountSheet } from '../components/CreateAccountSheet';
import { TreasuryAccountCard } from '../components/TreasuryAccountCard';
import { useTreasuryAccounts } from '../queries';
import { agencyTreasury, type AgencyTreasury } from '../treasuryGroups';
import type { TreasuryAccount } from '../types';

// Écran « Comptes de trésorerie » (maquette 14). Tout compte connecté voit les comptes de son agence (les deux
// pour le Superviseur sur « les deux agences »), désactivés compris ; les soldes et le total seulement avec
// cash.budget.read (le serveur renvoie null sinon). Avec treasury.manage : créer, renommer, désactiver,
// réactiver ; hors ligne, ces actions sont remplacées par une explication. Chaque carte ouvre la fiche du compte
// (relevé et actions, maquette 14).

function AccountGroup({ title, accounts }: { title: string; accounts: TreasuryAccount[] }) {
  if (accounts.length === 0) return null;
  return (
    <section className="flex flex-col gap-2">
      <h3 className="m-0 text-sm font-semibold text-muted">{title}</h3>
      <ul className="m-0 flex list-none flex-col gap-2 p-0">
        {accounts.map((account) => (
          <li key={account.id}>
            <TreasuryAccountCard account={account} />
          </li>
        ))}
      </ul>
    </section>
  );
}

function AgencySection({ agency, group }: { agency: AgencyRef; group: AgencyTreasury }) {
  const { t } = useTranslation();
  const name = t(`agencies.${agency.code}`, { defaultValue: agency.name });
  const empty = group.cash.length === 0 && group.banks.length === 0;
  return (
    <section className="flex flex-col gap-3" aria-label={name}>
      <div className="flex flex-col gap-1 rounded-2xl border border-border bg-surface p-3.5">
        <div className="flex items-center gap-2">
          <AgencyBadge code={agency.code} color={agency.color} />
          <h2 className="m-0 text-base font-semibold">{name}</h2>
        </div>
        {group.activeTotal !== null && (
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <span className="text-sm text-muted">{t('treasury.activeTotal')}</span>
            <span className="amount text-xl">{formatAmount(group.activeTotal, group.currency)}</span>
          </div>
        )}
      </div>
      {empty ? (
        <p className="m-0 rounded-2xl bg-surface-2 p-4 text-center text-muted">{t('treasury.empty')}</p>
      ) : (
        <>
          <AccountGroup title={t('treasury.groups.cash')} accounts={group.cash} />
          <AccountGroup title={t('treasury.groups.banks')} accounts={group.banks} />
        </>
      )}
    </section>
  );
}

export function TreasuryPage() {
  const { t, i18n } = useTranslation();
  const { scope } = useAgencyScope();
  const canManage = usePermission('treasury.manage');
  const online = useOnlineStatus();
  const accounts = useTreasuryAccounts(scopedAgencyId(scope), { includeInactive: true });
  const [creating, setCreating] = useState(false);

  const shown = useMemo(() => (scope.kind === 'all' ? scope.agencies : [scope.agency]), [scope]);
  // La devise d'une agence verrouillée arrive avec la liste des agences : sans elle, pas de total juste.
  const ready = shown.length > 0 && shown.every((a) => a.currency !== null);
  const sections = useMemo(() => {
    const data = accounts.data;
    if (!data) return [];
    return shown.flatMap((agency) => (agency.currency ? [{ agency, group: agencyTreasury(data, { id: agency.id, currency: agency.currency }) }] : []));
  }, [accounts.data, shown]);
  const firstAgencyId = shown[0]?.id;

  const manageable = canManage && online;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      {canManage && !online && <Notice tone="info">{t('treasury.offline')}</Notice>}
      {manageable && ready && (
        <Button variant="primary" icon={<Icon name="plus" />} className="self-start" onClick={() => setCreating(true)}>
          {t('treasury.create.open')}
        </Button>
      )}

      {(accounts.isPending || (accounts.isSuccess && !ready)) && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-20 animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-16 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {accounts.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, accounts.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void accounts.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {ready && sections.length > 0 && (
        <div className={`grid gap-6 ${sections.length > 1 ? 'lg:grid-cols-2' : ''}`}>
          {sections.map(({ agency, group }) => (
            <AgencySection key={agency.id} agency={agency} group={group} />
          ))}
        </div>
      )}

      {creating && firstAgencyId && (
        <CreateAccountSheet agencies={shown} defaultAgencyId={firstAgencyId} onClose={() => setCreating(false)} />
      )}
    </div>
  );
}
