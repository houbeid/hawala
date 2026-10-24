import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { budgetKeys } from '@/features/budget/queries';
import { partyKeys } from '@/features/parties/queries';
import { treasuryKeys } from '@/features/treasury/queries';
import { useSessionManager } from '@/shared/auth/hooks';
import { listBalanceAdjustments, listCashMovements } from './api';
import type { CashListParams } from './types';

export const cashKeys = {
  all: ['cash'] as const,
  list: (params: CashListParams) => ['cash', 'list', params] as const,
  adjustments: (params: { agencyId?: string; from?: string; to?: string }) => ['cash', 'adjustments', params] as const,
};

export function useCashMovements(params: CashListParams, enabled = true) {
  const session = useSessionManager();
  return useQuery({
    queryKey: cashKeys.list(params),
    queryFn: () => listCashMovements(session.http, params),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useBalanceAdjustments(params: { agencyId?: string; from?: string; to?: string }, enabled = true) {
  const session = useSessionManager();
  return useQuery({
    queryKey: cashKeys.adjustments(params),
    queryFn: () => listBalanceAdjustments(session.http, params),
    enabled,
  });
}

/**
 * Action sur la caisse : listes de mouvements et d'ajustements relues après succès, ainsi que les soldes des comptes
 * de trésorerie et des tiers, et le budget (un mouvement, une annulation ou un ajustement change leurs soldes).
 */
export function useCashAction<TArgs, TResult>(action: (http: HttpClient, args: TArgs) => Promise<TResult>) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: TArgs) => action(session.http, args),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: cashKeys.all });
      await queryClient.invalidateQueries({ queryKey: treasuryKeys.all });
      await queryClient.invalidateQueries({ queryKey: partyKeys.all });
      await queryClient.invalidateQueries({ queryKey: budgetKeys.all });
    },
  });
}
