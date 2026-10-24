import type { HttpClient } from '@/shared/api/http';
import type { Role } from '@/shared/auth/types';
import type {
  AccountPartyScope,
  CreateUserRequest,
  CreatedUser,
  SessionView,
  TemporaryPassword,
  UpdateUserProfileRequest,
  UserLimits,
  UserListParams,
  UserPermissions,
  UserSummary,
} from './types';

// Toutes ces routes exigent users.manage. Un non-Superviseur ne voit et ne gère que les comptes de son agence ;
// rôle, agence et création d'un Superviseur : compte de rôle Superviseur seulement (le serveur tranche).

export function listUsers(http: HttpClient, params: UserListParams): Promise<UserSummary[]> {
  return http.get<UserSummary[]>('/users', { query: params });
}

export function getUser(http: HttpClient, id: string): Promise<UserSummary> {
  return http.get<UserSummary>(`/users/${id}`);
}

export function createUser(http: HttpClient, request: CreateUserRequest): Promise<CreatedUser> {
  return http.post<CreatedUser>('/users', request);
}

export function updateUserProfile(http: HttpClient, id: string, request: UpdateUserProfileRequest): Promise<void> {
  return http.put<void>(`/users/${id}`, request);
}

/** Coupe aussi toutes les sessions du compte. */
export function blockUser(http: HttpClient, id: string): Promise<void> {
  return http.post<void>(`/users/${id}/block`);
}

/** L'ancien mot de passe n'est plus valable : nouveau mot de passe temporaire, verrouillage levé. */
export function unblockUser(http: HttpClient, id: string): Promise<TemporaryPassword> {
  return http.post<TemporaryPassword>(`/users/${id}/unblock`);
}

/** Nouveau mot de passe temporaire, verrouillage levé, sessions coupées ; un compte bloqué reste bloqué. */
export function resetUserPassword(http: HttpClient, id: string): Promise<TemporaryPassword> {
  return http.post<TemporaryPassword>(`/users/${id}/reset-password`);
}

/** Change le rôle (avec l'agence : obligatoire sauf pour Superviseur) ; efface permissions ajustées et plafonds. */
export function changeUserRole(http: HttpClient, id: string, request: { role: Role; agencyId: string | null }): Promise<void> {
  return http.post<void>(`/users/${id}/change-role`, request);
}

/** Déplace le compte dans l'autre agence ; efface ses plafonds. */
export function changeUserAgency(http: HttpClient, id: string, agencyId: string): Promise<void> {
  return http.post<void>(`/users/${id}/change-agency`, { agencyId });
}

export function getUserPermissions(http: HttpClient, id: string): Promise<UserPermissions> {
  return http.get<UserPermissions>(`/users/${id}/permissions`);
}

/** Liste complète des permissions voulues ; un Superviseur garde tout (SupervisorHasAll). */
export function setUserPermissions(http: HttpClient, id: string, permissions: string[]): Promise<UserPermissions> {
  return http.put<UserPermissions>(`/users/${id}/permissions`, { permissions });
}

export function getPartyScope(http: HttpClient, id: string): Promise<AccountPartyScope> {
  return http.get<AccountPartyScope>(`/users/${id}/party-scope`);
}

export function setPartyScope(http: HttpClient, id: string, request: { limited: boolean; partyIds: string[] }): Promise<AccountPartyScope> {
  return http.put<AccountPartyScope>(`/users/${id}/party-scope`, request);
}

export function getUserLimits(http: HttpClient, id: string): Promise<UserLimits> {
  return http.get<UserLimits>(`/users/${id}/limits`);
}

/** État complet des trois plafonds (null = pas de limite). */
export function setUserLimits(
  http: HttpClient,
  id: string,
  request: { perTransfer: number | null; perDay: number | null; creditPerDay: number | null },
): Promise<UserLimits> {
  return http.put<UserLimits>(`/users/${id}/limits`, request);
}

export function getUserSessions(http: HttpClient, id: string): Promise<SessionView[]> {
  return http.get<SessionView[]>(`/users/${id}/sessions`);
}

/** Ferme une session d'un compte (effet immédiat ; l'appareil est déconnecté en direct). */
export function closeUserSession(http: HttpClient, id: string, sessionId: string): Promise<void> {
  return http.post<void>(`/users/${id}/sessions/${sessionId}/revoke`);
}
