import { useQuery } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { listTreasuryAccounts } from './api';

export const treasuryKeys = {
  all: ['treasury'] as const,
  accounts: (agencyId: string | undefined, includeInactive: boolean) => ['treasury', 'accounts', agencyId ?? 'all', includeInactive] as const,
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
