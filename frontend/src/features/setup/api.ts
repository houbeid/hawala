import type { HttpClient } from '@/shared/api/http';
import type { ImportReport, ImportResult, StartDateView } from './types';

/** Modèle Excel à remplir (en-têtes dans la langue de l'interface, feuille d'aide). */
export function downloadPartyTemplate(http: HttpClient, lang: 'fr' | 'ar'): Promise<Blob> {
  return http.blob('/setup/parties/template', { query: { lang } });
}

/** « Vérifier » : rapport ligne par ligne, rien n'est créé. */
export function checkPartyImport(http: HttpClient, file: File): Promise<ImportReport> {
  const form = new FormData();
  form.append('file', file);
  return http.request<ImportReport>('/setup/parties/check', { method: 'POST', form });
}

/** « Importer » : tout ou rien ; soldes initiaux datés du jour de mise en service (aujourd'hui si null). */
export function importParties(http: HttpClient, file: File, operationDate: string | null): Promise<ImportResult> {
  const form = new FormData();
  form.append('file', file);
  if (operationDate) form.append('operationDate', operationDate);
  return http.request<ImportResult>('/setup/parties/import', { method: 'POST', form });
}

/** Modèle Excel des transferts non livrés de l'ancienne app (reprise). */
export function downloadResumeTemplate(http: HttpClient, lang: 'fr' | 'ar'): Promise<Blob> {
  return http.blob('/setup/transfers/template', { query: { lang } });
}

/** « Vérifier » la reprise des transferts non livrés : rien n'est créé. */
export function checkResumedTransfers(http: HttpClient, file: File): Promise<ImportReport> {
  const form = new FormData();
  form.append('file', file);
  return http.request<ImportReport>('/setup/transfers/check', { method: 'POST', form });
}

/** « Importer » les transferts repris : tout ou rien ; écritures d'ouverture datées du jour de mise en service. */
export function importResumedTransfers(http: HttpClient, file: File, operationDate: string | null): Promise<ImportResult> {
  const form = new FormData();
  form.append('file', file);
  if (operationDate) form.append('operationDate', operationDate);
  return http.request<ImportResult>('/setup/transfers/import', { method: 'POST', form });
}

/** Date de démarrage (lue par tout compte connecté). */
export function getStartDate(http: HttpClient): Promise<StartDateView> {
  return http.get<StartDateView>('/setup/start-date');
}

/** Fixe la date de démarrage, une seule fois (balance.manage). */
export function setStartDate(http: HttpClient, startDate: string): Promise<StartDateView> {
  return http.put<StartDateView>('/setup/start-date', { startDate });
}

// --- Liste de contrôle : lectures légères des autres écrans (seuls les champs utiles sont typés) ---

export function countActiveAccounts(http: HttpClient, agencyId: string): Promise<number> {
  return http.get<{ role: string; status: string }[]>('/users', { query: { agencyId } }).then((users) => users.filter((u) => u.role !== 'Supervisor' && u.status === 'Active').length);
}

export function countTreasuryAccounts(http: HttpClient, agencyId: string): Promise<number> {
  return http.get<{ isActive: boolean }[]>('/treasury-accounts', { query: { agencyId } }).then((accounts) => accounts.filter((a) => a.isActive).length);
}

export function countParties(http: HttpClient): Promise<number> {
  return http.get<{ totalCount: number }>('/parties').then((list) => list.totalCount);
}

/** Vrai si le taux du jour est fixé (404 = pas de taux). */
export function hasRate(http: HttpClient, day: string): Promise<boolean> {
  return http.get<unknown>(`/rates/${day}`).then(
    () => true,
    (error: unknown) => {
      if ((error as { status?: number }).status === 404) return false;
      throw error;
    },
  );
}

/** Capital initial saisi, par agence. */
export function capitalByAgency(http: HttpClient): Promise<Record<string, number | null>> {
  return http
    .get<{ agencyId: string; current: number | null }[]>('/balance/capital')
    .then((list) => Object.fromEntries(list.map((c) => [c.agencyId, c.current])));
}

/** Budget actuel de la caisse (dans sa devise). */
export function currentBudget(http: HttpClient, agencyId: string): Promise<number> {
  return http.get<{ budget: number }>('/cash-budget', { query: { agencyId } }).then((view) => view.budget);
}

export function budgetSavedOn(http: HttpClient, agencyId: string, day: string): Promise<boolean> {
  return http.get<{ date: string }[]>('/cash-budget/snapshots', { query: { agencyId } }).then((list) => list.some((s) => s.date === day));
}

/** Agences dont le bilan du jour est enregistré. */
export function balanceSavedOn(http: HttpClient, day: string): Promise<string[]> {
  return http.get<{ agencyId: string; date: string }[]>('/balance/snapshots').then((list) => list.filter((s) => s.date === day).map((s) => s.agencyId));
}
