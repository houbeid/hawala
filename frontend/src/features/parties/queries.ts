import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { useSessionManager } from '@/shared/auth/hooks';
import { createParty, getParty, getPartyStatement, listParties } from './api';
import type { CreatePartyRequest, PartyListParams } from './types';

export const partyKeys = {
  all: ['parties'] as const,
  list: (params: PartyListParams) => ['parties', 'list', params] as const,
  detail: (id: string) => ['parties', 'detail', id] as const,
  statement: (id: string, params: { agencyId?: string; from?: string; to?: string }) => ['parties', 'statement', id, params] as const,
};

export function useParties(params: PartyListParams, enabled = true) {
  const session = useSessionManager();
  return useQuery({
    queryKey: partyKeys.list(params),
    queryFn: () => listParties(session.http, params),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useCreateParty() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreatePartyRequest) => createParty(session.http, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: partyKeys.all }),
  });
}

export function useParty(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: partyKeys.detail(id), queryFn: () => getParty(session.http, id) });
}

export function usePartyStatement(id: string, params: { agencyId?: string; from?: string; to?: string }, enabled = true) {
  const session = useSessionManager();
  return useQuery({
    queryKey: partyKeys.statement(id, params),
    queryFn: () => getPartyStatement(session.http, id, params),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** Actions du Superviseur sur un tiers : liste, fiche et relevés relus après succès. */
export function usePartyAction<TArgs, TResult>(action: (http: HttpClient, args: TArgs) => Promise<TResult>) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: TArgs) => action(session.http, args),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: partyKeys.all }),
  });
}
