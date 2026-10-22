import type { HttpClient } from '@/shared/api/http';
import type { AuthResponse, ChangePasswordRequest, CurrentAccount, LoginRequest } from './types';

// Routes /api/v1/auth. login, refresh et logout passent par le client ANONYME (pas d'en-tête Authorization,
// pas de rejeu : un 401 y veut dire « identifiants faux » ou « session perdue », pas « jeton expiré »).

export function login(anonymous: HttpClient, request: LoginRequest): Promise<AuthResponse> {
  return anonymous.post<AuthResponse>('/auth/login', request);
}

export function refresh(anonymous: HttpClient): Promise<AuthResponse> {
  return anonymous.post<AuthResponse>('/auth/refresh');
}

export function logout(anonymous: HttpClient): Promise<void> {
  return anonymous.post<void>('/auth/logout');
}

/** Avec le jeton : seule route permise tant que le mot de passe temporaire n'est pas changé. */
export function changePassword(http: HttpClient, request: ChangePasswordRequest): Promise<AuthResponse> {
  return http.post<AuthResponse>('/auth/change-password', request);
}

/** Refusé (403) tant que le mot de passe temporaire n'est pas changé. */
export function getCurrentAccount(http: HttpClient): Promise<CurrentAccount> {
  return http.get<CurrentAccount>('/auth/me');
}
