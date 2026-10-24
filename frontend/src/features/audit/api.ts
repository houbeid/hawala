import type { HttpClient } from '@/shared/api/http';
import type { AuditParams, AuditResponse } from './types';

/** Journal d'audit (audit.view) ; le serveur applique le périmètre (un non-Superviseur : son agence). */
export function listAudit(http: HttpClient, params: AuditParams): Promise<AuditResponse> {
  return http.get<AuditResponse>('/audit', { query: params });
}
