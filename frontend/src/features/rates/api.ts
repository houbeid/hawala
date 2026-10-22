import { isApiError } from '@/shared/api/errors';
import type { HttpClient } from '@/shared/api/http';
import type { DailyRate, SetDailyRateRequest } from './types';

export const RATE_NOT_SET_CODE = 'Rates.Rate.NotFound';

/** Taux d'une date ; null si aucun taux n'est fixé ce jour-là (404 Rates.Rate.NotFound, état normal). */
export async function getDailyRate(http: HttpClient, date: string): Promise<DailyRate | null> {
  try {
    return await http.get<DailyRate>(`/rates/${date}`);
  } catch (error) {
    if (isApiError(error) && error.code === RATE_NOT_SET_CODE) return null;
    throw error;
  }
}

/** Taux enregistrés entre deux dates incluses (seulement les jours qui en ont un). */
export function listDailyRates(http: HttpClient, from: string, to: string): Promise<DailyRate[]> {
  return http.get<DailyRate[]>('/rates', { query: { from, to } });
}

/** Superviseur seulement (rates.set_daily) : crée ou change le taux de la date. 204. */
export function setDailyRate(http: HttpClient, date: string, request: SetDailyRateRequest): Promise<void> {
  return http.put<void>(`/rates/${date}`, request);
}
