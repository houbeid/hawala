using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Rates.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.GetTransferSummary;

/// <summary>
/// Calcule les cartes (règles validées le 21 octobre 2026) : envoyés et entrées sur tous les transferts non annulés ;
/// sorties et livrés sur les transferts livrés seulement ; non livrés = ce que l'agence doit encore payer ;
/// bénéfice = somme sur la période, dans les deux sens ; taux = celui du dernier jour de la période.
/// POINT OUVERT : calcul en mémoire sur les transferts de la période (quelques dizaines par jour) ;
/// à passer en SQL si de longues périodes deviennent lentes.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class GetTransferSummaryQueryHandler(
    ITransferRepository transfers,
    IAgencyDirectory agencies,
    IDailyRates dailyRates,
    IClock clock)
    : IQueryHandler<GetTransferSummaryQuery, TransferSummary>
{
    public async Task<Result<TransferSummary>> Handle(GetTransferSummaryQuery query, CancellationToken cancellationToken)
    {
        if (query.AgencyId is not { } agencyId)
        {
            return TransfersErrors.Filter.AgencyRequired;
        }

        var all = await agencies.GetAllAsync(cancellationToken);
        var agency = all.FirstOrDefault(a => a.Id == agencyId);
        var other = all.Where(a => a.Id != agencyId).ToList();
        if (agency is null || other.Count != 1)
        {
            return TransfersErrors.Filter.AgencyNotFound;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return TransfersErrors.Filter.PeriodInvalid;
        }

        var filter = new TransferFilter(
            Start(from),
            Start(to.AddDays(1)),
            EitherAgencyId: agency.Id,
            Search: string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim());

        var active = (await transfers.ListAllAsync(filter, cancellationToken))
            .Where(t => t.Status != TransferStatus.Cancelled)
            .ToList();

        var sentByAgency = active.Where(t => t.SourceAgencyId == agency.Id).ToList();
        var sentByOther = active.Where(t => t.SourceAgencyId == other[0].Id).ToList();
        var rate = await dailyRates.GetAsync(to, cancellationToken);

        return new TransferSummary(
            agency.Id,
            agency.Currency,
            other[0].Id,
            other[0].Currency,
            from,
            to,
            Incoming: Sum(sentByAgency, agency.Currency),
            Outgoing: Sum(sentByOther.Where(t => t.Status == TransferStatus.Delivered), agency.Currency),
            SentByOther: Sum(sentByOther, other[0].Currency),
            DeliveredByOther: Sum(sentByAgency.Where(t => t.Status == TransferStatus.Delivered), other[0].Currency),
            NotDelivered: Sum(sentByOther.Where(t => t.Status == TransferStatus.NotDelivered), agency.Currency),
            ProfitMro: active.Sum(t => t.ProfitMro),
            SaleRate: rate?.SaleRate.MroPer5000Xof,
            PurchaseRate: rate?.PurchaseRate.MroPer5000Xof);
    }

    private static SummaryAmount Sum(IEnumerable<Transfer> transfers, Currency currency)
    {
        var list = transfers.ToList();
        return new SummaryAmount(list.Sum(t => t.AmountIn(currency)), list.Count);
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
