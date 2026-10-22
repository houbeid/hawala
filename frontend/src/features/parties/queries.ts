import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useSessionManager } from '@/shared/auth/hooks';
import { createParty, listParties } from './api';
import type { CreatePartyRequest } from './types';

export const partyKeys = {
  all: ['parties'] as const,
  list: (params: { search?: string; type?: string; includeArchived?: boolean; agencyId?: string }) => ['parties', 'list', params] as const,
};

export function useParties(params: { search?: string; type?: string; includeArchived?: boolean; agencyId?: string }, enabled = true) {
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
