// Bénéfice, copiés sur le backend :
// - ProfitDay, ProfitReport, ProfitDayDetail, ProfitSnapshotView : Profit.Application/Features/ProfitReports.cs
// - TransferProfitLine : Transfers.Contracts/ITransferProfits.cs
// Montants en pleine précision (règle d'arrondi 4) : seul l'affichage arrondit.

export interface ProfitDay {
  date: string;
  transferCount: number;
  /** Montant envoyé du jour, en FCFA. */
  amountXof: number;
  /** Prix d'achat du jour ; null si les transferts du jour en ont plusieurs. */
  purchaseRate: number | null;
  profitMro: number;
  /** Bénéfice de la photo enregistrée ce jour-là ; null sinon. */
  savedProfitMro: number | null;
  savedAt: string | null;
}

/** GET /profit?agencyId=&from=&to= (profit.view) ; agencyId null = les deux agences (bénéfice général). */
export interface ProfitReport {
  from: string;
  to: string;
  agencyId: string | null;
  totalProfitMro: number;
  transferCount: number;
  days: ProfitDay[];
}

export interface TransferProfitLine {
  transferId: string;
  reference: string;
  sourceAgencyId: string;
  destinationAgencyId: string;
  operationAt: string;
  amountXof: number;
  amountMro: number;
  saleRate: number;
  purchaseRate: number;
  profitMro: number;
}

/** GET /profit/days/{date}?agencyId= : une ligne par transfert. */
export interface ProfitDayDetail {
  date: string;
  agencyId: string | null;
  totalProfitMro: number;
  transfers: TransferProfitLine[];
}

/** POST /profit/snapshots { agencyId } : photo du jour courant, remplacée le même jour. */
export interface ProfitSnapshot {
  id: string;
  agencyId: string | null;
  date: string;
  transferCount: number;
  amountXof: number;
  purchaseRate: number | null;
  profitMro: number;
  savedBy: string;
  savedAt: string;
}
