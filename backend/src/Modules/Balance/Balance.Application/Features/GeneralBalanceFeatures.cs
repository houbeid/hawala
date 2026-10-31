using System.Globalization;
using Audit.Contracts;
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

/// <summary>Composants d'une caisse (ou leur total), dans la devise indiquée ; totaux seulement, jamais le détail par tiers.</summary>
public sealed record ComponentsView(
    Currency Currency,
    decimal Cash,
    decimal Banks,
    decimal Debtors,
    decimal Creditors,
    decimal Undelivered,
    decimal OwnerContributions,
    decimal Budget)
{
    public static ComponentsView From(CaisseComponents c) =>
        new(c.Currency, c.Cash, c.Banks, c.Debtors, c.Creditors, c.Undelivered, c.OwnerContributions, c.Budget);
}

/// <summary>Bilan général en direct, au taux donné ; pleine précision (l'affichage arrondit).</summary>
/// <param name="Rate">Taux du bilan : MRO pour 5 000 FCFA.</param>
/// <param name="DakarInMro">La caisse de Dakar convertie au taux du bilan.</param>
/// <param name="Total">Les composants des deux caisses additionnés, en MRO.</param>
/// <param name="SavedProfitsMro">
/// Bénéfices du jour enregistrés des deux agences, à titre d'information (hors formule) : jour par jour, la photo « des deux
/// agences » si elle existe, sinon la somme des photos de chaque agence.
/// </param>
public sealed record GeneralBalanceView(
    DateOnly Date,
    decimal Rate,
    ComponentsView Nouakchott,
    ComponentsView Dakar,
    ComponentsView DakarInMro,
    ComponentsView Total,
    decimal GeneralBalance,
    decimal? Capital,
    decimal OwnerContributionsMro,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfitsMro);

/// <summary>Photo du bilan général : les composants de chaque caisse dans sa devise et leur total en MRO au taux de la photo.</summary>
public sealed record GeneralBalanceSnapshotView(
    Guid Id,
    DateOnly Date,
    decimal Rate,
    ComponentsView Nouakchott,
    ComponentsView Dakar,
    ComponentsView Total,
    decimal GeneralBalance,
    decimal? Capital,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfitsMro,
    Guid SavedBy,
    DateTimeOffset SavedAt)
{
    public static GeneralBalanceSnapshotView From(GeneralBalanceSnapshot s)
    {
        var nouakchott = new CaisseComponents(
            Currency.MRO, s.NouakchottCash, s.NouakchottBanks, s.NouakchottDebtors, s.NouakchottCreditors,
            s.NouakchottUndelivered, s.NouakchottOwnerContributions);
        var dakar = new CaisseComponents(
            Currency.XOF, s.DakarCash, s.DakarBanks, s.DakarDebtors, s.DakarCreditors, s.DakarUndelivered, s.DakarOwnerContributions);
        var figures = GeneralBalanceFigures.Compute(nouakchott, dakar, ExchangeRate.Create(s.Rate).Value, s.Capital);

        return new GeneralBalanceSnapshotView(
            s.Id, s.Date, s.Rate, ComponentsView.From(nouakchott), ComponentsView.From(dakar), ComponentsView.From(figures.Total),
            s.GeneralBalance, s.Capital, s.CapitalUsed, s.NetResult, s.NetResultPercent, s.SavedProfitsMro, s.SavedBy, s.SavedAt);
    }
}

public sealed record GeneralCapitalChangeView(Guid Id, decimal Amount, string? Reason, Guid SetBy, DateTimeOffset SetAt);

/// <summary>Capital général actuel (MRO) et ses saisies, de la plus récente à la plus ancienne.</summary>
public sealed record GeneralCapitalView(decimal? Current, IReadOnlyList<GeneralCapitalChangeView> History);

/// <summary>Bilan général en direct (balance.view), au taux du bilan donné par l'écran.</summary>
public sealed record GetGeneralBalanceQuery(Guid ActorId, decimal? Rate) : IQuery<GeneralBalanceView>;

/// <summary>« Sauvegarder le bilan » général du jour (balance.manage), avec son taux.</summary>
public sealed record SaveGeneralBalanceCommand(Guid ActorId, decimal? Rate) : ICommand<GeneralBalanceSnapshotView>;

/// <summary>Photos du bilan général (balance.view) ; 30 derniers jours par défaut.</summary>
public sealed record ListGeneralBalanceSnapshotsQuery(Guid ActorId, DateOnly? From, DateOnly? To)
    : IQuery<IReadOnlyList<GeneralBalanceSnapshotView>>;

/// <summary>Saisir le capital général en MRO (balance.manage) ; motif facultatif.</summary>
public sealed record SetGeneralCapitalCommand(Guid ActorId, decimal? Amount, string? Reason) : ICommand;

/// <summary>Capital général et historique de ses saisies (balance.view).</summary>
public sealed record GetGeneralCapitalQuery(Guid ActorId) : IQuery<GeneralCapitalView>;

/// <summary>
/// Bilan général (décisions du 31 octobre 2026) : la caisse en MRO (Nouakchott) et la caisse en FCFA (Dakar) convertie au taux
/// du bilan, que l'écran envoie (prérempli au prix d'achat du jour, modifiable) : le Bilan ne lit pas le module Taux.
/// Résultat net = bilan général − (capital général + apports nets du patron des deux caisses). Photo du jour courant, remplacée
/// le même jour. Superviseur seulement.
/// </summary>
public sealed class GeneralBalanceHandlers(
    IAgencyDirectory agencies,
    ICashBudgets budgets,
    ILedger ledger,
    IProfitHistory profits,
    IGeneralCapitalRepository capital,
    IGeneralBalanceSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : IQueryHandler<GetGeneralBalanceQuery, GeneralBalanceView>,
      ICommandHandler<SaveGeneralBalanceCommand, GeneralBalanceSnapshotView>,
      IQueryHandler<ListGeneralBalanceSnapshotsQuery, IReadOnlyList<GeneralBalanceSnapshotView>>,
      ICommandHandler<SetGeneralCapitalCommand>,
      IQueryHandler<GetGeneralCapitalQuery, GeneralCapitalView>
{
    public async Task<Result<GeneralBalanceView>> Handle(GetGeneralBalanceQuery query, CancellationToken cancellationToken)
    {
        var computed = await ComputeAsync(query.Rate, cancellationToken);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        var (figures, savedProfits) = computed.Value;
        return new GeneralBalanceView(
            Today(), figures.Rate.MroPer5000Xof, ComponentsView.From(figures.Nouakchott), ComponentsView.From(figures.Dakar),
            ComponentsView.From(figures.DakarInMro), ComponentsView.From(figures.Total), figures.GeneralBalance, figures.Capital,
            figures.OwnerContributionsMro, figures.CapitalUsed, figures.NetResult, figures.NetResultPercent, savedProfits);
    }

    public async Task<Result<GeneralBalanceSnapshotView>> Handle(SaveGeneralBalanceCommand command, CancellationToken cancellationToken)
    {
        var computed = await ComputeAsync(command.Rate, cancellationToken);
        if (computed.IsFailure)
        {
            return computed.Error;
        }

        var (figures, savedProfits) = computed.Value;
        var now = clock.UtcNow;
        var today = Today();
        var snapshot = await snapshots.GetAsync(today, cancellationToken);
        if (snapshot is null)
        {
            snapshot = GeneralBalanceSnapshot.Create(today, figures, savedProfits, command.ActorId, now);
            snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Replace(figures, savedProfits, command.ActorId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return GeneralBalanceSnapshotView.From(snapshot);
    }

    public async Task<Result<IReadOnlyList<GeneralBalanceSnapshotView>>> Handle(
        ListGeneralBalanceSnapshotsQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? Today();
        var from = query.From ?? to.AddDays(-(BalanceHandlers.DefaultHistoryDays - 1));
        if (from > to)
        {
            return BalanceErrors.Filter.PeriodInvalid;
        }

        var found = await snapshots.ListAsync(from, to, cancellationToken);
        return found.Select(GeneralBalanceSnapshotView.From).ToList();
    }

    public async Task<Result> Handle(SetGeneralCapitalCommand command, CancellationToken cancellationToken)
    {
        var change = GeneralCapitalChange.Create(command.Amount, command.Reason, command.ActorId, clock.UtcNow);
        if (change.IsFailure)
        {
            return change.Error;
        }

        var previous = (await capital.ListAsync(cancellationToken)).FirstOrDefault()?.Amount;
        capital.Add(change.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Une nouvelle saisie au même montant reste tracée (motif), sans champ changé.
        var previousText = previous?.ToString("0.##", CultureInfo.InvariantCulture);
        var amountText = change.Value.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var audited = await audit.RecordAsync(
            new AuditRecord(
                "Balance.GeneralCapital.Set",
                "GeneralBalance",
                EntityId: null,
                command.ActorId,
                Reason: change.Value.Reason,
                Changes: previousText == amountText ? [] : [new AuditChange("generalCapital", previousText, amountText)]),
            cancellationToken);
        return audited.IsFailure ? audited.Error : Result.Success();
    }

    public async Task<Result<GeneralCapitalView>> Handle(GetGeneralCapitalQuery query, CancellationToken cancellationToken)
    {
        var history = await capital.ListAsync(cancellationToken);
        return new GeneralCapitalView(
            history.FirstOrDefault()?.Amount,
            [.. history.Select(c => new GeneralCapitalChangeView(c.Id, c.Amount, c.Reason, c.SetBy, c.SetAt))]);
    }

    /// <summary>Les deux caisses lues à la Caisse et au grand livre, puis combinées au taux du bilan.</summary>
    private async Task<Result<(GeneralBalanceFigures Figures, decimal SavedProfits)>> ComputeAsync(
        decimal? rateValue, CancellationToken cancellationToken)
    {
        if (rateValue is not { } value)
        {
            return BalanceErrors.Rate.Required;
        }

        var rate = ExchangeRate.Create(value);
        if (rate.IsFailure)
        {
            return rate.Error;
        }

        var all = await agencies.GetAllAsync(cancellationToken);
        var mroAgency = all.FirstOrDefault(a => a.Currency == Currency.MRO);
        var xofAgency = all.FirstOrDefault(a => a.Currency == Currency.XOF);
        if (mroAgency is null || xofAgency is null)
        {
            return BalanceErrors.General.CaissesMissing;
        }

        var nouakchott = await ComponentsAsync(mroAgency, cancellationToken);
        var dakar = await ComponentsAsync(xofAgency, cancellationToken);
        if (nouakchott is null || dakar is null)
        {
            return BalanceErrors.General.CaissesMissing;
        }

        var current = (await capital.ListAsync(cancellationToken)).FirstOrDefault()?.Amount;
        var figures = GeneralBalanceFigures.Compute(nouakchott, dakar, rate.Value, current);
        var savedProfits = await profits.SumSavedProfitsOfBothAgenciesAsync(cancellationToken);
        return (figures, savedProfits);
    }

    private async Task<CaisseComponents?> ComponentsAsync(AgencySummary agency, CancellationToken cancellationToken)
    {
        var budget = await budgets.GetBudgetAsync(agency.Id, cancellationToken);
        if (budget is null)
        {
            return null;
        }

        return new CaisseComponents(
            agency.Currency, budget.Cash, budget.Banks, budget.Debtors, budget.Creditors, budget.Undelivered,
            await OwnerContributions.ReadAsync(ledger, agency.Id, cancellationToken));
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
}
