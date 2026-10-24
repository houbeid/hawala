import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { useSessionManager } from '@/shared/auth/hooks';
import { getPartyScope, getUser, getUserLimits, getUserPermissions, getUserSessions, listUsers } from './api';
import type { UserListParams } from './types';

export const userKeys = {
  all: ['users'] as const,
  list: (params: UserListParams) => ['users', 'list', params] as const,
  detail: (id: string) => ['users', 'detail', id] as const,
  permissions: (id: string) => ['users', 'permissions', id] as const,
  partyScope: (id: string) => ['users', 'party-scope', id] as const,
  limits: (id: string) => ['users', 'limits', id] as const,
  sessions: (id: string) => ['users', 'sessions', id] as const,
};

export function useUsers(params: UserListParams) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.list(params), queryFn: () => listUsers(session.http, params), placeholderData: keepPreviousData });
}

export function useUser(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.detail(id), queryFn: () => getUser(session.http, id) });
}

/** Action sur un compte : liste et fiches relues après succès. */
export function useUserAction<TArgs, TResult>(action: (http: HttpClient, args: TArgs) => Promise<TResult>) {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (args: TArgs) => action(session.http, args),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: userKeys.all }),
  });
}

export function useUserPermissions(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.permissions(id), queryFn: () => getUserPermissions(session.http, id) });
}

export function usePartyScope(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.partyScope(id), queryFn: () => getPartyScope(session.http, id) });
}

export function useUserLimits(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.limits(id), queryFn: () => getUserLimits(session.http, id) });
}

export function useUserSessions(id: string) {
  const session = useSessionManager();
  return useQuery({ queryKey: userKeys.sessions(id), queryFn: () => getUserSessions(session.http, id) });
}
