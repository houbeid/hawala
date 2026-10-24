import { useContext, useSyncExternalStore } from 'react';
import { SessionContext } from './context';
import { accountOf, type SessionManager, type SessionState } from './session';
import type { CurrentAccount } from './types';

export function useSessionManager(): SessionManager {
  const session = useContext(SessionContext);
  if (!session) throw new Error('useSessionManager doit être utilisé sous <SessionProvider>.');
  return session;
}

export function useSession(): SessionState {
  const session = useSessionManager();
  return useSyncExternalStore(session.subscribe, session.getState, session.getState);
}

/** Le compte connecté (ou gardé pour la lecture hors ligne), ou null tant que la session n'est pas ouverte. */
export function useCurrentAccount(): CurrentAccount | null {
  return accountOf(useSession());
}

/**
 * Vrai si le compte connecté a la permission. Sert seulement à afficher ou non une action :
 * le serveur revérifie toujours (et un bouton interdit est absent, jamais grisé).
 */
export function usePermission(permission: string): boolean {
  const account = useCurrentAccount();
  return account?.permissions.includes(permission) ?? false;
}
