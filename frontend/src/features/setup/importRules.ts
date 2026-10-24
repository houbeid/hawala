import type { ImportReport, PartyImportIssue } from './types';

// Lecture du rapport d'import : un problème s'écrit « Ligne 4 · Téléphone : <message> » ; une colonne d'agence
// (« NKC.balance ») devient « Solde · Nouakchott ». Le rapport d'un refus d'import (« Parties.Import.Invalid ») arrive
// dans le champ `report` de l'erreur.

export interface IssueView {
  row: number;
  /** Clé de colonne fixe (name, phone…) ou colonne d'agence. */
  column: { key: string; agencyCode: string | null } | null;
  code: string;
}

export function issueView(issue: PartyImportIssue): IssueView {
  if (!issue.column) return { row: issue.row, column: null, code: issue.code };
  const [first, second] = issue.column.split('.');
  return second ? { row: issue.row, column: { key: second, agencyCode: first ?? null }, code: issue.code } : { row: issue.row, column: { key: first!, agencyCode: null }, code: issue.code };
}

/** Rapport joint à un refus d'import (400 « Parties.Import.Invalid »), s'il y en a un. */
export function reportOfError(details: Readonly<Record<string, unknown>>): ImportReport | null {
  const report = details.report as ImportReport | undefined;
  return report && Array.isArray(report.issues) ? report : null;
}

/** L'import n'est proposé qu'après une vérification sans problème, d'au moins un tiers, du même fichier. */
export function canImport(report: ImportReport | null, checkedFile: File | null, file: File | null): boolean {
  return report !== null && report.issues.length === 0 && report.validCount > 0 && checkedFile !== null && checkedFile === file;
}
