import type { HttpClient } from '@/shared/api/http';
import type { TreasuryAccount } from './types';

/** Comptes de trésorerie visibles (périmètre de l'agence côté serveur) ; actifs seulement par défaut. */
export function listTreasuryAccounts(
  http: HttpClient,
  params: { agencyId?: string; includeInactive?: boolean } = {},
): Promise<TreasuryAccount[]> {
  return http.get<TreasuryAccount[]>('/treasury-accounts', { query: params });
}
