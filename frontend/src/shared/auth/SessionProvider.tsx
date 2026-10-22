import { useEffect, type ReactNode } from 'react';
import { SessionContext } from './context';
import type { SessionManager } from './session';

/** Fournit la session à l'application et la reprend au chargement (cookie de rafraîchissement). */
export function SessionProvider({ session, children }: { session: SessionManager; children: ReactNode }) {
  useEffect(() => {
    void session.start();
  }, [session]);

  return <SessionContext.Provider value={session}>{children}</SessionContext.Provider>;
}
