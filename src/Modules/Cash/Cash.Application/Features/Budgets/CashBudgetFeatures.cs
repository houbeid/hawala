using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.Budgets;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;

namespace Cash.Application.Features.Budgets;

/// <summary>Budget de la caisse en direct (cash.budget.read).</summary>
public sealed record GetCashBudgetQuery(Guid ActorId, Guid? AgencyId) : IQuery<CashBudgetView>;

public sealed class GetCashBudgetQueryHandler(CashBudgetCalculator calculator)
    : IQueryHandler<GetCashBudgetQuery, CashBudgetView>
{
    public async Task<Result<CashBudgetView>> Handle(GetCashBudgetQuery query, CancellationToken cancellationToken)
    {
        var agency = await calculator.ResolveAgencyAsync(query.AgencyId, cancellationToken);
        return agency.IsFailure ? agency.Error : await calculator.ComputeAsync(agency.Value, cancellationToken);
    }
}

/// <summary>Une photo enregistrée du budget.</summary>
public sealed record CashBudgetSnapshotView(
    Guid Id,
    Guid AgencyId,
    DateOnly Date,
    Currency Currency,
    decimal Cash,
    decimal Banks,
    decimal Debtors,
    decimal Creditors,
    decimal Undelivered,
    decimal Budget,
    Guid SavedBy,
    DateTimeOffset SavedAt,
    DateTimeOffset? CheckedSince,
    IReadOnlyList<CoherenceAlert> Alerts)
{
    public static CashBudgetSnapshotView From(CashBudgetSnapshot s) =>
        new(s.Id, s.AgencyId, s.Date, s.Currency, s.Cash, s.Banks, s.Debtors, s.Creditors, s.Undelivered, s.Budget,
            s.SavedBy, s.SavedAt, s.CheckedSince, s.Alerts);
}

/// <summary>Enregistrer le budget du jour (cash.budget.save : Superviseur, Caissier). Les chiffres sont calculés, jamais saisis.</summary>
public sealed record SaveCashBudgetCommand(Guid ActorId, Guid? AgencyId) : ICommand<CashBudgetSnapshotView>;

/// <summary>
/// Règle validée (21 octobre 2026) : une photo par agence et par jour ; réenregistrer le même jour remplace la précédente ;
/// les jours passés ne changent jamais (seul le jour courant s'enregistre). Les alertes du contrôle de cohérence
/// (depuis le dernier budget d'un jour passé) sont gardées avec la photo.
/// POINT OUVERT : le jour est lu en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class SaveCashBudgetCommandHandler(
    CashBudgetCalculator calculator,
    CoherenceChecker checker,
    ICashBudgetSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<SaveCashBudgetCommand, CashBudgetSnapshotView>
{
    public async Task<Result<CashBudgetSnapshotView>> Handle(SaveCashBudgetCommand command, CancellationToken cancellationToken)
    {
        var agency = await calculator.ResolveAgencyAsync(command.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var budget = await calculator.ComputeAsync(agency.Value, cancellationToken);
        if (budget.IsFailure)
        {
            return budget.Error;
        }

        var since = await checker.DefaultSinceAsync(agency.Value.Id, cancellationToken);
        var alerts = await checker.CheckAsync(agency.Value, budget.Value, since, cancellationToken);

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var snapshot = await snapshots.GetForUpdateAsync(agency.Value.Id, today, cancellationToken);
        if (snapshot is null)
        {
            snapshot = CashBudgetSnapshot.Create(
                agency.Value.Id, today, agency.Value.Currency, budget.Value.Figures, command.ActorId, now, alerts, since);
            snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Replace(budget.Value.Figures, command.ActorId, now, alerts, since);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CashBudgetSnapshotView.From(snapshot);
    }
}

/// <summary>Historique des budgets enregistrés (cash.budget.read), pour la courbe ; 30 derniers jours par défaut.</summary>
public sealed record ListCashBudgetSnapshotsQuery(Guid ActorId, Guid? AgencyId, DateOnly? From, DateOnly? To)
    : IQuery<IReadOnlyList<CashBudgetSnapshotView>>;

public sealed class ListCashBudgetSnapshotsQueryHandler(
    CashBudgetCalculator calculator,
    ICashBudgetSnapshotRepository snapshots,
    IClock clock)
    : IQueryHandler<ListCashBudgetSnapshotsQuery, IReadOnlyList<CashBudgetSnapshotView>>
{
    public const int DefaultDays = 30;

    public async Task<Result<IReadOnlyList<CashBudgetSnapshotView>>> Handle(
        ListCashBudgetSnapshotsQuery query, CancellationToken cancellationToken)
    {
        var agency = await calculator.ResolveAgencyAsync(query.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var to = query.To ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? to.AddDays(-(DefaultDays - 1));
        if (from > to)
        {
            return CashErrors.Filter.PeriodInvalid;
        }

        var found = await snapshots.ListAsync(agency.Value.Id, from, to, cancellationToken);
        return found.Select(CashBudgetSnapshotView.From).ToList();
    }
}
