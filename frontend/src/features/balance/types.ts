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

// --- Bilan général (décisions du 31 octobre 2026), copiés sur Balance.Application/Features/GeneralBalanceFeatures.cs ---

/** Composants d'une caisse (ou leur total), dans la devise indiquée : totaux seulement, jamais le détail par tiers. */
export interface BalanceComponents {
  currency: Currency;
  cash: number;
  banks: number;
  /** Créances : tiers « dans le bilan » qui nous doivent. */
  debtors: number;
  /** Dettes : tiers « dans le bilan » à qui l'on doit (en positif). */
  creditors: number;
  undelivered: number;
  /** Approvisionnements − retraits du patron. */
  ownerContributions: number;
  /** Espèces + banques + créances − dettes − non livrés. */
  budget: number;
}

/** GET /balance/general?rate= : Nouakchott en MRO + Dakar converti au taux du bilan ; pleine précision. */
export interface GeneralBalanceView {
  date: string;
  /** Taux du bilan : MRO pour 5 000 FCFA. */
  rate: number;
  nouakchott: BalanceComponents;
  dakar: BalanceComponents;
  dakarInMro: BalanceComponents;
  total: BalanceComponents;
  generalBalance: number;
  capital: number | null;
  ownerContributionsMro: number;
  capitalUsed: number | null;
  netResult: number | null;
  netResultPercent: number | null;
  /** Bénéfices enregistrés des deux agences (MRO), à titre d'information : hors formule. */
  savedProfitsMro: number;
}

/** Photo du bilan général : une par jour, remplacée le même jour, avec son taux. */
export interface GeneralBalanceSnapshot {
  id: string;
  date: string;
  rate: number;
  nouakchott: BalanceComponents;
  dakar: BalanceComponents;
  /** Total en MRO au taux de la photo. */
  total: BalanceComponents;
  generalBalance: number;
  capital: number | null;
  capitalUsed: number | null;
  netResult: number | null;
  netResultPercent: number | null;
  savedProfitsMro: number;
  savedBy: string;
  savedAt: string;
}

export interface GeneralCapitalChange {
  id: string;
  amount: number;
  reason: string | null;
  setBy: string;
  setAt: string;
}

/** GET /balance/general/capital : capital général (MRO) et ses saisies, de la plus récente à la plus ancienne. */
export interface GeneralCapital {
  current: number | null;
  history: GeneralCapitalChange[];
}

/** Composant du budget, en texte (BalanceComponent côté serveur). */
export type BalanceComponentName = 'Cash' | 'Banks' | 'Debtors' | 'Creditors' | 'Undelivered';

/** Variation d'un composant : Nouakchott en MRO, Dakar en FCFA et converti au taux de maintenant, effet signé (MRO). */
export interface ComponentVariation {
  component: BalanceComponentName;
  nouakchottChange: number;
  dakarChange: number;
  dakarChangeMro: number;
  /** Effet sur le bilan général : + pour espèces, banques, créances ; − pour dettes et non livrés. */
  effect: number;
}

/** GET /balance/general/variation?rate= : écart avec la dernière photo ; somme des effets + effet du taux = écart. */
export interface GeneralBalanceVariation {
  fromDate: string;
  fromSavedAt: string;
  fromRate: number;
  toRate: number;
  fromGeneralBalance: number;
  toGeneralBalance: number;
  components: ComponentVariation[];
  rateEffect: number;
  generalBalanceChange: number;
  capitalChange: number | null;
  ownerContributionsChange: number;
  netResultChange: number | null;
}

/** PUT /balance/general/capital (balance.manage) : positif, en MRO ; motif facultatif. */
export interface SetGeneralCapitalRequest {
  amount: number;
  reason: string | null;
}
