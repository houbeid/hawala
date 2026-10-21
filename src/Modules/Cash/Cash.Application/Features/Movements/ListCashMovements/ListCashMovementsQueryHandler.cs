using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.Movements;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Contracts;

namespace Cash.Application.Features.Movements.ListCashMovements;

/// <summary>
/// POINT OUVERT : la recherche porte sur la note seulement ; le nom du tiers viendra avec une recherche dans Parties.Contracts.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class ListCashMovementsQueryHandler(
    ICashMovementRepository movements,
    CashMovementViews views,
    IAgencyScope scope,
    IClock clock)
    : IQueryHandler<ListCashMovementsQuery, CashMovementList>
{
    public const int PageSize = 50;

    public async Task<Result<CashMovementList>> Handle(ListCashMovementsQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        if (page < 1)
        {
            return CashErrors.Filter.PageInvalid;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return CashErrors.Filter.PeriodInvalid;
        }

        CashMovementType? type = null;
        if (query.Type is not null)
        {
            if (!EnumNames.TryParse<CashMovementType>(query.Type, out var parsed))
            {
                return CashErrors.Movement.TypeInvalid;
            }

            type = parsed;
        }

        if (!scope.SeesAllAgencies && query.AgencyId is { } asked && asked != scope.AgencyId)
        {
            return CashErrors.Movement.AgencyNotAllowed;
        }

        var filter = new CashMovementFilter(
            Start(from),
            Start(to.AddDays(1)),
            query.AgencyId,
            type,
            query.TreasuryAccountId,
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim());

        var (items, total) = await movements.ListAsync(filter, (page - 1) * PageSize, PageSize, cancellationToken);
        var totals = await movements.TotalsAsync(filter, cancellationToken);
        var all = await movements.CountVisibleAsync(cancellationToken);
        var built = await views.BuildAsync(items, cancellationToken);

        var internals = Sum(totals.Where(t => t.Type == CashMovementType.InternalTransfer));
        var incoming = Sum(totals.Where(t => CashMovement.IsIncomingType(t.Type)));
        var outgoing = Sum(totals.Where(t => !CashMovement.IsIncomingType(t.Type) && t.Type != CashMovementType.InternalTransfer));
        return new CashMovementList(
            built, page, PageSize, total, all, incoming, outgoing, incoming.Amount - outgoing.Amount, internals);
    }

    private static CashAmount Sum(IEnumerable<CashMovementTypeTotal> totals)
    {
        var list = totals.ToList();
        return new CashAmount(list.Sum(t => t.Amount), list.Sum(t => t.Count));
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
