import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { getTransferDashboard, getTransferSummary, listTransfers } from './api';
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
