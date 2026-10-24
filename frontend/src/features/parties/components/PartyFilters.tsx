import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Icon } from '@/shared/ui/Icon';
import { activeFilterCount, type PartySearch } from '../partyRules';
import { PARTY_TYPES, type PartyType } from '../types';

// Filtres de la liste des tiers (maquette 08) : recherche (nom, téléphone, NNI), type, « dans le bilan »,
// solde (rouge / vert) et archivés. Écart avec la maquette : les filtres sont posés directement sous la
// recherche (pas de feuille « Appliquer »), ils s'appliquent tout de suite et restent dans l'adresse.
// Le filtre de solde porte sur UNE agence (règle du backend) : absent quand le Superviseur regarde les deux.

const SEARCH_DELAY_MS = 350;

const selectClass =
  'min-h-12 rounded-xl border-[1.5px] border-border bg-surface px-3 text-[15px] text-fg focus-visible:border-ink focus-visible:outline-none';

interface Props {
  filters: PartySearch;
  onChange: (next: PartySearch) => void;
  /** Filtre « solde » possible : soldes visibles (parties.view_balance) et une seule agence. */
  balanceFilter: boolean;
}

export function PartyFilters({ filters, onChange, balanceFilter }: Props) {
  const { t } = useTranslation();
  const [search, setSearch] = useState(filters.search ?? '');
  const [lastApplied, setLastApplied] = useState(filters.search);

  // Un changement d'adresse (retour arrière, réinitialisation) remet le champ à jour, pendant le rendu.
  if (filters.search !== lastApplied) {
    setLastApplied(filters.search);
    setSearch(filters.search ?? '');
  }

  useEffect(() => {
    const trimmed = search.trim();
    if (trimmed === (filters.search ?? '')) return;
    const timer = setTimeout(() => onChange({ ...filters, search: trimmed || undefined }), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search, filters, onChange]);

  const set = (patch: Partial<PartySearch>) => onChange({ ...filters, ...patch });
  const count = activeFilterCount(filters);

  return (
    <div className="flex flex-col gap-2.5">
      <label className="flex min-h-12 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface px-3 focus-within:border-ink">
        <Icon name="search" className="text-muted" />
        <span className="sr-only">{t('parties.filters.search')}</span>
        <input
          type="search"
          value={search}
          placeholder={t('parties.filters.searchPlaceholder')}
          onChange={(event) => setSearch(event.target.value)}
          className="min-w-0 flex-1 bg-transparent text-[17px] text-fg outline-none"
        />
      </label>
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label={t('parties.filters.label')}>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('parties.filters.type')}</span>
          <select value={filters.type ?? ''} onChange={(e) => set({ type: (e.target.value || undefined) as PartyType | undefined })} className={selectClass}>
            <option value="">{t('parties.filters.allTypes')}</option>
            {PARTY_TYPES.map((value) => (
              <option key={value} value={value}>
                {t(`parties.types.${value}`)}
              </option>
            ))}
          </select>
        </label>
        <label className="flex flex-col gap-1">
          <span className="text-xs font-semibold text-muted">{t('parties.filters.balanceSheet')}</span>
          <select
            value={filters.balanceSheet ?? ''}
            onChange={(e) => set({ balanceSheet: (e.target.value || undefined) as PartySearch['balanceSheet'] })}
            className={selectClass}
          >
            <option value="">{t('parties.filters.all')}</option>
            <option value="In">{t('parties.filters.in')}</option>
            <option value="Out">{t('parties.filters.out')}</option>
          </select>
        </label>
        {balanceFilter && (
          <label className="flex flex-col gap-1">
            <span className="text-xs font-semibold text-muted">{t('parties.filters.balance')}</span>
            <select
              value={filters.balance ?? ''}
              onChange={(e) => set({ balance: (e.target.value || undefined) as PartySearch['balance'] })}
              className={selectClass}
            >
              <option value="">{t('parties.filters.all')}</option>
              <option value="Red">{t('parties.filters.red')}</option>
              <option value="Green">{t('parties.filters.green')}</option>
            </select>
          </label>
        )}
        <label className="flex min-h-12 items-center gap-2 self-end px-1 text-[15px]">
          <input
            type="checkbox"
            checked={filters.archived ?? false}
            onChange={(e) => set({ archived: e.target.checked || undefined })}
            className="size-5 accent-[var(--ink)]"
          />
          {t('parties.filters.archived')}
        </label>
        {count > 0 && (
          <button
            type="button"
            onClick={() => onChange({ search: filters.search })}
            className="min-h-12 self-end rounded-xl px-3 text-[15px] font-semibold text-fg underline"
          >
            {t('parties.filters.reset', { count })}
          </button>
        )}
      </div>
    </div>
  );
}
