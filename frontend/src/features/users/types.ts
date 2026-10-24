// Gestion des comptes, copiés sur le backend :
// - UserSummary, UserAgency : Identity.Application/Features/Users/UserSummary.cs
// - CreatedUser, UnblockedUser, ResetPassword : Identity.Application/Features/Users/*/…Command.cs
// - requêtes : Hawala.Api/Endpoints/Identity/UserEndpoints.cs (users.manage)
import type { Role } from '@/shared/auth/types';

export type AccountStatus = 'Active' | 'Blocked';

export interface UserAgency {
  id: string;
  code: string;
  name: string;
  color: string;
}

export interface UserSummary {
  id: string;
  fullName: string;
  email: string;
  phone: string | null;
  role: Role;
  /** Null pour un Superviseur (il voit les deux agences). */
  agency: UserAgency | null;
  status: AccountStatus;
  mustChangePassword: boolean;
  /** Verrouillé après 5 échecs, jusqu'à cet instant ; null sinon. */
  lockedUntil: string | null;
  lastLoginAt: string | null;
}

export type UserListParams = {
  agencyId?: string;
  role?: Role;
  status?: AccountStatus;
  search?: string;
};

/** POST /users : agence obligatoire sauf pour un Superviseur (interdite). */
export interface CreateUserRequest {
  email: string;
  phone: string | null;
  fullName: string;
  role: Role;
  agencyId: string | null;
}

/** Réponse 201 : le mot de passe temporaire n'est renvoyé qu'une fois. */
export interface CreatedUser {
  id: string;
  temporaryPassword: string;
}

/** Réponse de « débloquer » et de « réinitialiser le mot de passe » : nouveau mot de passe temporaire, une fois. */
export interface TemporaryPassword {
  temporaryPassword: string;
}

/** PUT /users/{id} : nom, email, téléphone (vide = retiré) ; les sessions sont gardées. */
export interface UpdateUserProfileRequest {
  email: string;
  phone: string | null;
  fullName: string;
}

// Livraison 2 : permissions par compte, tiers autorisés, plafonds, sessions. Copiés sur :
// - UserPermissions, UserPermissionState : Identity.Application/Features/Users/PermissionOverrides/UserPermissions.cs
// - AccountPartyScopeView, AllowedPartyView : Parties.Application/Features/Scopes/AccountPartyScopeFeatures.cs
// - UserLimits : Identity.Application/Features/Users/Limits/UserLimitsFeatures.cs
// - SessionView : Identity.Application/Features/Sessions/SessionFeatures.cs

/** Role : modèle du rôle ; Added : ajoutée à ce compte ; Removed : retirée ; None : ni l'un ni l'autre. */
export type PermissionSource = 'Role' | 'Added' | 'Removed' | 'None';

export interface UserPermissionState {
  permission: string;
  granted: boolean;
  source: PermissionSource;
}

/** GET /users/{id}/permissions ; PUT avec la liste COMPLÈTE voulue (compte de rôle Superviseur). */
export interface UserPermissions {
  userId: string;
  role: Role;
  permissions: UserPermissionState[];
}

export interface AllowedParty {
  id: string;
  name: string;
  isArchived: boolean;
}

/** GET / PUT /users/{id}/party-scope (compte de rôle Superviseur) : non limité = liste vidée. */
export interface AccountPartyScope {
  accountId: string;
  limited: boolean;
  parties: AllowedParty[];
}

/** GET / PUT /users/{id}/limits : dans la devise de l'agence du compte ; null = pas de limite. */
export interface UserLimits {
  userId: string;
  currency: 'MRO' | 'XOF' | null;
  perTransfer: number | null;
  perDay: number | null;
  creditPerDay: number | null;
}

/** Une session = un appareil connecté (famille de refresh tokens). */
export interface SessionView {
  id: string;
  startedAt: string;
  lastUsedAt: string;
  expiresAt: string;
  device: string | null;
  ipAddress: string | null;
  /** Session de la requête en cours (« cet appareil »). */
  current: boolean;
}
