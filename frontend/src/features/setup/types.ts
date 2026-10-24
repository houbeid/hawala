// Mise en service (phase 8) : import des tiers, copiés sur Parties.Application/Features/Import/PartyImport.cs.

/** Un problème d'une ligne : numéro de ligne Excel (0 = le fichier entier), colonne (name, NKC.balance…), code stable. */
export interface PartyImportIssue {
  row: number;
  column: string | null;
  code: string;
}

/**
 * Rapport de « Vérifier » (rien n'est créé). Tiers : `totals` = soldes initiaux signés par code d'agence ; transferts
 * repris (Transfers.Application/.../ResumedTransferImport.cs) : montant à livrer par agence de destination.
 */
export interface ImportReport {
  rowCount: number;
  validCount: number;
  issues: PartyImportIssue[];
  totals: Record<string, number>;
}

export type PartyImportReport = ImportReport;

export interface ImportResult {
  created: number;
}

/** GET /setup/start-date (Ledger.Application/StartDate/StartDateFeatures.cs) : null tant qu'elle n'est pas fixée. */
export interface StartDateView {
  startDate: string | null;
  setAt: string | null;
  setBy: string | null;
}
