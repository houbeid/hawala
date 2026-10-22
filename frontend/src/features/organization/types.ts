// Réponse de GET /api/v1/agencies, copiée sur Organization.Contracts/AgencySummary.cs
// (camelCase, Currency en texte).

export type Currency = 'MRO' | 'XOF';

export interface Agency {
  id: string;
  /** NKC ou DKR : ne change jamais. */
  code: string;
  name: string;
  /** « Mauritania », « Senegal ». */
  country: string;
  currency: Currency;
  /** #RRGGBB, modifiable par le Superviseur (agencies.manage). */
  color: string;
  timeZoneId: string;
}
