import type { HttpClient } from '@/shared/api/http';
import type { BalanceOverview, BalanceSnapshot, CaisseCapital, SetCapitalRequest } from './types';

/** Bilan en direct des deux caisses (balance.view) : lourd (tous les soldes), jamais appelé à l'ouverture de l'app. */
export function getBalance(http: HttpClient): Promise<BalanceOverview> {
  return http.get<BalanceOverview>('/balance');
}

/** Enregistre le bilan du jour d'une caisse (balance.manage). */
export function saveBalance(http: HttpClient, agencyId: string): Promise<BalanceSnapshot> {
  return http.post<BalanceSnapshot>('/balance/snapshots', { agencyId });
}

/** Bilans enregistrés des deux caisses, 30 derniers jours par défaut. */
export function listBalanceSnapshots(http: HttpClient): Promise<BalanceSnapshot[]> {
  return http.get<BalanceSnapshot[]>('/balance/snapshots');
}

export function getCapital(http: HttpClient): Promise<CaisseCapital[]> {
  return http.get<CaisseCapital[]>('/balance/capital');
}

export function setCapital(http: HttpClient, request: SetCapitalRequest): Promise<void> {
  return http.put<void>('/balance/capital', request);
}
