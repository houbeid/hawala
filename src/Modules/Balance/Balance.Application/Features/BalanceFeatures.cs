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
using Rates.Contracts;

namespace Balance.Application.Features;

/// <summary>Le bilan général tel qu'on le montre (écran « الميزانية العامة »), en pleine précision ; l'affichage arrondit.</summary>
/// <param name="SavedProfits">« الأرباح المحفوظة » : cumul des bénéfices du jour enregistrés, à titre d'information.</param>
public sealed record BalanceView(
    DateOnly Date,
    decimal Rate,
    Guid MroAgencyId,
    decimal MroBudget,
    Guid XofAgencyId,
    decimal XofBudget,
    decimal XofBudgetInMro,
    decimal GeneralBalance,
    decimal? InitialCapital,
    decimal OwnerContributionsMro,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfits);

public sealed record BalanceSnapshotView(
    Guid Id,
    DateOnly Date,
    decimal Rate,
    decimal MroBudget,
    decimal XofBudget,
    decimal GeneralBalance,
    decimal? InitialCapital,
    decimal OwnerContributionsMro,
    decimal? CapitalUsed,
    decimal? NetResult,
    decimal? NetResultPercent,
    decimal SavedProfits,
    Guid SavedBy,
    DateTimeOffset SavedAt)
{
    public static BalanceSnapshotView From(BalanceSnapshot s) =>
        new(s.Id, s.Date, s.Rate, s.MroBudget, s.XofBudget, s.GeneralBalance, s.InitialCapital, s.OwnerContributionsMro,
            s.CapitalUsed, s.NetResult, s.NetResultPercent, s.SavedProfits, s.SavedBy, s.SavedAt);
}

public sealed record CapitalChangeView(Guid Id, decimal Amount, string? Reason, Guid SetBy, DateTimeOffset SetAt);

/// <summary>Capital initial actuel et toutes ses saisies.</summary>
public sealed record CapitalView(decimal? Current, IReadOnlyList<CapitalChangeView> History);

/// <summary>Bilan en direct (balance.view). <paramref name="Rate"/> : taux du bilan ; prix d'achat du jour par défaut.</summary>
public sealed record GetBalanceQuery(Guid ActorId, decimal? Rate) : IQuery<BalanceView>;

/// <summary>« Enregistrer le bilan » du jour (balance.manage).</summary>
public sealed record SaveBalanceCommand(Guid ActorId, decimal? Rate) : ICommand<BalanceSnapshotView>;

/// <summary>Historique des bilans enregistrés (balance.view), 30 derniers jours par défaut.</summary>
public sealed record ListBalanceSnapshotsQuery(Guid ActorId, DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<BalanceSnapshotView>>;

/// <summary>Saisir le capital initial (balance.manage) ; motif facultatif.</summary>
public sealed record SetInitialCapitalCommand(Guid ActorId, decimal? Amount, string? Reason) : ICommand;

/// <summary>Capital initial actuel et historique de ses saisies (balance.view).</summary>
public sealed record GetCapitalQuery(Guid ActorId) : IQuery<CapitalView>;

/// <summary>
/// Bilan général (règles validées, décisions du 21 octobre 2026) : budget de l'agence en MRO + budget de l'agence en FCFA
/// converti au taux du bilan (prix d'achat du jour par défaut, modifiable) ; capital pris en compte = capital initial
/// + approvisionnements − retraits du patron (comptes « Apports du patron » des deux agences) ; résultat net et pourcentage ;
/// « الأرباح المحفوظة » en information. Photo du jour courant, remplacée le même jour. Superviseur seulement.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit des saisies de capital arrivera avec le module Audit ; chaque saisie garde déjà son auteur.
/// </summary>
public sealed class BalanceHandlers(
    IAgencyDirectory agencies,
    ICashBudgets budgets,
    ILedger ledger,
    IDailyRates dailyRates,
    IProfitHistory profits,
    ICapitalRepository capital,
    IBalanceSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IQueryHandler<GetBalanceQuery, BalanceView>,
      ICommandHandler<SaveBalanceCommand, BalanceSnapshotView>,
      IQueryHandler<ListBalanceSnapshotsQuery, IReadOnlyList<BalanceSnapshotView>>,
      ICommandHandler<SetInitialCapitalCommand>,
      IQueryHandler<GetCapitalQuery, CapitalView>
{
    public const int DefaultHistoryDays = 30;

    public async Task<Result<BalanceView>> Handle(GetBalanceQuery query, CancellationToken cancellationToken) =>
        await ComputeAsync(query.Rate, cancellationToken);

    public async Task<Result<BalanceSnapshotView>> Handle(SaveBalanceCommand command, CancellationToken cancellationToken)
    {
        var view = await ComputeAsync(command.Rate, cancellationToken);
        if (view.IsFailure)
        {
            return view.Error;
        }

        var figures = Figures(view.Value);
        var now = clock.UtcNow;
        var snapshot = await snapshots.GetAsync(view.Value.Date, cancellationToken);
        if (snapshot is null)
        {
            snapshot = BalanceSnapshot.Create(view.Value.Date, figures, view.Value.SavedProfits, command.ActorId, now);
            snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Replace(figures, view.Value.SavedProfits, command.ActorId, now);
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

        var found = await snapshots.ListAsync(from, to, cancellationToken);
        return found.Select(BalanceSnapshotView.From).ToList();
    }

    public async Task<Result> Handle(SetInitialCapitalCommand command, CancellationToken cancellationToken)
    {
        var change = CapitalChange.Create(command.Amount, command.Reason, command.ActorId, clock.UtcNow);
        if (change.IsFailure)
        {
            return change.Error;
        }

        capital.Add(change.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CapitalView>> Handle(GetCapitalQuery query, CancellationToken cancellationToken)
    {
        var history = await capital.ListAsync(cancellationToken);
        return new CapitalView(
            history.FirstOrDefault()?.Amount,
            [.. history.Select(c => new CapitalChangeView(c.Id, c.Amount, c.Reason, c.SetBy, c.SetAt))]);
    }

    private static BalanceFigures Figures(BalanceView v) =>
        new(v.MroBudget, v.XofBudget, v.XofBudgetInMro, v.GeneralBalance, v.InitialCapital, v.OwnerContributionsMro,
            v.CapitalUsed, v.NetResult, v.NetResultPercent, v.Rate);

    private async Task<Result<BalanceView>> ComputeAsync(decimal? requestedRate, CancellationToken cancellationToken)
    {
        var today = Today();

        ExchangeRate rate;
        if (requestedRate is { } value)
        {
            var parsed = ExchangeRate.Create(value);
            if (parsed.IsFailure)
            {
                return parsed.Error;
            }

            rate = parsed.Value;
        }
        else if (await dailyRates.GetAsync(today, cancellationToken) is { } daily)
        {
            rate = daily.PurchaseRate;
        }
        else
        {
            return BalanceErrors.Rate.Required;
        }

        var all = await agencies.GetAllAsync(cancellationToken);
        var mroAgency = all.SingleOrDefault(a => a.Currency == Currency.MRO);
        var xofAgency = all.SingleOrDefault(a => a.Currency == Currency.XOF);
        if (mroAgency is null || xofAgency is null)
        {
            return BalanceErrors.Agencies.Missing;
        }

        var mroBudget = await budgets.GetBudgetAsync(mroAgency.Id, cancellationToken);
        var xofBudget = await budgets.GetBudgetAsync(xofAgency.Id, cancellationToken);
        if (mroBudget is null || xofBudget is null)
        {
            return BalanceErrors.Agencies.Missing;
        }

        var current = await capital.GetCurrentAsync(cancellationToken);
        var figures = BalanceFigures.Compute(new BalanceInputs(
            mroBudget.Budget,
            xofBudget.Budget,
            rate,
            current?.Amount,
            await OwnerContributionsAsync(mroAgency.Id, cancellationToken),
            await OwnerContributionsAsync(xofAgency.Id, cancellationToken)));

        var savedProfits = await profits.SumSavedProfitsAsync(cancellationToken);
        return new BalanceView(
            today, figures.Rate, mroAgency.Id, figures.MroBudget, xofAgency.Id, figures.XofBudget, figures.XofBudgetInMro,
            figures.GeneralBalance, figures.InitialCapital, figures.OwnerContributionsMro, figures.CapitalUsed,
            figures.NetResult, figures.NetResultPercent, savedProfits);
    }

    /// <summary>
    /// Approvisionnements − retraits du patron dans une agence : « Apports du patron » est crédité à chaque approvisionnement,
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
