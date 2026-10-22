import { QueryClient } from '@tanstack/react-query';
import { isApiError } from '@/shared/api/errors';

// Cache des données serveur. Pas de nouvel essai automatique pour un refus du serveur (4xx : droit,
// périmètre, donnée absente) : seulement pour le réseau ou une erreur 5xx, deux fois au plus.
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        retry: (failureCount, error) =>
          failureCount < 2 && !(isApiError(error) && error.status >= 400 && error.status < 500),
        refetchOnWindowFocus: true,
      },
      mutations: { retry: false },
    },
  });
}
