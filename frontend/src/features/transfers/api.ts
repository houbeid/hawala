import type { HttpClient } from '@/shared/api/http';
import { IDEMPOTENCY_HEADER } from '@/shared/api/idempotency';
import type {
  CreatedTransfer,
  CreateTransferRequest,
  TransferDashboard,
  TransferFilters,
  TransferList,
  TransferSummary,
} from './types';

/** Liste paginée (50 par page), période sur la date d'opération (aujourd'hui par défaut côté serveur). */
export function listTransfers(http: HttpClient, filters: TransferFilters & { agencyId?: string }): Promise<TransferList> {
  return http.get<TransferList>('/transfers', {
    query: {
      from: filters.from,
      to: filters.to,
      status: filters.status,
      direction: filters.direction,
      agencyId: filters.agencyId,
      search: filters.search,
      page: filters.page,
    },
  });
}

/** Cartes de l'écran pour une agence (obligatoire pour le Superviseur, imposée pour les autres comptes). */
export function getTransferSummary(
  http: HttpClient,
  params: { agencyId?: string; from?: string; to?: string; search?: string },
): Promise<TransferSummary> {
  return http.get<TransferSummary>('/transfers/summary', { query: params });
}

/** Tableau de bord du Superviseur (balance.view) : une entrée par caisse, aujourd'hui par défaut. */
export function getTransferDashboard(http: HttpClient, params: { from?: string; to?: string }): Promise<TransferDashboard> {
  return http.get<TransferDashboard>('/transfers/dashboard', { query: params });
}

/** Crée un transfert. La même clé d'idempotence renvoie le transfert déjà créé (200), sans code. */
export function createTransfer(http: HttpClient, request: CreateTransferRequest, idempotencyKey: string): Promise<CreatedTransfer> {
  return http.post<CreatedTransfer>('/transfers', request, { headers: { [IDEMPOTENCY_HEADER]: idempotencyKey } });
}
