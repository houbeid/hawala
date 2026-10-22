import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatDayShort } from '@/shared/format/dates';
import { Icon } from '@/shared/ui/Icon';
import { TRANSFER_DIRECTIONS, TRANSFER_STATUSES, type TransferFilters } from '../types';

// Barre de filtres (maquette 03) : recherche (envoyée après une courte pause de frappe), période, et un bouton
// « Filtres · n » qui ouvre statut et sens. Les filtres actifs sont des puces retirables. Le sens n'existe
// qu'avec une agence (règle du serveur : « Envoyé / Reçu » se lit depuis une agence).

const SEARCH_DELAY_MS = 350;

const control =
  'min-h-11 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[15px] text-fg focus-visible:border-ink focus-visible:outline-none';
const pill =
  'inline-flex min-h-11 items-center gap-1.5 rounded-full border-[1.5px] border-border bg-surface px-3.5 text-[15px] font-semibold text-fg ' +
  'aria-expanded:border-ink aria-pressed:border-ink aria-pressed:bg-ink aria-pressed:text-on-ink';

interface Props {
  filters: TransferFilters;
  today: string;
  directionAvailable: boolean;
  onChange: (next: TransferFilters) => void;
}

export function TransferFiltersPanel({ filters, today, directionAvailable, onChange }: Props) {
  const { t } = useTranslation();
  const panelId = useId();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState(filters.search ?? '');
  const [lastApplied, setLastApplied] = useState(filters.search);

  // Un changement d'adresse (retour arrière, puce retirée) remet le champ à jour, pendant le rendu.
  if (filters.search !== lastApplied) {
    setLastApplied(filters.search);
    setSearch(filters.search ?? '');
  }

  // La recherche part après une pause de frappe.
  useEffect(() => {
    const trimmed = search.trim();
    if (trimmed === (filters.search ?? '')) return;
    const timer = setTimeout(() => onChange({ ...filters, search: trimmed || undefined, page: undefined }), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search, filters, onChange]);

  const from = filters.from ?? today;
  const to = filters.to ?? today;
  const periodLabel =
    from === today && to === today ? t('transfers.filters.today') : from === to ? formatDayShort(from) : `${formatDayShort(from)} – ${formatDayShort(to)}`;
  const activeCount = (filters.status ? 1 : 0) + (filters.direction ? 1 : 0);
  const set = (patch: Partial<TransferFilters>) => onChange({ ...filters, ...patch, page: undefined });

  return (
    <div className="flex flex-col gap-2.5">
      <label className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface px-3 focus-within:border-ink">
        <Icon name="search" className="text-muted" />
        <span className="sr-only">{t('transfers.filters.search')}</span>
        <input
          type="search"
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          placeholder={t('transfers.filters.searchPlaceholder')}
          className="min-h-11 w-full min-w-0 border-0 bg-transparent text-[16px] text-fg outline-none"
        />
      </label>

      <div className="flex flex-wrap items-center gap-2">
        <span className={`${pill} pointer-events-none`}>
          <Icon name="calendar" className="size-5" />
          {periodLabel}
        </span>
        <span className="flex-1" />
        <button type="button" className={pill} aria-expanded={open} aria-controls={panelId} onClick={() => setOpen((v) => !v)}>
          <Icon name="filter" className="size-5" />
          {activeCount > 0 ? t('transfers.filters.buttonCount', { count: activeCount }) : t('transfers.filters.button')}
        </button>
      </div>

      {open && (
        <div id={panelId} className="grid gap-3 rounded-2xl border border-border bg-surface p-3.5 sm:grid-cols-2">
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('transfers.filters.from')}</span>
            <input type="date" value={from} max={to} onChange={(e) => e.target.value && set({ from: e.target.value })} className={control} />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('transfers.filters.to')}</span>
            <input type="date" value={to} min={from} onChange={(e) => e.target.value && set({ to: e.target.value })} className={control} />
          </label>
          <label className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold text-muted">{t('transfers.filters.status')}</span>
            <select
              value={filters.status ?? ''}
              onChange={(e) => set({ status: (e.target.value || undefined) as TransferFilters['status'] })}
              className={control}
            >
              <option value="">{t('transfers.filters.allStatuses')}</option>
              {TRANSFER_STATUSES.map((status) => (
                <option key={status} value={status}>
                  {t(`transfers.status.${status}`)}
                </option>
              ))}
            </select>
          </label>
          {directionAvailable && (
            <label className="flex flex-col gap-1.5">
              <span className="text-sm font-semibold text-muted">{t('transfers.filters.direction')}</span>
              <select
                value={filters.direction ?? ''}
                onChange={(e) => set({ direction: (e.target.value || undefined) as TransferFilters['direction'] })}
                className={control}
              >
                <option value="">{t('transfers.filters.allDirections')}</option>
                {TRANSFER_DIRECTIONS.map((direction) => (
                  <option key={direction} value={direction}>
                    {t(`transfers.direction.${direction}`)}
                  </option>
                ))}
              </select>
            </label>
          )}
          {(filters.from || filters.to) && (
            <button type="button" className="min-h-11 justify-self-start font-semibold underline" onClick={() => set({ from: undefined, to: undefined })}>
              {t('transfers.filters.backToToday')}
            </button>
          )}
        </div>
      )}

      {activeCount > 0 && (
        <div className="flex flex-wrap gap-2">
          {filters.status && (
            <button type="button" className={pill} aria-pressed onClick={() => set({ status: undefined })}>
              {t(`transfers.status.${filters.status}`)}
              <Icon name="x" className="size-4" />
              <span className="sr-only">{t('transfers.filters.remove')}</span>
            </button>
          )}
          {filters.direction && (
            <button type="button" className={pill} aria-pressed onClick={() => set({ direction: undefined })}>
              {t(`transfers.direction.${filters.direction}`)}
              <Icon name="x" className="size-4" />
              <span className="sr-only">{t('transfers.filters.remove')}</span>
            </button>
          )}
        </div>
      )}
    </div>
  );
}
