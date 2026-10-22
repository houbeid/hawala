using Rates.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Contracts;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers;

/// <summary>
/// Implémentation de la porte publique du contrôle de cohérence (règles validées le 21 octobre 2026) :
/// taux inhabituel (écart de plus de 5 % entre le prix de vente du transfert et celui du jour de l'opération,
/// sur un transfert créé ou modifié depuis l'instant donné), transferts annulés, livraisons sans code.
/// </summary>
public sealed class TransferMonitor(ITransferRepository transfers, IDailyRates dailyRates) : ITransferMonitor
{
    /// <summary>Seuil validé : 5 % (avec 3 600 : hors de 3 420 à 3 780).</summary>
    public const decimal UnusualRateThreshold = 0.05m;

    public async Task<IReadOnlyList<TransferAlertEvent>> ListAlertsAsync(
        Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var changed = await transfers.ListChangedSinceAsync(agencyId, since, cancellationToken);
        var alerts = new List<TransferAlertEvent>();
        var rates = new Dictionary<DateOnly, DailyRateSnapshot?>();

        foreach (var transfer in changed)
        {
            var rateEvent = transfer.Events
                .Where(e => e.At >= since && e.Type is TransferEventType.Created or TransferEventType.Edited)
                .OrderByDescending(e => e.At)
                .FirstOrDefault();
            if (rateEvent is not null)
            {
                var day = DateOnly.FromDateTime(transfer.OperationAt.UtcDateTime);
                if (!rates.TryGetValue(day, out var daily))
                {
                    daily = await dailyRates.GetAsync(day, cancellationToken);
                    rates[day] = daily;
                }

                if (daily is not null && IsUnusual(transfer.SaleRate.MroPer5000Xof, daily.SaleRate.MroPer5000Xof))
                {
                    alerts.Add(new TransferAlertEvent(
                        transfer.Id, transfer.Reference, TransferAlertKind.UnusualRate, rateEvent.At, null,
                        transfer.SaleRate.MroPer5000Xof, daily.SaleRate.MroPer5000Xof));
                }
            }

            if (transfer.Status == TransferStatus.Cancelled && transfer.CancelledAt >= since)
            {
                alerts.Add(new TransferAlertEvent(
                    transfer.Id, transfer.Reference, TransferAlertKind.Cancelled, transfer.CancelledAt!.Value,
                    transfer.CancellationReason, null, null));
            }

            if (transfer.DeliveredWithoutCode && transfer.DeliveredAt >= since)
            {
                alerts.Add(new TransferAlertEvent(
                    transfer.Id, transfer.Reference, TransferAlertKind.DeliveredWithoutCode, transfer.DeliveredAt!.Value,
                    transfer.DeliveryWithoutCodeReason, null, null));
            }
        }

        return [.. alerts.OrderBy(a => a.At)];
    }

    /// <summary>Écart relatif au prix de vente du jour strictement supérieur à 5 %.</summary>
    public static bool IsUnusual(decimal saleRate, decimal dailySaleRate) =>
        dailySaleRate > 0m && Math.Abs(saleRate - dailySaleRate) / dailySaleRate > UnusualRateThreshold;
}
