using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Application.Features.Transfers.GetTransferSummary;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.GetTransferDashboard;

/// <summary>
/// Calcule les cartes du tableau de bord pour chaque caisse (Superviseur seulement, contrôlé par la route).
/// Pas de rapport caisse / capital (décision du 21 octobre 2026).
/// POINT OUVERT : calcul en mémoire sur les transferts de la période, comme les cartes de l'écran des transferts.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class GetTransferDashboardQueryHandler(ITransferRepository transfers, IAgencyDirectory agencies, IClock clock)
    : IQueryHandler<GetTransferDashboardQuery, TransferDashboard>
{
    public async Task<Result<TransferDashboard>> Handle(GetTransferDashboardQuery query, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return TransfersErrors.Filter.PeriodInvalid;
        }

        var active = (await transfers.ListAllAsync(new TransferFilter(Start(from), Start(to.AddDays(1))), cancellationToken))
            .Where(t => t.Status != TransferStatus.Cancelled)
            .ToList();

        var caisses = (await agencies.GetAllAsync(cancellationToken))
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(agency =>
            {
                var sent = active.Where(t => t.SourceAgencyId == agency.Id).ToList();
                var delivered = sent.Where(t => t.Status == TransferStatus.Delivered).ToList();
                var notDelivered = sent.Where(t => t.Status == TransferStatus.NotDelivered).ToList();
                return new CaisseDashboard(
                    agency.Id,
                    agency.Currency,
                    Sent: new SummaryAmount(sent.Sum(t => t.AmountIn(agency.Currency)), sent.Count),
                    NotDelivered: new SummaryAmount(notDelivered.Sum(t => t.AmountIn(agency.Currency)), notDelivered.Count),
                    Delivered: new SummaryAmount(delivered.Sum(t => t.AmountIn(agency.Currency)), delivered.Count),
                    DeliveryRatePercent: sent.Count == 0 ? null : (decimal)delivered.Count / sent.Count * 100m);
            })
            .ToList();

        return new TransferDashboard(from, to, caisses);
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
