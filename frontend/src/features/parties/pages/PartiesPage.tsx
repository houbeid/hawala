import { useNavigate, useSearch } from '@tanstack/react-router';
import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { scopedAgencyId, useAgencyScope } from '@/shared/agency/agencyScope';
import { formatAmount } from '@/shared/format/money';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { CreatePartyDialog } from '../components/CreatePartyDialog';
import { PartyFilters } from '../components/PartyFilters';
import { PartyRow } from '../components/PartyRow';
import { activeFilterCount, parsePartySearch, type PartySearch } from '../partyRules';
import { useParties } from '../queries';
import type { PartyTotals } from '../types';

// Liste des tiers (maquettes 08 et D3). Un tiers est commun aux deux agences ; son solde est montré pour
// chaque agence visible (la sienne pour un agent, les deux pour le Superviseur sur « les deux agences »),
// seulement avec parties.view_balance. Les totaux restent séparés par devise, jamais convertis.
// POINT OUVERT : pas de pagination (le backend renvoie toute la liste ; quelques centaines de tiers au plus).

function Totals({ totals }: { totals: PartyTotals[] }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-2">
      {totals.map((total) => (
        <dl key={total.currency} className="m-0 grid grid-cols-3 gap-2 rounded-2xl border border-border bg-surface p-3.5">
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs text-muted">{t('parties.totals.red')}</dt>
            <dd className="amount m-0 text-red">{formatAmount(total.red, total.currency)}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs text-muted">{t('parties.totals.green')}</dt>
            <dd className="amount m-0 text-green">{formatAmount(-total.green, total.currency)}</dd>
          </div>
          <div className="flex flex-col gap-0.5">
            <dt className="text-xs text-muted">{t('parties.totals.net')}</dt>
            <dd className="m-0 flex flex-col">
              <span className="amount">{formatAmount(Math.abs(total.net), total.currency)}</span>
              <span className="text-xs text-muted">{t(`parties.totals.netState.${total.net > 0 ? 'red' : total.net < 0 ? 'green' : 'zero'}`)}</span>
            </dd>
          </div>
        </dl>
      ))}
    </div>
  );
}

export function PartiesPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const raw = useSearch({ strict: false }) as Record<string, unknown>;
  const filters = useMemo(() => parsePartySearch(raw), [raw]);
  const { scope } = useAgencyScope();
  const agencyId = scopedAgencyId(scope);
  const canViewBalance = usePermission('parties.view_balance');
  const canCreate = usePermission('parties.create');
  const online = useOnlineStatus();
  const agencies = useAgencies();
  const agencyCodes = useMemo(() => new Map((agencies.data ?? []).map((a) => [a.id, a.code])), [agencies.data]);
  const [creating, setCreating] = useState(false);

  // Le filtre de solde exige une agence : il est ignoré (et masqué) quand le Superviseur regarde les deux.
  const balanceFilter = canViewBalance && agencyId !== undefined;
  const parties = useParties({
    agencyId,
    search: filters.search,
    type: filters.type,
    balanceSheet: filters.balanceSheet,
    balance: balanceFilter ? filters.balance : undefined,
    includeArchived: filters.archived,
  });

  const setFilters = useCallback(
    (next: PartySearch) => void navigate({ to: '/parties', search: next as Record<string, unknown>, replace: true }),
    [navigate],
  );

  const data = parties.data;
  const filtered = Boolean(filters.search) || activeFilterCount(filters) > 0;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      {data?.totals && data.totals.length > 0 && <Totals totals={data.totals} />}

      {canCreate &&
        (online ? (
          <Button variant="primary" icon={<Icon name="plus" />} className="self-start" onClick={() => setCreating(true)}>
            {t('parties.create.open')}
          </Button>
        ) : (
          <Notice tone="info">{t('parties.offline')}</Notice>
        ))}

      <PartyFilters filters={filters} onChange={setFilters} balanceFilter={balanceFilter} />

      {parties.isPending && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-[72px] animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-[72px] animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {parties.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, parties.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void parties.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {data && (
        <>
          <p className="m-0 text-sm text-muted" aria-live="polite">
            {t('parties.count', { count: data.count, total: data.totalCount })}
          </p>
          {data.items.length === 0 ? (
            <div className="flex flex-col items-center gap-3 rounded-2xl bg-surface-2 p-6 text-center">
              <p className="m-0 font-semibold">{t('parties.empty.title')}</p>
              {filtered && (
                <Button onClick={() => setFilters({})}>{t('parties.empty.reset')}</Button>
              )}
            </div>
          ) : (
            <ul className={`m-0 flex list-none flex-col gap-2 p-0 ${parties.isPlaceholderData ? 'opacity-60' : ''}`}>
              {data.items.map((party) => (
                <li key={party.id}>
                  <PartyRow party={party} agencyCodes={agencyCodes} />
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {creating && (
        <CreatePartyDialog
          open
          onOpenChange={(value) => !value && setCreating(false)}
          initialName={filters.search ?? ''}
          onCreated={(party) => void navigate({ to: '/parties/$partyId', params: { partyId: party.id } })}
        />
      )}
    </div>
  );
}
