import type { HttpClient } from '@/shared/api/http';
import type {
  BalanceOverview,
  BalanceSnapshot,
  CaisseCapital,
  GeneralBalanceSnapshot,
  GeneralBalanceVariation,
  GeneralBalanceView,
  GeneralCapital,
  SetCapitalRequest,
  SetGeneralCapitalRequest,
} from './types';

/** Bilan en direct des deux caisses (balance.view) : lourd (tous les soldes), jamais appelé à l'ouverture de l'app. */
export function getBalance(http: HttpClient): Promise<BalanceOverview> {
  return http.get<BalanceOverview>('/balance');
}

/** Enregistre le bilan du jour d'une caisse (balance.manage). */
export function saveBalance(http: HttpClient, agencyId: string): Promise<BalanceSnapshot> {
  return http.post<BalanceSnapshot>('/balance/snapshots', { agencyId });
}

/** Bilans enregistrés des deux caisses, 30 derniers jours par défaut. */
export function listBalanceSnapshots(http: HttpClient): Promise<BalanceSnapshot[]> {
  return http.get<BalanceSnapshot[]>('/balance/snapshots');
}

export function getCapital(http: HttpClient): Promise<CaisseCapital[]> {
  return http.get<CaisseCapital[]>('/balance/capital');
}

export function setCapital(http: HttpClient, request: SetCapitalRequest): Promise<void> {
  return http.put<void>('/balance/capital', request);
}

/** Bilan général en direct, au taux du bilan (MRO pour 5 000 FCFA) ; aussi lourd que le bilan par caisse. */
export function getGeneralBalance(http: HttpClient, rate: number): Promise<GeneralBalanceView> {
  return http.get<GeneralBalanceView>(`/balance/general?rate=${encodeURIComponent(String(rate))}`);
}

/** Sauvegarde le bilan général du jour avec son taux (balance.manage). */
export function saveGeneralBalance(http: HttpClient, rate: number): Promise<GeneralBalanceSnapshot> {
  return http.post<GeneralBalanceSnapshot>('/balance/general/snapshots', { rate });
}

/** Photos du bilan général, 30 derniers jours par défaut, de la plus ancienne à la plus récente. */
export function listGeneralSnapshots(http: HttpClient): Promise<GeneralBalanceSnapshot[]> {
  return http.get<GeneralBalanceSnapshot[]>('/balance/general/snapshots');
}

/** Écart entre la dernière photo du bilan général et maintenant, au taux donné ; 404 sans photo. */
export function getGeneralVariation(http: HttpClient, rate: number): Promise<GeneralBalanceVariation> {
  return http.get<GeneralBalanceVariation>(`/balance/general/variation?rate=${encodeURIComponent(String(rate))}`);
}

export function getGeneralCapital(http: HttpClient): Promise<GeneralCapital> {
  return http.get<GeneralCapital>('/balance/general/capital');
}

export function setGeneralCapital(http: HttpClient, request: SetGeneralCapitalRequest): Promise<void> {
  return http.put<void>('/balance/general/capital', request);
}
