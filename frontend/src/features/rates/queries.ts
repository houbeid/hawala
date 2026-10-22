import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { getDailyRate, listDailyRates, setDailyRate } from './api';
import type { SetDailyRateRequest } from './types';

export const rateKeys = {
  all: ['rates'] as const,
  day: (date: string) => ['rates', 'day', date] as const,
  range: (from: string, to: string) => ['rates', 'range', from, to] as const,
};

export function useDailyRate(date: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: rateKeys.day(date), queryFn: () => getDailyRate(session.http, date) });
}

export function useRateHistory(from: string, to: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: rateKeys.range(from, to), queryFn: () => listDailyRates(session.http, from, to) });
}

/** Après un enregistrement, tous les taux affichés sont relus (fiche du jour, historique, veille). */
export function useSetDailyRate(date: string) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SetDailyRateRequest) => setDailyRate(session.http, date, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: rateKeys.all }),
  });
}
