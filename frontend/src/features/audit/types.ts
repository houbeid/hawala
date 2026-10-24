// Journal d'audit, copiés sur Hawala.Api/Endpoints/Audit/AuditEndpoints.cs (AuditLine, AuditActor, AuditResponse) et
// Audit.Contracts/AuditRecord.cs (AuditChange). Seulement les actions (réglages et corrections), règle du 22 octobre 2026.

export interface AuditChange {
  field: string;
  before: string | null;
  after: string | null;
}

export interface AuditLine {
  id: string;
  occurredAt: string;
  /** Code stable de l'action (« Identity.Account.Blocked ») : le texte lisible est construit par l'interface. */
  action: string;
  entityType: string;
  entityId: string | null;
  /** Nom du compte ou du tiers concerné ; null pour un autre type. */
  entityName: string | null;
  /** Auteur ; null pour une action du système. */
  actor: { id: string; name: string } | null;
  agencyId: string | null;
  reason: string | null;
  changes: AuditChange[];
  ipAddress: string | null;
  device: string | null;
}

/** GET /audit (audit.view) : 50 par page, plus récent d'abord ; aujourd'hui par défaut. */
export interface AuditResponse {
  items: AuditLine[];
  page: number;
  totalCount: number;
}

export type AuditParams = {
  from?: string;
  to?: string;
  /** Début du code d'action (« Identity.Account », « Transfers »…). */
  action?: string;
  agencyId?: string;
  page?: number;
};

export const AUDIT_PAGE_SIZE = 50;

/** Familles d'actions proposées dans le filtre (début du code). */
export const AUDIT_FAMILIES = [
  'Identity.Account',
  'Identity.Session',
  'Parties',
  'Treasury',
  'Transfers',
  'Cash',
  'Rates',
  'Balance',
  'Organization',
] as const;
