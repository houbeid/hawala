import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { getProfitDay, getProfitReport, saveProfitSnapshot } from './api';

export const profitKeys = {
  all: ['profit'] as const,
  report: (params: { agencyId?: string; from?: string; to?: string }) => ['profit', 'report', params] as const,
  day: (date: string, agencyId: string | undefined) => ['profit', 'day', date, agencyId ?? 'all'] as const,
};

export function useProfitReport(params: { agencyId?: string; from?: string; to?: string }) {
  const session = useSessionManager();
  return useQuery({
    queryKey: profitKeys.report(params),
    queryFn: () => getProfitReport(session.http, params),
    placeholderData: keepPreviousData,
  });
}

export function useProfitDay(date: string, agencyId: string | undefined, enabled: boolean) {
  const session = useSessionManager();
  return useQuery({ queryKey: profitKeys.day(date, agencyId), queryFn: () => getProfitDay(session.http, date, agencyId), enabled });
}

/** « Enregistrer le bénéfice du jour » : rapport relu (colonne « Enregistré »). */
export function useSaveProfit() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (agencyId: string | null) => saveProfitSnapshot(session.http, agencyId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: profitKeys.all }),
  });
}
