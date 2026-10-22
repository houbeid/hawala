import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { useSessionManager } from '@/shared/auth/hooks';
import { getTransfer, getTransferDashboard, getTransferHistory, getTransferSummary, listTransfers } from './api';
import type { TransferFilters } from './types';

export const transferKeys = {
  all: ['transfers'] as const,
  list: (filters: TransferFilters & { agencyId?: string }) => ['transfers', 'list', filters] as const,
  summary: (params: { agencyId?: string; from?: string; to?: string; search?: string }) => ['transfers', 'summary', params] as const,
};

/** La page précédente reste affichée pendant le chargement de la suivante (pas d'écran vide entre deux filtres). */
export function useTransferList(filters: TransferFilters & { agencyId?: string }) {
  const session = useSessionManager();
  return useQuery({
    queryKey: transferKeys.list(filters),
    queryFn: () => listTransfers(session.http, filters),
    placeholderData: keepPreviousData,
  });
}

export function useTransferSummary(params: { agencyId?: string; from?: string; to?: string; search?: string }, enabled: boolean) {
  const session = useSessionManager();
  return useQuery({
    queryKey: transferKeys.summary(params),
    queryFn: () => getTransferSummary(session.http, params),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useTransferDashboard(params: { from?: string; to?: string }) {
  const session = useSessionManager();
  return useQuery({
    queryKey: ['transfers', 'dashboard', params] as const,
    queryFn: () => getTransferDashboard(session.http, params),
    placeholderData: keepPreviousData,
  });
}

export function useTransfer(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: ['transfers', 'detail', id] as const, queryFn: () => getTransfer(session.http, id) });
}

export function useTransferHistory(id: string, enabled = true) {
  const session = useSessionManager();
  return useQuery({ queryKey: ['transfers', 'history', id] as const, queryFn: () => getTransferHistory(session.http, id), enabled });
}

/** Après toute action sur un transfert : fiche, historique, listes et cartes sont relues. */
export function useTransferAction<TArgs, TResult>(action: (http: HttpClient, args: TArgs) => Promise<TResult>) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: TArgs) => action(session.http, args),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: transferKeys.all }),
  });
}
