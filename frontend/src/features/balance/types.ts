// Bilan par caisse, copiés sur Balance.Application/Features/BalanceFeatures.cs (décision du 21 octobre 2026 :
// deux caisses, deux capitaux, chacune dans sa devise, sans taux ni total converti). Pleine précision ; l'affichage arrondit.
import type { Currency } from '@/features/organization/types';

/** Le bilan d'une caisse. Sans capital initial saisi : ni capital pris en compte, ni résultat net, ni pourcentage. */
export interface BalanceView {
  agencyId: string;
  currency: Currency;
  date: string;
  /** Budget de la caisse (non livrés déjà soustraits). */
  budget: number;
  initialCapital: number | null;
  /** Approvisionnements − retraits du patron dans cette caisse. */
  ownerContributions: number;
  /** Capital initial + apports nets du patron. */
  capitalUsed: number | null;
  /** Budget − capital pris en compte. */
  netResult: number | null;
  /** Résultat net ÷ capital pris en compte × 100. */
  netResultPercent: number | null;
  /** Cumul des bénéfices du jour enregistrés de la caisse (en MRO), à titre d'information. */
  savedProfitsMro: number;
}

/** GET /balance (balance.view) : les deux caisses côte à côte. */
export interface BalanceOverview {
  date: string;
  caisses: BalanceView[];
}

/** Photo enregistrée du bilan d'une caisse (une par caisse et par jour, remplacée le même jour). */
export interface BalanceSnapshot extends Omit<BalanceView, 'date'> {
  id: string;
  date: string;
  savedBy: string;
  savedAt: string;
}

export interface CapitalChange {
  id: string;
  amount: number;
  currency: Currency;
  reason: string | null;
  setBy: string;
  setAt: string;
}

/** GET /balance/capital : capital actuel de chaque caisse (la saisie la plus récente fait foi) et ses saisies. */
export interface CaisseCapital {
  agencyId: string;
  currency: Currency;
  current: number | null;
  history: CapitalChange[];
}

/** PUT /balance/capital (balance.manage) : positif, dans la devise de la caisse ; motif facultatif (500 au plus). */
export interface SetCapitalRequest {
  agencyId: string;
  amount: number;
  reason: string | null;
}

export const CAPITAL_REASON_MAX = 500;
