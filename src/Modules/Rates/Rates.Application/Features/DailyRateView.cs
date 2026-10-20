using Rates.Domain.DailyRates;

namespace Rates.Application.Features;

/// <summary>Les taux d'une journée, tels que le compte connecté a le droit de les voir.</summary>
/// <param name="SaleRate">Prix de vente (MRO pour 5 000 FCFA) : visible de tous.</param>
/// <param name="PurchaseRate">Prix d'achat : null sans la permission profit.view (règle validée : réservé au Superviseur).</param>
public sealed record DailyRateView(DateOnly Date, decimal SaleRate, decimal? PurchaseRate, DateTimeOffset UpdatedAt);

internal static class DailyRateViewMapper
{
    public static DailyRateView ToView(DailyRate rate, bool canSeePurchase) =>
        new(rate.Date, rate.SaleRate.MroPer5000Xof, canSeePurchase ? rate.PurchaseRate.MroPer5000Xof : null, rate.UpdatedAt);
}
