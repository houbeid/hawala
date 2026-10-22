import { useQuery } from '@tanstack/react-query';
import { useSession, useSessionManager } from '@/shared/auth/hooks';
import { listAgencies } from './api';

export const organizationKeys = {
  agencies: ['organization', 'agencies'] as const,
};

/** Les deux agences (NKC, DKR) : elles changent rarement (couleur seulement), gardées 10 minutes. */
export function useAgencies() {
  const session = useSessionManager();
  const authenticated = useSession().status === 'authenticated';
  return useQuery({
    queryKey: organizationKeys.agencies,
    queryFn: () => listAgencies(session.http),
    enabled: authenticated,
    staleTime: 10 * 60_000,
  });
}
