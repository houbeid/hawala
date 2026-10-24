import { useContext, useSyncExternalStore } from 'react';
import { SessionContext } from './auth/context';

// État réseau du navigateur. « En ligne » ne garantit pas que le serveur répond : c'est seulement
// un indice pour prévenir l'agent avant qu'il essaie (la vraie erreur réseau reste gérée par le client HTTP).
// En lecture hors ligne (session « offline » : app rouverte sans réseau, aucun jeton), l'app se comporte comme
// hors ligne même si le téléphone a retrouvé du réseau, tant que la session n'est pas reprise.

function subscribe(onChange: () => void): () => void {
  window.addEventListener('online', onChange);
  window.addEventListener('offline', onChange);
  return () => {
    window.removeEventListener('online', onChange);
    window.removeEventListener('offline', onChange);
  };
}

const noSubscription = () => () => {};

export function useOnlineStatus(): boolean {
  const network = useSyncExternalStore(
    subscribe,
    () => navigator.onLine,
    () => true,
  );
  const session = useContext(SessionContext);
  const offlineSession = useSyncExternalStore(
    session?.subscribe ?? noSubscription,
    () => session?.getState().status === 'offline',
    () => false,
  );
  return network && !offlineSession;
}
