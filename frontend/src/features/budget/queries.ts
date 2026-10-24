import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { getCashBudget, getCoherenceCheck, listCashBudgetSnapshots, saveCashBudget } from './api';

export const budgetKeys = {
  all: ['budget'] as const,
  live: (agencyId: string) => ['budget', 'live', agencyId] as const,
  coherence: (agencyId: string) => ['budget', 'coherence', agencyId] as const,
  snapshots: (agencyId: string) => ['budget', 'snapshots', agencyId] as const,
};

export function useCashBudget(agencyId: string | undefined) {
  const session = useSessionManager();
  return useQuery({
    queryKey: budgetKeys.live(agencyId ?? ''),
    queryFn: () => getCashBudget(session.http, agencyId!),
    enabled: Boolean(agencyId),
  });
}

export function useCoherenceCheck(agencyId: string | undefined) {
  const session = useSessionManager();
  return useQuery({
    queryKey: budgetKeys.coherence(agencyId ?? ''),
    queryFn: () => getCoherenceCheck(session.http, agencyId!),
    enabled: Boolean(agencyId),
  });
}

export function useBudgetSnapshots(agencyId: string | undefined) {
  const session = useSessionManager();
  return useQuery({
    queryKey: budgetKeys.snapshots(agencyId ?? ''),
    queryFn: () => listCashBudgetSnapshots(session.http, agencyId!),
    enabled: Boolean(agencyId),
  });
}

/** Enregistrer le budget du jour : budget, contrôle (nouvelle période) et historique relus après succès. */
export function useSaveCashBudget() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (agencyId: string) => saveCashBudget(session.http, agencyId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: budgetKeys.all }),
  });
}
