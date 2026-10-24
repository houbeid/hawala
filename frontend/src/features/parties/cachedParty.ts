import type { QueryClient } from '@tanstack/react-query';
import type { PartyList, PartySummary } from './types';

/**
 * Un tiers lu dans le cache (fiche ou listes déjà chargées, dont la liste gardée pour le hors ligne), pour reprendre
 * une saisie (« Corriger » une opération rejetée). Absent du cache : un tiers minimal avec son identifiant.
 */
export function cachedParty(queryClient: QueryClient, id: string): PartySummary {
  const detail = queryClient.getQueryData<PartySummary>(['parties', 'detail', id]);
  if (detail) return detail;
  for (const [, list] of queryClient.getQueriesData<PartyList>({ queryKey: ['parties', 'list'] })) {
    const found = list?.items.find((party) => party.id === id);
    if (found) return found;
  }
  return {
    id,
    name: '—',
    type: 'Other',
    accountCode: null,
    phone: null,
    nni: null,
    address: null,
    note: null,
    inBalanceSheet: true,
    isArchived: false,
    balances: null,
  };
}
