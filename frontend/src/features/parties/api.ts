import type { HttpClient } from '@/shared/api/http';
import type { CreatePartyRequest, PartyList } from './types';

/** Tiers visibles (communs aux deux agences ; un compte limité ne voit que les siens). */
export function listParties(http: HttpClient, params: { search?: string; type?: string; includeArchived?: boolean; agencyId?: string } = {}): Promise<PartyList> {
  return http.get<PartyList>('/parties', { query: params });
}

/** Création (parties.create) : un compte au grand livre est ouvert dans chaque agence. 201 { id }. */
export function createParty(http: HttpClient, request: CreatePartyRequest): Promise<{ id: string }> {
  return http.post<{ id: string }>('/parties', request);
}
