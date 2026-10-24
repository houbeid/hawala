import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { HttpClient } from '@/shared/api/http';
import { useSessionManager } from '@/shared/auth/hooks';

// « Mes appareils » (décision du 22 octobre 2026 : chaque compte voit et ferme ses propres sessions, ex. un téléphone
// perdu). Copié sur Identity.Application/Features/Sessions/SessionFeatures.cs (SessionView) ;
// routes GET /auth/sessions et POST /auth/sessions/{id}/revoke (tout compte connecté).

export interface MySession {
  id: string;
  startedAt: string;
  lastUsedAt: string;
  expiresAt: string;
  device: string | null;
  ipAddress: string | null;
  /** Session de cet appareil. */
  current: boolean;
}

export function listMySessions(http: HttpClient): Promise<MySession[]> {
  return http.get<MySession[]>('/auth/sessions');
}

export function closeMySession(http: HttpClient, sessionId: string): Promise<void> {
  return http.post<void>(`/auth/sessions/${sessionId}/revoke`);
}

const key = ['auth', 'sessions'] as const;

export function useMySessions() {
  const session = useSessionManager();
  return useQuery({ queryKey: key, queryFn: () => listMySessions(session.http) });
}

export function useCloseMySession() {
  const session = useSessionManager();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (sessionId: string) => closeMySession(session.http, sessionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: key }),
  });
}
