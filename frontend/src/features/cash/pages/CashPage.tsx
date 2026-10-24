import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useTreasuryAccounts } from '@/features/treasury/queries';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { AgencyChoice } from '@/shared/agency/AgencyChoice';
import { useAgencyScope, type AgencyRef } from '@/shared/agency/agencyScope';
import { ExportButton } from '@/shared/files/ExportButton';
import { compactDay, isIsoDay, todayIso } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useOnlineStatus } from '@/shared/useOnlineStatus';
import { parseCashSearch, type CashSearch } from '../cashRules';
import { AdjustmentList } from '../components/AdjustmentList';
import { AdjustmentSheet } from '../components/AdjustmentSheet';
import { CancelMovementSheet } from '../components/CancelMovementSheet';
import { CashMovementRow } from '../components/CashMovementRow';
import { CashTotals } from '../components/CashTotals';
import { exportCashMovements } from '../api';
import { useBalanceAdjustments, useCashMovements } from '../queries';
import { CASH_MOVEMENT_TYPES, type CashMovement, type CashMovementType } from '../types';

// Écran « Caisse » (maquette 09) : mouvements d'une agence sur une période (aujourd'hui par défaut), totaux
// entrées / sorties / net (annulés exclus) et virements internes à part, 50 par page, filtres dans l'adresse.
// Une caisse = une agence = une devise : sur « les deux agences », le Superviseur choisit la caisse (NKC | DKR) et
// l'agence est toujours envoyée (sinon le serveur additionnerait des MRO et des FCFA dans les mêmes totaux).
// Corrections du Superviseur (cash.manage, livraison 2) : « Annuler » sur un mouvement actif (motif obligatoire) et
// « Ajustement de solde » ; les ajustements de la période sont listés à part. Hors ligne : ni l'un ni l'autre.
// POINT OUVERT : la recherche porte sur la note seulement (backend) ; le nom du tiers viendra plus tard.

const SEARCH_DELAY_MS = 350;

const control = 'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[15px] text-fg focus-visible:border-ink focus-visible:outline-none';

export function CashPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const raw = useSearch({ strict: false }) as Record<string, unknown>;
  const filters = useMemo(() => parseCashSearch(raw), [raw]);
  const { scope } = useAgencyScope();
  const canCreate = usePermission('cash.create');
  const canManage = usePermission('cash.manage');
  const online = useOnlineStatus();
  const [cancelling, setCancelling] = useState<CashMovement | null>(null);
  const [adjusting, setAdjusting] = useState(false);

  const choices: AgencyRef[] = scope.kind === 'all' ? scope.agencies : [];
  const agency: AgencyRef | undefined =
    scope.kind === 'all' ? (choices.find((a) => a.id === filters.agencyId) ?? choices[0]) : scope.agency;

  const setFilters = useCallback(
    (next: CashSearch) => void navigate({ to: '/cash', search: next as Record<string, unknown>, replace: true }),
    [navigate],
  );
  const set = (patch: Partial<CashSearch>) => setFilters({ ...filters, ...patch, page: undefined });

  // La recherche (dans la note) part après une pause de frappe.
  const [search, setSearch] = useState(filters.search ?? '');
  useEffect(() => {
    const trimmed = search.trim();
    if (trimmed === (filters.search ?? '')) return;
    const timer = setTimeout(() => setFilters({ ...filters, search: trimmed || undefined, page: undefined }), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search, filters, setFilters]);

  const today = todayIso();
  const accounts = useTreasuryAccounts(agency?.id, { includeInactive: true, enabled: Boolean(agency) });
  const list = useCashMovements(
    {
      agencyId: agency?.id,
      from: filters.from,
      to: filters.to,
      type: filters.type,
      treasuryAccountId: filters.account,
      search: filters.search,
      page: filters.page,
    },
    Boolean(agency),
  );
  const adjustments = useBalanceAdjustments({ agencyId: agency?.id, from: filters.from, to: filters.to }, Boolean(agency));
  const data = list.data;
  const page = filters.page ?? 1;
  const manageable = canManage && online;
  const lastPage = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1;

  return (
    <div className="mx-auto flex w-full max-w-3xl flex-col gap-4">
      {choices.length > 1 && (
        <AgencyChoice label={t('cash.agencyChoice')} agencies={choices} selectedId={agency?.id} onSelect={(id) => setFilters({ agencyId: id })} />
      )}

      {data && agency?.currency && <CashTotals list={data} currency={agency.currency} />}
      {data && agency && (
        <ExportButton
          fileName={`Caisse-${compactDay(filters.from ?? today)}-${compactDay(filters.to ?? today)}.xlsx`}
          fetchFile={(http, lang) =>
            exportCashMovements(
              http,
              { agencyId: agency.id, from: filters.from, to: filters.to, type: filters.type, treasuryAccountId: filters.account, search: filters.search },
              lang,
            )
          }
        />
      )}

      {canCreate &&
        (online ? (
          <Link
            to="/cash/new"
            search={scope.kind === 'all' && agency ? { agencyId: agency.id } : {}}
            className="inline-flex min-h-14 items-center justify-center gap-2 self-start rounded-[14px] border-2 border-ink bg-ink px-5 text-[17px] font-semibold text-on-ink no-underline"
          >
            <Icon name="plus" />
            {t('cash.new')}
          </Link>
        ) : (
          <Notice tone="info">{t('cash.offline')}</Notice>
        ))}
      {manageable && agency?.currency && (
        <Button icon={<Icon name="pencil" />} className="self-start" onClick={() => setAdjusting(true)}>
          {t('cash.adjustment.open')}
        </Button>
      )}

      <div className="flex flex-col gap-2.5" role="group" aria-label={t('cash.filters.label')}>
        <label className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface px-3 focus-within:border-ink">
          <Icon name="search" className="text-muted" />
          <span className="sr-only">{t('cash.filters.search')}</span>
          <input
            type="search"
            value={search}
            placeholder={t('cash.filters.searchPlaceholder')}
            onChange={(e) => setSearch(e.target.value)}
            className="min-w-0 flex-1 bg-transparent text-[17px] text-fg outline-none"
          />
        </label>
        <div className="flex flex-wrap items-end gap-2">
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('cash.filters.from')}</span>
            <input
              type="date"
              value={filters.from ?? today}
              max={filters.to ?? today}
              onChange={(e) => set({ from: isIsoDay(e.target.value) && e.target.value !== today ? e.target.value : undefined })}
              className={control}
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('cash.filters.to')}</span>
            <input
              type="date"
              value={filters.to ?? today}
              min={filters.from}
              onChange={(e) => set({ to: isIsoDay(e.target.value) && e.target.value !== today ? e.target.value : undefined })}
              className={control}
            />
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('cash.filters.type')}</span>
            <select value={filters.type ?? ''} onChange={(e) => set({ type: (e.target.value || undefined) as CashMovementType | undefined })} className={control}>
              <option value="">{t('cash.filters.allTypes')}</option>
              {CASH_MOVEMENT_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(`cash.types.${type}`)}
                </option>
              ))}
            </select>
          </label>
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('cash.filters.account')}</span>
            <select value={filters.account ?? ''} onChange={(e) => set({ account: e.target.value || undefined })} className={control}>
              <option value="">{t('cash.filters.allAccounts')}</option>
              {(accounts.data ?? []).map((account) => (
                <option key={account.id} value={account.id}>
                  {account.name}
                </option>
              ))}
            </select>
          </label>
        </div>
      </div>

      {list.isPending && agency && (
        <div role="status" className="flex flex-col gap-2">
          <div className="h-20 animate-pulse rounded-2xl bg-surface-2" />
          <div className="h-20 animate-pulse rounded-2xl bg-surface-2" />
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}
      {list.isError && (
        <div className="flex flex-col gap-3">
          <Notice tone="error">{errorMessage(i18n, list.error)}</Notice>
          <Button icon={<Icon name="sync" />} onClick={() => void list.refetch()}>
            {t('common.retry')}
          </Button>
        </div>
      )}

      {data && (
        <>
          <p className="m-0 text-sm text-muted" aria-live="polite">
            {t('cash.count', { count: data.totalCount, total: data.allCount })}
          </p>
          {data.items.length === 0 ? (
            <p className="m-0 rounded-2xl bg-surface-2 p-6 text-center font-semibold">{t('cash.empty')}</p>
          ) : (
            <ul className={`m-0 flex list-none flex-col gap-2 p-0 ${list.isPlaceholderData ? 'opacity-60' : ''}`}>
              {data.items.map((movement) => (
                <li key={movement.id}>
                  <CashMovementRow movement={movement} onCancel={manageable ? () => setCancelling(movement) : undefined} />
                </li>
              ))}
            </ul>
          )}
          {lastPage > 1 && (
            <nav className="flex items-center justify-between gap-2" aria-label={t('cash.pagination')}>
              <Button disabled={page <= 1} onClick={() => setFilters({ ...filters, page: page - 1 > 1 ? page - 1 : undefined })}>
                {t('cash.previous')}
              </Button>
              <span className="text-sm text-muted">{t('cash.page', { page, last: lastPage })}</span>
              <Button disabled={page >= lastPage} onClick={() => setFilters({ ...filters, page: page + 1 })}>
                {t('cash.next')}
              </Button>
            </nav>
          )}
        </>
      )}

      {adjustments.data && <AdjustmentList adjustments={adjustments.data} />}

      {cancelling && <CancelMovementSheet key={cancelling.id} movement={cancelling} onClose={() => setCancelling(null)} />}
      {adjusting && agency?.currency && (
        <AdjustmentSheet
          agencyId={agency.id}
          agencyName={t(`agencies.${agency.code}`, { defaultValue: agency.name })}
          currency={agency.currency}
          onClose={() => setAdjusting(false)}
        />
      )}
    </div>
  );
}
