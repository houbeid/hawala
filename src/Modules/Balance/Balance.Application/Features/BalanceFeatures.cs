using Balance.Application.Abstractions;
using Balance.Domain;
using Cash.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Organization.Contracts;
using Profit.Contracts;

namespace Balance.Application.Features;

/// <summary>Le bilan d'une caisse, dans sa devise, en pleine précision (l'affichage arrondit).</summary>
/// <param name="SavedProfitsMro">« الأرباح المحفوظة » de la caisse : cumul des bénéfices du jour enregistrés (en MRO), information.</param>
public sealed record BalanceView(
    Guid AgencyId,
    Currency Currency,
    DateOnly Date,
    decimal Budget,
    decimal? InitialCapital,
    decimal OwnerContributions,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfitsMro);

/// <summary>Les deux caisses côte à côte, sans total converti (décision du 21 octobre 2026).</summary>
public sealed record BalanceOverview(DateOnly Date, IReadOnlyList<BalanceView> Caisses);

public sealed record BalanceSnapshotView(
    Guid Id,
    Guid AgencyId,
    Currency Currency,
    DateOnly Date,
    decimal Budget,
    decimal? InitialCapital,
    decimal OwnerContributions,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfitsMro,
    Guid SavedBy,
    DateTimeOffset SavedAt)
{
    public static BalanceSnapshotView From(BalanceSnapshot s) =>
        new(s.Id, s.AgencyId, s.Currency, s.Date, s.Budget, s.InitialCapital, s.OwnerContributions, s.CapitalUsed,
            s.NetResult, s.NetResultPercent, s.SavedProfitsMro, s.SavedBy, s.SavedAt);
}

public sealed record CapitalChangeView(Guid Id, decimal Amount, Currency Currency, string? Reason, Guid SetBy, DateTimeOffset SetAt);

/// <summary>Capital initial actuel d'une caisse et ses saisies.</summary>
public sealed record CaisseCapitalView(Guid AgencyId, Currency Currency, decimal? Current, IReadOnlyList<CapitalChangeView> History);

/// <summary>Bilan en direct des deux caisses (balance.view).</summary>
public sealed record GetBalanceQuery(Guid ActorId) : IQuery<BalanceOverview>;

/// <summary>« Enregistrer le bilan » du jour d'une caisse (balance.manage).</summary>
public sealed record SaveBalanceCommand(Guid ActorId, Guid? AgencyId) : ICommand<BalanceSnapshotView>;

/// <summary>Historique des bilans enregistrés (balance.view), d'une caisse ou des deux ; 30 derniers jours par défaut.</summary>
public sealed record ListBalanceSnapshotsQuery(Guid ActorId, Guid? AgencyId, DateOnly? From, DateOnly? To)
    : IQuery<IReadOnlyList<BalanceSnapshotView>>;

/// <summary>Saisir le capital initial d'une caisse (balance.manage), dans sa devise ; motif facultatif.</summary>
public sealed record SetInitialCapitalCommand(Guid ActorId, Guid? AgencyId, decimal? Amount, string? Reason) : ICommand;

/// <summary>Capital initial des deux caisses et historique de leurs saisies (balance.view).</summary>
public sealed record GetCapitalQuery(Guid ActorId) : IQuery<IReadOnlyList<CaisseCapitalView>>;

/// <summary>
/// Bilan par caisse (décision du 21 octobre 2026 : « deux caisses, deux capitaux », pas de total converti) : pour chaque agence,
/// budget de la caisse (Caisse) − capital pris en compte (capital initial de la caisse + approvisionnements − retraits du patron
/// de cette caisse), dans sa devise ; le taux du jour n'intervient pas. « الأرباح المحفوظة » de la caisse en information.
/// Photo du jour courant par caisse, remplacée le même jour. Superviseur seulement.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit des saisies de capital arrivera avec le module Audit ; chaque saisie garde déjà son auteur.
/// </summary>
public sealed class BalanceHandlers(
    IAgencyDirectory agencies,
    ICashBudgets budgets,
    ILedger ledger,
    IProfitHistory profits,
    ICapitalRepository capital,
    IBalanceSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IQueryHandler<GetBalanceQuery, BalanceOverview>,
      ICommandHandler<SaveBalanceCommand, BalanceSnapshotView>,
      IQueryHandler<ListBalanceSnapshotsQuery, IReadOnlyList<BalanceSnapshotView>>,
      ICommandHandler<SetInitialCapitalCommand>,
      IQueryHandler<GetCapitalQuery, IReadOnlyList<CaisseCapitalView>>
{
    public const int DefaultHistoryDays = 30;

    public async Task<Result<BalanceOverview>> Handle(GetBalanceQuery query, CancellationToken cancellationToken)
    {
        var caisses = new List<BalanceView>();
        foreach (var agency in (await agencies.GetAllAsync(cancellationToken)).OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var view = await ComputeAsync(agency, cancellationToken);
            if (view is not null)
            {
                caisses.Add(view);
            }
        }

        return new BalanceOverview(Today(), caisses);
    }

    public async Task<Result<BalanceSnapshotView>> Handle(SaveBalanceCommand command, CancellationToken cancellationToken)
    {
        var agency = await ResolveAgencyAsync(command.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var view = await ComputeAsync(agency.Value, cancellationToken);
        if (view is null)
        {
            return BalanceErrors.Agency.NotFound;
        }

        var figures = new BalanceFigures(
            view.Currency, view.Budget, view.InitialCapital, view.OwnerContributions, view.CapitalUsed, view.NetResult, view.NetResultPercent);
        var now = clock.UtcNow;
        var snapshot = await snapshots.GetAsync(agency.Value.Id, view.Date, cancellationToken);
        if (snapshot is null)
        {
            snapshot = BalanceSnapshot.Create(agency.Value.Id, view.Date, figures, view.SavedProfitsMro, command.ActorId, now);
            snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Replace(figures, view.SavedProfitsMro, command.ActorId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BalanceSnapshotView.From(snapshot);
    }

    public async Task<Result<IReadOnlyList<BalanceSnapshotView>>> Handle(
        ListBalanceSnapshotsQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? Today();
        var from = query.From ?? to.AddDays(-(DefaultHistoryDays - 1));
        if (from > to)
        {
            return BalanceErrors.Filter.PeriodInvalid;
        }

        var found = await snapshots.ListAsync(query.AgencyId, from, to, cancellationToken);
        return found.Select(BalanceSnapshotView.From).ToList();
    }

    public async Task<Result> Handle(SetInitialCapitalCommand command, CancellationToken cancellationToken)
    {
        var agency = await ResolveAgencyAsync(command.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var change = CapitalChange.Create(agency.Value.Id, agency.Value.Currency, command.Amount, command.Reason, command.ActorId, clock.UtcNow);
        if (change.IsFailure)
        {
            return change.Error;
        }

        capital.Add(change.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<CaisseCapitalView>>> Handle(GetCapitalQuery query, CancellationToken cancellationToken)
    {
        var history = await capital.ListAsync(cancellationToken);
        return (await agencies.GetAllAsync(cancellationToken))
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .Select(a =>
            {
                var changes = history.Where(c => c.AgencyId == a.Id).ToList();
                return new CaisseCapitalView(
                    a.Id, a.Currency, changes.FirstOrDefault()?.Amount,
                    [.. changes.Select(c => new CapitalChangeView(c.Id, c.Amount, c.Currency, c.Reason, c.SetBy, c.SetAt))]);
            })
            .ToList();
    }

    private async Task<Result<AgencySummary>> ResolveAgencyAsync(Guid? agencyId, CancellationToken cancellationToken)
    {
        if (agencyId is not { } id)
        {
            return BalanceErrors.Agency.Required;
        }

        var agency = await agencies.GetByIdAsync(id, cancellationToken);
        return agency is null ? BalanceErrors.Agency.NotFound : agency;
    }

    /// <summary>Bilan d'une caisse ; null si son budget ne peut pas être lu.</summary>
    private async Task<BalanceView?> ComputeAsync(AgencySummary agency, CancellationToken cancellationToken)
    {
        var budget = await budgets.GetBudgetAsync(agency.Id, cancellationToken);
        if (budget is null)
        {
            return null;
        }

        var current = await capital.GetCurrentAsync(agency.Id, cancellationToken);
        var figures = BalanceFigures.Compute(new BalanceInputs(
            agency.Currency, budget.Budget, current?.Amount, await OwnerContributionsAsync(agency.Id, cancellationToken)));
        var savedProfits = await profits.SumSavedProfitsAsync(agency.Id, cancellationToken);

        return new BalanceView(
            agency.Id, agency.Currency, Today(), figures.Budget, figures.InitialCapital, figures.OwnerContributions,
            figures.CapitalUsed, figures.NetResult, figures.NetResultPercent, savedProfits);
    }

    /// <summary>
    /// Approvisionnements − retraits du patron dans une caisse : « Apports du patron » est crédité à chaque approvisionnement,
    /// son solde (débit − crédit) est donc négatif quand le patron a apporté plus qu'il n'a retiré.
    /// </summary>
    private async Task<decimal> OwnerContributionsAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var account = await ledger.FindSystemAccountAsync(SystemAccountRole.OwnerContributions, agencyId, cancellationToken);
        if (account is not { } id)
        {
            return 0m;
        }

        var balances = await ledger.GetBalancesAsync([id], asOf: null, cancellationToken);
        return balances.IsSuccess ? -balances.Value.Sum(b => b.Balance.Amount) : 0m;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
}
