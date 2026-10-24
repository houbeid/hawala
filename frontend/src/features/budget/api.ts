import type { HttpClient } from '@/shared/api/http';
import type { CashBudgetSnapshot, CashBudgetView, CoherenceCheckView } from './types';

/** Budget en direct (cash.budget.read) ; le Superviseur doit donner l'agence. */
export function getCashBudget(http: HttpClient, agencyId: string): Promise<CashBudgetView> {
  return http.get<CashBudgetView>('/cash-budget', { query: { agencyId } });
}

/** Contrôle de cohérence depuis le dernier budget enregistré (ou minuit, s'il n'y en a pas). */
export function getCoherenceCheck(http: HttpClient, agencyId: string): Promise<CoherenceCheckView> {
  return http.get<CoherenceCheckView>('/cash-budget/coherence', { query: { agencyId } });
}

/** Enregistre le budget du jour (cash.budget.save) : chiffres calculés par le serveur ; remplace celui du même jour. */
export function saveCashBudget(http: HttpClient, agencyId: string): Promise<CashBudgetSnapshot> {
  return http.post<CashBudgetSnapshot>('/cash-budget/snapshots', { agencyId });
}

/** Budgets enregistrés (30 derniers jours par défaut). */
export function listCashBudgetSnapshots(http: HttpClient, agencyId: string): Promise<CashBudgetSnapshot[]> {
  return http.get<CashBudgetSnapshot[]>('/cash-budget/snapshots', { query: { agencyId } });
}
