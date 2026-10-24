import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { useSessionManager } from '@/shared/auth/hooks';
import { getTreasuryAccount, getTreasuryAccountStatement, listTreasuryAccounts } from './api';

export const treasuryKeys = {
  all: ['treasury'] as const,
  accounts: (agencyId: string | undefined, includeInactive: boolean) => ['treasury', 'accounts', agencyId ?? 'all', includeInactive] as const,
  account: (id: string) => ['treasury', 'account', id] as const,
  statement: (id: string, params: { from?: string; to?: string }) => ['treasury', 'statement', id, params] as const,
};

/** Méthodes de paiement d'une agence (comptes actifs) : choix d'une répartition de paiement ou de livraison. */
export function useTreasuryAccounts(agencyId: string | undefined, options: { includeInactive?: boolean; enabled?: boolean } = {}) {
  const session = useSessionManager();
  const includeInactive = options.includeInactive ?? false;
  return useQuery({
    queryKey: treasuryKeys.accounts(agencyId, includeInactive),
    queryFn: () => listTreasuryAccounts(session.http, { agencyId, includeInactive }),
    enabled: options.enabled ?? true,
  });
}

/** Création, renommage, désactivation, réactivation : toutes les listes de comptes sont relues après succès. */
export function useTreasuryAction<TArgs, TResult>(action: (http: HttpClient, args: TArgs) => Promise<TResult>) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: TArgs) => action(session.http, args),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: treasuryKeys.all }),
  });
}

export function useTreasuryAccount(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: treasuryKeys.account(id), queryFn: () => getTreasuryAccount(session.http, id) });
}

export function useTreasuryStatement(id: string, params: { from?: string; to?: string }, enabled = true) {
  const session = useSessionManager();
  return useQuery({
    queryKey: treasuryKeys.statement(id, params),
    queryFn: () => getTreasuryAccountStatement(session.http, id, params),
    enabled,
    placeholderData: keepPreviousData,
  });
}
