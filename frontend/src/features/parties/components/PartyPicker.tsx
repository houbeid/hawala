import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePermission } from '@/shared/auth/hooks';
import { Icon } from '@/shared/ui/Icon';
import { useParties } from '../queries';
import type { PartySummary } from '../types';
import { CreatePartyDialog } from './CreatePartyDialog';

// Choix d'un tiers (crédit d'un transfert, plus tard mouvement de caisse) : recherche par nom ou téléphone,
// 8 résultats au plus, tiers archivés exclus (le serveur les refuse). Le téléphone départage deux tiers
// de même nom (noms non uniques). « Créer un tiers » avec parties.create.

const SEARCH_DELAY_MS = 300;
const MAX_RESULTS = 8;

interface Props {
  label: string;
  selected: PartySummary | null;
  onSelect: (party: PartySummary | null) => void;
  error?: string;
}

export function PartyPicker({ label, selected, onSelect, error }: Props) {
  const { t } = useTranslation();
  const canCreate = usePermission('parties.create');
  const listId = useId();
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => setSearch(text.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [text]);

  const parties = useParties({ search: search || undefined }, selected === null);
  const results = (parties.data?.items ?? []).filter((p) => !p.isArchived).slice(0, MAX_RESULTS);

  if (selected) {
    return (
      <div className="flex flex-col gap-1.5">
        <span className="text-sm font-semibold text-muted">{label}</span>
        <div className="flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-ink bg-surface px-3.5">
          <Icon name="user" />
          <span className="flex-1 font-semibold">{selected.name}</span>
          <button type="button" className="min-h-11 font-semibold underline" onClick={() => onSelect(null)}>
            {t('parties.picker.change')}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <label className="flex flex-col gap-1.5">
        <span className="text-sm font-semibold text-muted">{label}</span>
        <span
          data-invalid={error ? '' : undefined}
          className="flex min-h-14 items-center gap-2 rounded-xl border-[1.5px] border-border bg-surface px-3 focus-within:border-ink data-invalid:border-red"
        >
          <Icon name="search" className="text-muted" />
          <input
            type="search"
            value={text}
            onChange={(event) => setText(event.target.value)}
            placeholder={t('parties.picker.searchPlaceholder')}
            aria-controls={listId}
            aria-invalid={error ? true : undefined}
            className="min-h-13 w-full min-w-0 border-0 bg-transparent text-[16px] text-fg outline-none"
          />
        </span>
      </label>
      {error && <span className="text-sm font-semibold text-red">{error}</span>}

      <ul id={listId} className="m-0 flex list-none flex-col overflow-hidden rounded-2xl border border-border p-0" aria-label={t('parties.picker.results')}>
        {results.map((party) => (
          <li key={party.id} className="border-b border-border last:border-b-0">
            <button
              type="button"
              onClick={() => onSelect(party)}
              className="flex min-h-14 w-full items-center gap-3 bg-surface px-3.5 text-start"
            >
              <Icon name="user" className="text-muted" />
              <span className="flex min-w-0 flex-1 flex-col">
                <span className="truncate font-semibold">{party.name}</span>
                <span className="text-sm text-muted">
                  {t(`parties.types.${party.type}`)}
                  {party.phone && (
                    <>
                      {' · '}
                      <span dir="ltr">{party.phone}</span>
                    </>
                  )}
                </span>
              </span>
            </button>
          </li>
        ))}
        {parties.isSuccess && results.length === 0 && <li className="bg-surface p-3.5 text-muted">{t('parties.picker.none')}</li>}
      </ul>

      {canCreate && (
        <>
          <button type="button" className="inline-flex min-h-11 items-center gap-2 self-start font-semibold underline" onClick={() => setCreating(true)}>
            <Icon name="plus" />
            {t('parties.picker.create')}
          </button>
          {creating && (
            <CreatePartyDialog
              open={creating}
              onOpenChange={setCreating}
              initialName={text}
              onCreated={(party) => {
                setText('');
                // Tiers neuf : solde nul dans les deux agences ; son plafond sera relu avec la liste.
                onSelect({
                  ...party,
                  accountCode: null,
                  nni: null,
                  address: null,
                  note: null,
                  inBalanceSheet: true,
                  isArchived: false,
                  balances: null,
                });
              }}
            />
          )}
        </>
      )}
    </div>
  );
}
