import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAgencies } from '@/features/organization/queries';
import type { Agency } from '@/features/organization/types';
import { scopedAgencyId, useAgencyScope } from '@/shared/agency/agencyScope';
import { errorMessage } from '@/shared/api/errorMessage';
import { usePermission } from '@/shared/auth/hooks';
import { todayIso } from '@/shared/format/dates';
import { Button } from '@/shared/ui/Button';
import { Icon } from '@/shared/ui/Icon';
import { Notice } from '@/shared/ui/Notice';
import { useIsDesktop } from '@/shared/useMediaQuery';
import { SummaryCards } from '../components/SummaryCards';
import { TransferCard } from '../components/TransferCard';
import { TransferFiltersPanel } from '../components/TransferFiltersPanel';
import { TransferTable } from '../components/TransferTable';
import { useTransferList, useTransferSummary } from '../queries';
import type { TransferFilters } from '../types';

// Liste des transferts (maquettes 03 et D1). Filtres dans l'adresse (partageables, gardés au rechargement).
// Cartes : pour une agence ; le Superviseur sur « les deux agences » choisit d'abord l'agence des cartes
// (décision du 22 octobre 2026 : additionner MRO et FCFA n'aurait pas de sens, l'API exige une agence).

function useAgencyMap(): Map<string, Agency> {
  const agencies = useAgencies();
  return useMemo(() => new Map((agencies.data ?? []).map((agency) => [agency.id, agency])), [agencies.data]);
}

function CardsAgencyChooser() {
  const { t } = useTranslation();
  const { scope, select } = useAgencyScope();
  if (scope.kind !== 'all') return null;
  return (
    <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-dashed border-border bg-surface p-3">
      <span className="text-sm font-semibold text-muted">{t('transfers.cards.chooseAgency')}</span>
      {scope.agencies.map((agency) => (
        <button
          key={agency.id}
          type="button"
          onClick={() => select(agency.id)}
          className="inline-flex min-h-11 items-center gap-2 rounded-full border-[1.5px] px-3.5 font-semibold"
          style={{ borderColor: agency.color }}
        >
          <i className="size-2.5 rounded-full" style={{ backgroundColor: agency.color }} aria-hidden="true" />
          {agency.code}
        </button>
      ))}
    </div>
  );
}

export function TransfersPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const filters = useSearch({ strict: false }) as TransferFilters;
  const { scope } = useAgencyScope();
  const canCreate = usePermission('transfers.create');
  const desktop = useIsDesktop();
  const agencies = useAgencyMap();
  const today = todayIso();

  const agencyId = scopedAgencyId(scope);
  // Un sens sans agence est refusé par le serveur : on ne l'envoie pas tant que le Superviseur voit tout.
  const effectiveFilters = scope.kind === 'all' ? { ...filters, direction: undefined } : filters;
  const list = useTransferList({ ...effectiveFilters, agencyId });
  const summary = useTransferSummary(
    { agencyId, from: filters.from, to: filters.to, search: filters.search },
    scope.kind !== 'all',
  );

  const setFilters = useCallback(
    (next: TransferFilters) => void navigate({ to: '/transfers', search: next, replace: true }),
    [navigate],
  );
  const withDay = (filters.from ?? today) !== (filters.to ?? today);
  const page = list.data?.page ?? filters.page ?? 1;
  const pageCount = list.data ? Math.max(1, Math.ceil(list.data.totalCount / list.data.pageSize)) : 1;

  return (
    <div className="flex flex-col gap-4">
      {scope.kind === 'all' ? (
        <CardsAgencyChooser />
      ) : summary.isSuccess ? (
        <SummaryCards summary={summary.data} agencies={agencies} />
      ) : summary.isError ? (
        <Notice tone="error">{errorMessage(i18n, summary.error)}</Notice>
      ) : (
        <div role="status" className="h-24 animate-pulse rounded-2xl bg-surface-2">
          <span className="sr-only">{t('common.loading')}</span>
        </div>
      )}

      <div className="flex flex-wrap items-start gap-3">
        <div className="min-w-0 flex-1">
          <TransferFiltersPanel
            filters={effectiveFilters}
            today={today}
            directionAvailable={scope.kind !== 'all'}
            onChange={setFilters}
          />
        </div>
        {canCreate && (
          <Link
            to="/transfers/new"
            className="hidden min-h-14 items-center gap-2 rounded-[14px] border-2 border-ink bg-ink px-5 text-[17px] font-semibold text-on-ink no-underline lg:inline-flex"
          >
            <Icon name="plus" />
            {t('transfers.new')}
          </Link>
        )}
      </div>

      {list.data && (
        <p className="m-0 font-semibold text-muted" aria-live="polite">
          {t('transfers.counter', { shown: list.data.totalCount, all: list.data.allCount })}
        </p>
      )}

      {list.isPending && (
        <div role="status" className="flex items-center gap-2 text-muted">
          <Icon name="sync" spin />
          {t('common.loading')}
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

      {list.data && list.data.items.length === 0 && (
        <div className="flex flex-col items-center gap-2 rounded-2xl border border-border bg-surface p-6 text-center">
          <span className="inline-flex size-16 items-center justify-center rounded-full bg-surface-2 text-muted">
            <Icon name="transfer" className="size-8" />
          </span>
          <p className="m-0 font-semibold">{t('transfers.empty.title')}</p>
          <p className="m-0 text-muted">{t('transfers.empty.hint')}</p>
        </div>
      )}

      {list.data && list.data.items.length > 0 && (
        <div className={list.isPlaceholderData ? 'opacity-60' : ''} aria-busy={list.isPlaceholderData || undefined}>
          {desktop ? (
            <TransferTable transfers={list.data.items} agencies={agencies} withDay={withDay} />
          ) : (
            <ul className="m-0 flex list-none flex-col gap-3 p-0">
              {list.data.items.map((transfer) => (
                <li key={transfer.id}>
                  <TransferCard transfer={transfer} agencies={agencies} withDay={withDay} />
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      {list.data && pageCount > 1 && (
        <nav aria-label={t('transfers.pagination.label')} className="flex items-center justify-between gap-3">
          <Button disabled={page <= 1} onClick={() => setFilters({ ...filters, page: page - 1 > 1 ? page - 1 : undefined })}>
            {t('transfers.pagination.previous')}
          </Button>
          <span className="font-semibold text-muted">{t('transfers.pagination.position', { page, count: pageCount })}</span>
          <Button disabled={page >= pageCount} onClick={() => setFilters({ ...filters, page: page + 1 })}>
            {t('transfers.pagination.next')}
          </Button>
        </nav>
      )}
    </div>
  );
}
