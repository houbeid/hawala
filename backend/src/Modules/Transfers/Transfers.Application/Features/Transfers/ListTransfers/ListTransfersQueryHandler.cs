using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.ListTransfers;

/// <summary>
/// Un agent ne voit que les transferts envoyés et reçus par son agence (filtre global) ;
/// le Superviseur voit tout, ou une agence choisie.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class ListTransfersQueryHandler(
    ITransferRepository transfers,
    TransferViews views,
    IAgencyScope scope,
    IClock clock)
    : IQueryHandler<ListTransfersQuery, TransferList>
{
    public const int PageSize = 50;

    public async Task<Result<TransferList>> Handle(ListTransfersQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page ?? 1;
        if (page < 1)
        {
            return TransfersErrors.Filter.PageInvalid;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return TransfersErrors.Filter.PeriodInvalid;
        }

        TransferStatus? status = null;
        if (query.Status is not null)
        {
            if (!EnumNames.TryParse<TransferStatus>(query.Status, out var parsedStatus))
            {
                return TransfersErrors.Filter.StatusInvalid;
            }

            status = parsedStatus;
        }

        TransferDirection? direction = null;
        if (query.Direction is not null)
        {
            if (!EnumNames.TryParse<TransferDirection>(query.Direction, out var parsedDirection))
            {
                return TransfersErrors.Filter.DirectionInvalid;
            }

            direction = parsedDirection;
        }

        // Agence de référence : celle de l'agent (il ne peut pas en demander une autre), ou celle choisie par le Superviseur.
        Guid? agency;
        if (scope.SeesAllAgencies)
        {
            agency = query.AgencyId;
        }
        else
        {
            if (query.AgencyId is { } asked && asked != scope.AgencyId)
            {
                return TransfersErrors.Filter.AgencyNotAllowed;
            }

            agency = scope.AgencyId;
        }

        if (direction is not null && agency is null)
        {
            return TransfersErrors.Filter.DirectionNeedsAgency;
        }

        var filter = new TransferFilter(
            Start(from),
            Start(to.AddDays(1)),
            status,
            SourceAgencyId: direction == TransferDirection.Sent ? agency : null,
            DestinationAgencyId: direction == TransferDirection.Received ? agency : null,
            EitherAgencyId: direction is null ? agency : null,
            Search: string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim());

        var (items, total) = await transfers.ListAsync(filter, (page - 1) * PageSize, PageSize, cancellationToken);
        var built = await views.BuildAsync(query.ActorId, items, agency, cancellationToken);
        var all = await transfers.CountVisibleAsync(cancellationToken);

        return new TransferList(built, page, PageSize, total, all);
    }

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
