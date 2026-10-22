import { Navigate } from '@tanstack/react-router';
import { useCurrentAccount } from '@/shared/auth/hooks';
import { homePathFor } from './navigation';

/** Adresse « / » : envoie vers la page d'ouverture du compte (voir homePathFor). */
export function HomeRedirect() {
  const account = useCurrentAccount();
  return <Navigate to={homePathFor(account?.role)} replace />;
}
