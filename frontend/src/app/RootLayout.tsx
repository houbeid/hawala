import { Navigate, Outlet, useRouterState } from '@tanstack/react-router';
import { ServerUnreachableScreen, SessionLoadingScreen } from '@/features/auth/pages/SessionScreens';
import { useSession } from '@/shared/auth/hooks';
import { isAuthPath, safeRedirect } from './redirect';

// Garde unique de l'application, branchée sur l'état de la session (réactif : un SignOut reçu
// par SignalR, un rafraîchissement refusé ou une connexion réussie changent l'écran tout de suite).
// La garde par permission de chaque page est dans l'AppShell ; le serveur reste juge.

export function RootLayout() {
  const state = useSession();
  const location = useRouterState({ select: (s) => s.location });

  switch (state.status) {
    case 'loading':
      return <SessionLoadingScreen />;
    case 'unreachable':
      return <ServerUnreachableScreen />;
    case 'anonymous': {
      if (location.pathname === '/login') return <Outlet />;
      const back = location.pathname === '/' ? undefined : location.href;
      return <Navigate to="/login" search={{ redirect: back }} replace />;
    }
    case 'mustChangePassword':
      return location.pathname === '/change-password' ? <Outlet /> : <Navigate to="/change-password" replace />;
    // Lecture hors ligne : mêmes écrans, données gardées sur le téléphone, actions serveur remplacées par leur explication.
    case 'offline':
    case 'authenticated': {
      if (!isAuthPath(location.pathname)) return <Outlet />;
      const search = location.search as { redirect?: unknown };
      // Chemin interne vérifié par safeRedirect ; le routeur ne peut pas typer un chemin venu de l'URL.
      return <Navigate to={safeRedirect(search.redirect) as '/'} replace />;
    }
  }
}
