import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { Icon } from '@/shared/ui/Icon';
import { initials } from '../partyRules';
import type { PartySummary } from '../types';
import { BalanceAmount } from './BalanceAmount';

// Ligne de la liste des tiers (maquette 08) : pastille d'initiales, nom, type, téléphone, marques « Hors bilan »
// et « Archivé » en texte, solde de chaque agence visible. Toute la ligne ouvre la fiche.

interface Props {
  party: PartySummary;
  /** Code d'agence par identifiant, pour écrire « NKC » / « DKR » devant chaque solde quand il y en a deux. */
  agencyCodes: Map<string, string>;
}

export function PartyRow({ party, agencyCodes }: Props) {
  const { t } = useTranslation();
  const balances = party.balances ?? [];
  const several = balances.length > 1;
  return (
    <Link
      to="/parties/$partyId"
      params={{ partyId: party.id }}
      className="flex min-h-[72px] items-center gap-3 rounded-2xl border border-border bg-surface p-3.5 text-fg no-underline"
    >
      <span className="inline-flex size-11 shrink-0 items-center justify-center rounded-full bg-surface-2 text-sm font-bold" aria-hidden="true">
        {initials(party.name)}
      </span>
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="truncate font-semibold">{party.name}</span>
        <span className="flex flex-wrap items-center gap-x-2 text-sm text-muted">
          <span>{t(`parties.types.${party.type}`)}</span>
          {party.phone && (
            <span dir="ltr" className="tabular-nums">
              {party.phone}
            </span>
          )}
          {!party.inBalanceSheet && <span className="font-semibold">{t('parties.outOfBalanceSheet')}</span>}
          {party.isArchived && <span className="font-semibold">{t('parties.archived')}</span>}
        </span>
      </span>
      {balances.length > 0 && (
        <span className="flex shrink-0 flex-col items-end gap-1">
          {balances.map((b) => (
            <BalanceAmount key={b.agencyId} balance={b.balance} currency={b.currency} agencyCode={several ? agencyCodes.get(b.agencyId) : undefined} />
          ))}
        </span>
      )}
      <Icon name="chevron" className="size-5 text-muted" />
    </Link>
  );
}
