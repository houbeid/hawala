import type { HttpClient } from '@/shared/api/http';
import type {
  CreateTreasuryAccountRequest,
  CreatedTreasuryAccount,
  RenameTreasuryAccountRequest,
  TreasuryAccount,
  TreasuryAccountStatement,
} from './types';

/** Comptes de trésorerie visibles (périmètre de l'agence côté serveur) ; actifs seulement par défaut. */
export function listTreasuryAccounts(
  http: HttpClient,
  params: { agencyId?: string; includeInactive?: boolean } = {},
): Promise<TreasuryAccount[]> {
  return http.get<TreasuryAccount[]>('/treasury-accounts', { query: params });
}

/** Crée le compte et son compte au grand livre (treasury.manage) ; nom unique dans l'agence. */
export function createTreasuryAccount(http: HttpClient, request: CreateTreasuryAccountRequest): Promise<CreatedTreasuryAccount> {
  return http.post<CreatedTreasuryAccount>('/treasury-accounts', request);
}

export function renameTreasuryAccount(http: HttpClient, id: string, request: RenameTreasuryAccountRequest): Promise<void> {
  return http.put<void>(`/treasury-accounts/${id}`, request);
}

/** Solde nul seulement (Treasury.Account.BalanceNotZero sinon) ; ferme aussi le compte au grand livre. */
export function deactivateTreasuryAccount(http: HttpClient, id: string): Promise<void> {
  return http.post<void>(`/treasury-accounts/${id}/deactivate`);
}

export function reactivateTreasuryAccount(http: HttpClient, id: string): Promise<void> {
  return http.post<void>(`/treasury-accounts/${id}/reactivate`);
}

/** Fiche d'un compte ; 404 hors de l'agence du compte connecté. */
export function getTreasuryAccount(http: HttpClient, id: string): Promise<TreasuryAccount> {
  return http.get<TreasuryAccount>(`/treasury-accounts/${id}`);
}

/** Relevé lu au grand livre (cash.budget.read) : tous les mouvements du compte sur la période. */
export function getTreasuryAccountStatement(http: HttpClient, id: string, params: { from?: string; to?: string }): Promise<TreasuryAccountStatement> {
  return http.get<TreasuryAccountStatement>(`/treasury-accounts/${id}/statement`, { query: params });
}
