import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { getBalance, getCapital, listBalanceSnapshots, saveBalance, setCapital } from './api';
import type { SetCapitalRequest } from './types';

export const balanceKeys = {
  all: ['balance'] as const,
  live: ['balance', 'live'] as const,
  snapshots: ['balance', 'snapshots'] as const,
  capital: ['balance', 'capital'] as const,
};

export function useBalance() {
  const session = useSessionManager();
  return useQuery({ queryKey: balanceKeys.live, queryFn: () => getBalance(session.http) });
}

export function useBalanceSnapshots() {
  const session = useSessionManager();
  return useQuery({ queryKey: balanceKeys.snapshots, queryFn: () => listBalanceSnapshots(session.http) });
}

export function useCapital(enabled = true) {
  const session = useSessionManager();
  return useQuery({ queryKey: balanceKeys.capital, queryFn: () => getCapital(session.http), enabled });
}

/** Bilan, historique et capital relus après un enregistrement ou une saisie de capital. */
export function useSaveBalance() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (agencyId: string) => saveBalance(session.http, agencyId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: balanceKeys.all }),
  });
}

export function useSetCapital() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SetCapitalRequest) => setCapital(session.http, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: balanceKeys.all }),
  });
}
