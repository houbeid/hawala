// Contrats d'authentification, copiés sur le backend (JSON en camelCase, énumérations en texte) :
// - AuthResponse : Hawala.Api/Endpoints/Identity/AuthEndpoints.cs (record AuthResponse)
// - CurrentAccount : Identity.Application/Features/Accounts/GetCurrentAccount/GetCurrentAccountQuery.cs
// - Role : Identity.Domain/Roles/RoleTemplate.cs (Role.ToString())

/** Réponse de /auth/login, /auth/refresh et /auth/change-password. Le refresh token reste dans le cookie. */
export interface AuthResponse {
  accessToken: string;
  /** Instant ISO 8601 (DateTimeOffset). */
  accessTokenExpiresAt: string;
  mustChangePassword: boolean;
}

export type Role = 'Supervisor' | 'Agent' | 'Cashier' | 'ReadOnly';

export interface CurrentAccountAgency {
  id: string;
  /** NKC ou DKR : ne change jamais. */
  code: string;
  name: string;
  /** #RRGGBB, choisie par le Superviseur. */
  color: string;
}

/** Réponse de GET /auth/me. */
export interface CurrentAccount {
  id: string;
  email: string;
  phone: string | null;
  fullName: string;
  role: Role;
  mustChangePassword: boolean;
  /** Null pour un Superviseur, qui voit les deux agences. */
  agency: CurrentAccountAgency | null;
  /** Permissions effectives (rôle + ajouts − retraits), triées. */
  permissions: string[];
}

export interface LoginRequest {
  /** Email ou téléphone, dans un seul champ. */
  login: string;
  password: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
