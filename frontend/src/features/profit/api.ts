import type { HttpClient } from '@/shared/api/http';
import type { ProfitDayDetail, ProfitReport, ProfitSnapshot } from './types';

/** Bénéfice d'une période (profit.view) ; aujourd'hui par défaut ; sans agence : les deux. */
export function getProfitReport(http: HttpClient, params: { agencyId?: string; from?: string; to?: string }): Promise<ProfitReport> {
  return http.get<ProfitReport>('/profit', { query: params });
}

/** Export Excel (export + profit.view) : une feuille par jour, une par transfert. */
export function exportProfit(http: HttpClient, params: { agencyId?: string; from?: string; to?: string }, lang: 'fr' | 'ar'): Promise<Blob> {
  return http.blob('/profit/export', { query: { ...params, lang } });
}

export function getProfitDay(http: HttpClient, date: string, agencyId: string | undefined): Promise<ProfitDayDetail> {
  return http.get<ProfitDayDetail>(`/profit/days/${date}`, { query: { agencyId } });
}

/** Enregistre le bénéfice du jour courant (une agence ou les deux), remplacé s'il l'était déjà. */
export function saveProfitSnapshot(http: HttpClient, agencyId: string | null): Promise<ProfitSnapshot> {
  return http.post<ProfitSnapshot>('/profit/snapshots', { agencyId });
}
