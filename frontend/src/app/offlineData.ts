import { dehydrate, hydrate, type QueryClient } from '@tanstack/react-query';
import { listParties } from '@/features/parties/api';
import { partyKeys } from '@/features/parties/queries';
import { getDailyRate } from '@/features/rates/api';
import { rateKeys } from '@/features/rates/queries';
import { listTreasuryAccounts } from '@/features/treasury/api';
import { treasuryKeys } from '@/features/treasury/queries';
import { listTransfers } from '@/features/transfers/api';
import { transferKeys } from '@/features/transfers/queries';
import type { Transfer, TransferList } from '@/features/transfers/types';
import type { HttpClient } from '@/shared/api/http';
import type { CurrentAccount } from '@/shared/auth/types';
import { addDays, todayIso } from '@/shared/format/dates';
import { isFresh, QUERIES_KEY, type KeyValueStore } from '@/shared/offline/offlineStore';

// Lecture hors ligne (décisions du 24 octobre 2026). Deux pièces :
// 1. préchargement, à chaque connexion et retour du réseau : taux du jour, comptes de trésorerie, tiers autorisés,
//    transferts des 7 derniers jours (avec les mêmes clés que les écrans, pour qu'ils les trouvent tels quels) ;
// 2. sauvegarde du cache de ces données dans le stockage du téléphone, rechargée à la réouverture de l'app si elle
//    appartient au même compte et date de moins de 12 h. Rien d'autre (caisse, budget, bénéfice, bilan, comptes,
//    audit) n'est gardé : ces écrans exigent le réseau.

/** Racines des clés de cache gardées sur le téléphone. */
export const OFFLINE_ROOTS: readonly string[] = ['rates', 'treasury', 'parties', 'transfers', 'organization'];

/** Transferts des 7 derniers jours : 4 pages de 50 au plus. */
const RECENT_DAYS = 7;
const RECENT_PAGES = 4;
const SAVE_DELAY_MS = 1_500;

interface SavedQueries {
  accountId: string;
  savedAt: string;
  state: ReturnType<typeof dehydrate>;
}

/** Agences à précharger : celle d'un agent, ou « toutes » puis chacune pour le Superviseur. */
function scopes(account: CurrentAccount, agencyIds: readonly string[]): (string | undefined)[] {
  return account.agency ? [account.agency.id] : [undefined, ...agencyIds];
}

export async function prefetchOfflineData(
  queryClient: QueryClient,
  http: HttpClient,
  account: CurrentAccount,
  agencyIds: readonly string[],
  today: string = todayIso(),
): Promise<void> {
  const from = addDays(today, -(RECENT_DAYS - 1));
  const work: Promise<unknown>[] = [
    queryClient.prefetchQuery({ queryKey: rateKeys.day(today), queryFn: () => getDailyRate(http, today) }),
  ];

  for (const agencyId of scopes(account, agencyIds)) {
    work.push(
      queryClient.prefetchQuery({
        queryKey: treasuryKeys.accounts(agencyId, true),
        queryFn: () => listTreasuryAccounts(http, { agencyId, includeInactive: true }),
      }),
      queryClient.prefetchQuery({ queryKey: partyKeys.list({ agencyId }), queryFn: () => listParties(http, { agencyId }) }),
      // Liste sans filtre du choix d'un tiers (crédit, encaissement) : la recherche hors ligne se fait dedans.
      queryClient.prefetchQuery({ queryKey: partyKeys.list({}), queryFn: () => listParties(http, {}) }),
      queryClient.prefetchQuery({ queryKey: transferKeys.list({ agencyId }), queryFn: () => listTransfers(http, { agencyId }) }),
      prefetchRecentTransfers(queryClient, http, agencyId, from, today),
    );
    if (agencyId) {
      work.push(
        queryClient.prefetchQuery({
          queryKey: treasuryKeys.accounts(agencyId, false),
          queryFn: () => listTreasuryAccounts(http, { agencyId, includeInactive: false }),
        }),
      );
    }
  }
  await Promise.all(work);
}

/** Pages des 7 derniers jours ; chaque transfert lu remplit aussi sa fiche (ouvrable hors ligne). */
async function prefetchRecentTransfers(
  queryClient: QueryClient,
  http: HttpClient,
  agencyId: string | undefined,
  from: string,
  to: string,
): Promise<void> {
  for (let page = 1; page <= RECENT_PAGES; page++) {
    const filters = page === 1 ? { from, to, agencyId } : { from, to, agencyId, page };
    let list: TransferList;
    try {
      list = await queryClient.fetchQuery({ queryKey: transferKeys.list(filters), queryFn: () => listTransfers(http, filters) });
    } catch {
      return;
    }
    for (const transfer of list.items) seedTransfer(queryClient, transfer);
    if (page * list.pageSize >= list.totalCount) return;
  }
}

function seedTransfer(queryClient: QueryClient, transfer: Transfer): void {
  const key = ['transfers', 'detail', transfer.id] as const;
  if (!queryClient.getQueryData(key)) queryClient.setQueryData(key, transfer);
}

/**
 * Recharge le cache gardé (même compte, moins de 12 h), puis le sauvegarde à chaque changement (regroupé toutes les
 * 1,5 s). `isActive` : la session est toujours celle de ce compte (sinon on n'écrit plus rien). Renvoie l'arrêt.
 */
export function startOfflineCache(
  queryClient: QueryClient,
  store: KeyValueStore,
  accountId: string,
  isActive: () => boolean,
): () => void {
  let stopped = false;
  let timer: ReturnType<typeof setTimeout> | null = null;

  void store.get<SavedQueries>(QUERIES_KEY).then((saved) => {
    if (stopped || !saved || saved.accountId !== accountId || !isFresh(saved.savedAt)) return;
    hydrate(queryClient, saved.state);
  });

  const save = () => {
    timer = null;
    if (stopped || !isActive()) return;
    const state = dehydrate(queryClient, {
      shouldDehydrateQuery: (query) =>
        query.state.status === 'success' && OFFLINE_ROOTS.includes(String(query.queryKey[0])),
    });
    const saved: SavedQueries = { accountId, savedAt: new Date().toISOString(), state };
    void store.set(QUERIES_KEY, saved);
  };

  const unsubscribe = queryClient.getQueryCache().subscribe((event) => {
    if (event.type !== 'updated' && event.type !== 'added' && event.type !== 'removed') return;
    if (!timer) timer = setTimeout(save, SAVE_DELAY_MS);
  });

  return () => {
    stopped = true;
    if (timer) clearTimeout(timer);
    unsubscribe();
  };
}
