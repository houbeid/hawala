import { useSyncExternalStore } from 'react';

// État réseau du navigateur. « En ligne » ne garantit pas que le serveur répond : c'est seulement
// un indice pour prévenir l'agent avant qu'il essaie (la vraie erreur réseau reste gérée par le client HTTP).

function subscribe(onChange: () => void): () => void {
  window.addEventListener('online', onChange);
  window.addEventListener('offline', onChange);
  return () => {
    window.removeEventListener('online', onChange);
    window.removeEventListener('offline', onChange);
  };
}

export function useOnlineStatus(): boolean {
  return useSyncExternalStore(
    subscribe,
    () => navigator.onLine,
    () => true,
  );
}
