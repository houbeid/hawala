import type { HttpClient } from '@/shared/api/http';
import type { Agency } from './types';

/** Lisible par tout compte connecté (badge et couleur de l'interface). */
export function listAgencies(http: HttpClient): Promise<Agency[]> {
  return http.get<Agency[]>('/agencies');
}
