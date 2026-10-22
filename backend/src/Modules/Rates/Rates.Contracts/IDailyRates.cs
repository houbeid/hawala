using Hawala.SharedKernel.Money;

namespace Rates.Contracts;

/// <summary>Seule porte d'entrée des autres modules vers les taux du jour (ex. les Transferts).</summary>
public interface IDailyRates
{
    /// <summary>Les taux fixés pour une journée, ou null s'ils ne l'ont pas été (pas de transfert ce jour-là).</summary>
    Task<DailyRateSnapshot?> GetAsync(DateOnly date, CancellationToken cancellationToken);
}

/// <param name="SaleRate">Prix de vente : appliqué au client.</param>
/// <param name="PurchaseRate">Prix d'achat (taux du marché) : gardé sur le transfert pour le bénéfice.</param>
public sealed record DailyRateSnapshot(DateOnly Date, ExchangeRate SaleRate, ExchangeRate PurchaseRate);
