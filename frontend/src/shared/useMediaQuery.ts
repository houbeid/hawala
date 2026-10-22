import { useSyncExternalStore } from 'react';

// Choix d'affichage selon la largeur (cartes sur téléphone, tableau sur ordinateur). Un seul des deux est
// rendu : pas de contenu en double dans la page pour les lecteurs d'écran.

export const DESKTOP_QUERY = '(min-width: 1024px)';

export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      if (typeof window.matchMedia !== 'function') return () => {};
      const list = window.matchMedia(query);
      list.addEventListener('change', onChange);
      return () => list.removeEventListener('change', onChange);
    },
    () => typeof window.matchMedia === 'function' && window.matchMedia(query).matches,
    () => false,
  );
}

export function useIsDesktop(): boolean {
  return useMediaQuery(DESKTOP_QUERY);
}
