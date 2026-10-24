import type { HttpClient } from '@/shared/api/http';
import { IDEMPOTENCY_HEADER } from '@/shared/api/idempotency';
import type {
  CancelTransferRequest,
  CreatedTransfer,
  CreateTransferRequest,
  DeliveredTransfer,
  DeliverTransferRequest,
  DeliverWithoutCodeRequest,
  EditTransferAmountsRequest,
  EditTransferTextsRequest,
  NewWithdrawalCode,
  Transfer,
  TransferEvent,
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

/** Fiche d'un transfert (404 hors périmètre). */
export function getTransfer(http: HttpClient, id: string): Promise<Transfer> {
  return http.get<Transfer>(`/transfers/${id}`);
}

/** Historique, champs masqués selon les droits du lecteur, avec le nom de l'auteur. */
export function getTransferHistory(http: HttpClient, id: string): Promise<TransferEvent[]> {
  return http.get<TransferEvent[]>(`/transfers/${id}/history`);
}

/** Textes (agence d'envoi ou Superviseur) ; 409 Transfers.Transfer.VersionConflict si la version est périmée. */
export function editTransferTexts(http: HttpClient, id: string, request: EditTransferTextsRequest): Promise<void> {
  return http.put<void>(`/transfers/${id}`, request);
}

/** Annulation (transfers.cancel, Superviseur) : contre-passation de l'écriture de création. 204. */
export function cancelTransfer(http: HttpClient, id: string, request: CancelTransferRequest): Promise<void> {
  return http.post<void>(`/transfers/${id}/cancel`, request);
}

/** Déblocage du code après 5 essais faux (transfers.manage_code, Superviseur). 204. */
export function unlockTransferCode(http: HttpClient, id: string, reason: string | null): Promise<void> {
  return http.post<void>(`/transfers/${id}/unlock-code`, { reason });
}

/** Nouveau code (agence d'envoi ou Superviseur) : l'ancien ne marche plus ; ne débloque pas. */
export function newTransferCode(http: HttpClient, id: string): Promise<NewWithdrawalCode> {
  return http.post<NewWithdrawalCode>(`/transfers/${id}/new-code`);
}

/**
 * Export Excel de la liste (permission export) : mêmes filtres et même périmètre que la liste, sans la page ;
 * `lang` donne la langue des en-têtes.
 */
export function exportTransfers(
  http: HttpClient,
  filters: Omit<TransferFilters, 'page'> & { agencyId?: string },
  lang: 'fr' | 'ar',
): Promise<Blob> {
  return http.blob('/transfers/export', {
    query: {
      from: filters.from,
      to: filters.to,
      status: filters.status,
      direction: filters.direction,
      agencyId: filters.agencyId,
      search: filters.search,
      lang,
    },
  });
}

/**
 * Reçu PDF de l'expéditeur (règles du 24 octobre 2026). Avec le code : POST, le code dans le corps (jamais dans
 * l'adresse), seulement pour le compte qui vient de l'obtenir ; sans : GET, duplicata (agence d'envoi ou Superviseur).
 */
export function getTransferReceipt(http: HttpClient, id: string, code?: string): Promise<Blob> {
  return code === undefined
    ? http.blob(`/transfers/${id}/receipt`)
    : http.blob(`/transfers/${id}/receipt`, { method: 'POST', body: { code } });
}

/** Modification d'argent (Superviseur) : 204 ; 409 VersionConflict si la fiche a changé. */
export function editTransferAmounts(http: HttpClient, id: string, request: EditTransferAmountsRequest): Promise<void> {
  return http.put<void>(`/transfers/${id}/amounts`, request);
}

/**
 * Livraison avec le code : le serveur vérifie le code ET passe l'écriture en une fois. Code faux : 400
 * Transfers.Delivery.WrongCode + remainingAttempts ; au 5e : 409 Transfers.Delivery.CodeLocked.
 */
export function deliverTransfer(http: HttpClient, id: string, request: DeliverTransferRequest): Promise<DeliveredTransfer> {
  return http.post<DeliveredTransfer>(`/transfers/${id}/deliver`, request);
}

/** Livraison sans code (transfers.manage_code, Superviseur) : possible même si le code est bloqué. */
export function deliverWithoutCode(http: HttpClient, id: string, request: DeliverWithoutCodeRequest): Promise<DeliveredTransfer> {
  return http.post<DeliveredTransfer>(`/transfers/${id}/deliver-without-code`, request);
}
