import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import {
  getBalance,
  getCapital,
  getGeneralBalance,
  getGeneralCapital,
  listBalanceSnapshots,
  listGeneralSnapshots,
  saveBalance,
  saveGeneralBalance,
  setCapital,
  setGeneralCapital,
} from './api';
import type { SetCapitalRequest, SetGeneralCapitalRequest } from './types';

export const balanceKeys = {
  all: ['balance'] as const,
  live: ['balance', 'live'] as const,
  snapshots: ['balance', 'snapshots'] as const,
  capital: ['balance', 'capital'] as const,
  general: (rate: number | null) => ['balance', 'general', 'live', rate] as const,
  generalSnapshots: ['balance', 'general', 'snapshots'] as const,
  generalCapital: ['balance', 'general', 'capital'] as const,
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

/** Bilan général au taux appliqué ; rien n'est demandé tant qu'aucun taux valable n'est appliqué. */
export function useGeneralBalance(rate: number | null) {
  const session = useSessionManager();
  return useQuery({
    queryKey: balanceKeys.general(rate),
    queryFn: () => getGeneralBalance(session.http, rate!),
    enabled: rate !== null,
  });
}

export function useGeneralSnapshots() {
  const session = useSessionManager();
  return useQuery({ queryKey: balanceKeys.generalSnapshots, queryFn: () => listGeneralSnapshots(session.http) });
}

export function useGeneralCapital() {
  const session = useSessionManager();
  return useQuery({ queryKey: balanceKeys.generalCapital, queryFn: () => getGeneralCapital(session.http) });
}

export function useSaveGeneralBalance() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (rate: number) => saveGeneralBalance(session.http, rate),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: balanceKeys.all }),
  });
}

export function useSetGeneralCapital() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SetGeneralCapitalRequest) => setGeneralCapital(session.http, request),
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
