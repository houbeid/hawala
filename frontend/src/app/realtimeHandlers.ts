import type { QueryClient } from '@tanstack/react-query';
import { balanceKeys } from '@/features/balance/queries';
import { budgetKeys } from '@/features/budget/queries';
import { partyKeys } from '@/features/parties/queries';
import { profitKeys } from '@/features/profit/queries';
import { rateKeys } from '@/features/rates/queries';
import { transferKeys } from '@/features/transfers/queries';
import { treasuryKeys } from '@/features/treasury/queries';
import type { RealtimeHandlers } from '@/shared/realtime/RealtimeProvider';

// Ce que l'application relit quand le serveur annonce un changement (le message ne porte aucun montant) :
// - un transfert (créé, livré, modifié, annulé, code) : listes, fiche, historique, cartes et tableau de bord, mais aussi
//   ce qu'il change ailleurs : bénéfice, budget (non livrés), soldes des caisses et du tiers d'un crédit, bilan ;
// - un taux du jour : les taux (écran Taux, création d'un transfert).
// Seules les données déjà en cache et affichées sont relues : TanStack Query ne relance que les requêtes actives.

export function createRealtimeHandlers(queryClient: QueryClient): RealtimeHandlers {
  const refresh = (...keys: readonly (readonly unknown[])[]) => {
    for (const queryKey of keys) void queryClient.invalidateQueries({ queryKey });
  };
  return {
    onTransferChanged: () => refresh(transferKeys.all, profitKeys.all, budgetKeys.all, treasuryKeys.all, partyKeys.all, balanceKeys.all),
    onRatesChanged: () => refresh(rateKeys.all),
  };
}
