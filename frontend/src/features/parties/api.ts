import type { HttpClient } from '@/shared/api/http';
import type {
  CreatePartyRequest,
  PartyList,
  PartyListParams,
  PartyStatement,
  PartySummary,
  SetCreditLimitRequest,
  SetOpeningBalanceRequest,
  UpdatePartyRequest,
} from './types';

/** Tiers visibles (communs aux deux agences ; un compte limité ne voit que les siens). */
export function listParties(http: HttpClient, params: PartyListParams = {}): Promise<PartyList> {
  return http.get<PartyList>('/parties', { query: params });
}

/** Création (parties.create) : un compte au grand livre est ouvert dans chaque agence. 201 { id }. */
export function createParty(http: HttpClient, request: CreatePartyRequest): Promise<{ id: string }> {
  return http.post<{ id: string }>('/parties', request);
}

/** Fiche d'un tiers ; 404 s'il n'existe pas ou s'il est hors de la liste d'un compte limité. */
export function getParty(http: HttpClient, id: string): Promise<PartySummary> {
  return http.get<PartySummary>(`/parties/${id}`);
}

/** Relevé dans une agence (parties.view_balance) ; sans dates, le mois en cours. */
export function getPartyStatement(http: HttpClient, id: string, params: { agencyId?: string; from?: string; to?: string }): Promise<PartyStatement> {
  return http.get<PartyStatement>(`/parties/${id}/statement`, { query: params });
}

/** Fiche complète (parties.manage, Superviseur). */
export function updateParty(http: HttpClient, id: string, request: UpdatePartyRequest): Promise<void> {
  return http.put<void>(`/parties/${id}`, request);
}

export function setOpeningBalance(http: HttpClient, id: string, agencyId: string, request: SetOpeningBalanceRequest): Promise<void> {
  return http.put<void>(`/parties/${id}/agencies/${agencyId}/opening-balance`, request);
}

export function setCreditLimit(http: HttpClient, id: string, agencyId: string, request: SetCreditLimitRequest): Promise<void> {
  return http.put<void>(`/parties/${id}/agencies/${agencyId}/credit-limit`, request);
}

/** Solde nul dans les deux agences seulement (Parties.Party.BalanceNotZero) ; ferme ses comptes au grand livre. */
export function archiveParty(http: HttpClient, id: string): Promise<void> {
  return http.post<void>(`/parties/${id}/archive`);
}

export function unarchiveParty(http: HttpClient, id: string): Promise<void> {
  return http.post<void>(`/parties/${id}/unarchive`);
}
