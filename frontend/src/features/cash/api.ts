import type { HttpClient } from '@/shared/api/http';
import { IDEMPOTENCY_HEADER } from '@/shared/api/idempotency';
import type {
  BalanceAdjustment,
  CancelCashMovementRequest,
  CashListParams,
  CashMovementList,
  CreateBalanceAdjustmentRequest,
  CreateCashMovementRequest,
  CreatedCashMovement,
} from './types';

/** Mouvements de caisse (cash.budget.read), périmètre de l'agence côté serveur ; aujourd'hui par défaut. */
export function listCashMovements(http: HttpClient, params: CashListParams): Promise<CashMovementList> {
  return http.get<CashMovementList>('/cash-movements', { query: params });
}

/** Export Excel (export + cash.budget.read) : mêmes filtres que la liste, sans la page ; `lang` : langue des en-têtes. */
export function exportCashMovements(http: HttpClient, params: Omit<CashListParams, 'page'>, lang: 'fr' | 'ar'): Promise<Blob> {
  return http.blob('/cash-movements/export', { query: { ...params, lang } });
}

/** Création (cash.create) ; la même clé renvoie le mouvement déjà créé (200, replayed) au lieu d'en créer un second. */
export function createCashMovement(http: HttpClient, request: CreateCashMovementRequest, idempotencyKey: string): Promise<CreatedCashMovement> {
  return http.post<CreatedCashMovement>('/cash-movements', request, { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } });
}

/** Annulation (cash.manage) : le mouvement reste dans la liste, barré, et ne compte plus dans les totaux. */
export function cancelCashMovement(http: HttpClient, id: string, request: CancelCashMovementRequest): Promise<void> {
  return http.post<void>(`/cash-movements/${id}/cancel`, request);
}

/** Ajustement de solde (cash.manage) contre « Écarts et ajustements », sur un compte de trésorerie ou un tiers. */
export function createBalanceAdjustment(http: HttpClient, request: CreateBalanceAdjustmentRequest): Promise<{ id: string }> {
  return http.post<{ id: string }>('/balance-adjustments', request);
}

export function listBalanceAdjustments(http: HttpClient, params: { agencyId?: string; from?: string; to?: string }): Promise<BalanceAdjustment[]> {
  return http.get<BalanceAdjustment[]>('/balance-adjustments', { query: params });
}
