// Contrats du module Taux, copiés sur le backend :
// - DailyRateView : Rates.Application/Features/DailyRateView.cs
// - SetDailyRateRequest : Hawala.Api/Endpoints/Rates/RateEndpoints.cs
// Taux au format « 5 000 FCFA = ? MRO », 2 décimales au plus.

export interface DailyRate {
  /** aaaa-mm-jj (DateOnly). */
  date: string;
  /** Prix de vente (appliqué au client) : visible de tous. */
  saleRate: number;
  /** Prix d'achat (taux du marché) : null sans la permission profit.view. */
  purchaseRate: number | null;
  /** Dernière modification (ISO 8601). */
  updatedAt: string;
}

export interface SetDailyRateRequest {
  purchaseRate: number;
  saleRate: number;
}
