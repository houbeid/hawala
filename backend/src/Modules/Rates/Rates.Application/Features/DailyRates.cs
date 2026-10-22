using Rates.Application.Abstractions;
using Rates.Contracts;

namespace Rates.Application.Features;

/// <summary>Implémentation de la porte publique du module, au-dessus du repository.</summary>
public sealed class DailyRates(IDailyRateRepository rates) : IDailyRates
{
    public async Task<DailyRateSnapshot?> GetAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var rate = await rates.GetAsync(date, cancellationToken);
        return rate is null ? null : new DailyRateSnapshot(rate.Date, rate.SaleRate, rate.PurchaseRate);
    }
}
