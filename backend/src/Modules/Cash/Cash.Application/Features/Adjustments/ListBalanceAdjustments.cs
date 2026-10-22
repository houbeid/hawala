using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.Adjustments;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Parties.Contracts;
using Treasury.Contracts;

namespace Cash.Application.Features.Adjustments;

/// <summary>Ajustements de solde d'une période (aujourd'hui par défaut), lisibles avec cash.budget.read, dans son périmètre.</summary>
public sealed record ListBalanceAdjustmentsQuery(Guid ActorId, Guid? AgencyId, DateOnly? From, DateOnly? To)
    : IQuery<IReadOnlyList<BalanceAdjustmentView>>;

/// <param name="Amount">Différence signée : positive = le solde a monté.</param>
public sealed record BalanceAdjustmentView(
    Guid Id,
    Guid AgencyId,
    AdjustmentTarget Target,
    Guid TargetId,
    string? TargetName,
    decimal Amount,
    Currency Currency,
    string Reason,
    DateTimeOffset OperationAt,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);

/// <summary>POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.</summary>
public sealed class ListBalanceAdjustmentsQueryHandler(
    IBalanceAdjustmentRepository adjustments,
    ITreasuryDirectory treasury,
    IPartyDirectory parties,
    IAgencyScope scope,
    IClock clock)
    : IQueryHandler<ListBalanceAdjustmentsQuery, IReadOnlyList<BalanceAdjustmentView>>
{
    public async Task<Result<IReadOnlyList<BalanceAdjustmentView>>> Handle(
        ListBalanceAdjustmentsQuery query, CancellationToken cancellationToken)
    {
        if (!scope.SeesAllAgencies && query.AgencyId is { } asked && asked != scope.AgencyId)
        {
            return CashErrors.Movement.AgencyNotAllowed;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return CashErrors.Filter.PeriodInvalid;
        }

        var found = await adjustments.ListAsync(
            query.AgencyId,
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            cancellationToken);

        var accounts = await treasury.GetByIdsAsync(
            [.. found.Where(a => a.Target == AdjustmentTarget.TreasuryAccount).Select(a => a.TargetId).Distinct()], cancellationToken);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
        var partyNames = await parties.GetNamesAsync(
            [.. found.Where(a => a.Target == AdjustmentTarget.Party).Select(a => a.TargetId).Distinct()], cancellationToken);

        return found.Select(a => new BalanceAdjustmentView(
                a.Id, a.AgencyId, a.Target, a.TargetId,
                a.Target == AdjustmentTarget.TreasuryAccount ? accountNames.GetValueOrDefault(a.TargetId) : partyNames.GetValueOrDefault(a.TargetId),
                a.Amount, a.Currency, a.Reason, a.OperationAt, a.CreatedBy, a.CreatedAt))
            .ToList();
    }
}
