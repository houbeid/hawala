import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { addDays } from '@/shared/format/dates';
import { getDailyRate, listDailyRates, setDailyRate } from './api';
import type { DailyRate, SetDailyRateRequest } from './types';

export const rateKeys = {
  all: ['rates'] as const,
  day: (date: string) => ['rates', 'day', date] as const,
  range: (from: string, to: string) => ['rates', 'range', from, to] as const,
};

export function useDailyRate(date: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: rateKeys.day(date), queryFn: () => getDailyRate(session.http, date) });
}

/** Jours en arrière où l'écran cherche le dernier taux connu (le serveur, lui, cherche sans limite). */
const FALLBACK_DAYS = 60;

export interface ApplicableRate {
  status: 'loading' | 'error' | 'ready';
  /** Taux du jour, sinon le dernier taux connu (son `date` dit lequel) ; null s'il n'y en a aucun. */
  data: DailyRate | null;
}

/**
 * Taux appliqué à un transfert de ce jour (décision du patron, 2 novembre 2026) : celui du jour s'il est fixé, sinon le
 * dernier taux connu, que le serveur reprend aussi. L'écran l'affiche à titre indicatif ; le serveur fait foi.
 */
export function useApplicableRate(date: string): ApplicableRate {
  const session = useSessionManager();
  const day = useDailyRate(date);
  const from = addDays(date, -FALLBACK_DAYS);
  const earlier = useQuery({
    queryKey: rateKeys.range(from, date),
    queryFn: () => listDailyRates(session.http, from, date),
    enabled: day.isSuccess && day.data === null,
  });
  if (day.isPending || (day.data === null && earlier.isPending)) return { status: 'loading', data: null };
  if (day.isError || earlier.isError) return { status: 'error', data: null };
  return { status: 'ready', data: day.data ?? earlier.data?.[0] ?? null };
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
