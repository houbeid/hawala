using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Profit.Application.Abstractions;
using Profit.Domain;
using Profit.Domain.Snapshots;
using Transfers.Contracts;

namespace Profit.Application.Features;

/// <summary>Le bénéfice d'un jour : somme des transferts non annulés de ce jour (date d'opération).</summary>
/// <param name="AmountXof">Montant envoyé du jour, en FCFA.</param>
/// <param name="PurchaseRate">Prix d'achat du jour utilisé ; null si les transferts du jour en ont plusieurs.</param>
/// <param name="ProfitMro">Bénéfice du jour en pleine précision (seul l'affichage arrondit, règle d'arrondi 4).</param>
/// <param name="SavedProfitMro">Bénéfice de la photo enregistrée ce jour-là (colonne « Enregistré ») ; null sinon.</param>
/// <param name="SavedAt">Heure de la photo enregistrée ; null sinon.</param>
public sealed record ProfitDay(
    DateOnly Date,
    int TransferCount,
    decimal AmountXof,
    decimal? PurchaseRate,
    decimal ProfitMro,
    decimal? SavedProfitMro,
    DateTimeOffset? SavedAt);

/// <summary>
/// Écran « Bénéfice » (proposition validée le 21 octobre 2026) : total de la période et une ligne par jour.
/// </summary>
/// <param name="AgencyId">Agence d'envoi des transferts comptés ; null : les deux (bénéfice général).</param>
public sealed record ProfitReport(
    DateOnly From, DateOnly To, Guid? AgencyId, decimal TotalProfitMro, int TransferCount, IReadOnlyList<ProfitDay> Days);

/// <summary>Le détail d'un jour : une ligne par transfert.</summary>
public sealed record ProfitDayDetail(DateOnly Date, Guid? AgencyId, decimal TotalProfitMro, IReadOnlyList<TransferProfitLine> Transfers);

/// <summary>Bénéfice d'une période (profit.view, Superviseur). Aujourd'hui par défaut.</summary>
public sealed record GetProfitReportQuery(Guid ActorId, Guid? AgencyId, DateOnly? From, DateOnly? To) : IQuery<ProfitReport>;

/// <summary>Détail d'un jour, transfert par transfert.</summary>
public sealed record GetProfitDayQuery(Guid ActorId, Guid? AgencyId, DateOnly Date) : IQuery<ProfitDayDetail>;

/// <summary>Une photo enregistrée du bénéfice du jour.</summary>
public sealed record ProfitSnapshotView(
    Guid Id, Guid? AgencyId, DateOnly Date, int TransferCount, decimal AmountXof, decimal? PurchaseRate, decimal ProfitMro,
    Guid SavedBy, DateTimeOffset SavedAt)
{
    public static ProfitSnapshotView From(ProfitSnapshot s) =>
        new(s.Id, s.AgencyId, s.Date, s.TransferCount, s.AmountXof, s.PurchaseRate, s.ProfitMro, s.SavedBy, s.SavedAt);
}

/// <summary>« Enregistrer le bénéfice du jour » (profit.view, Superviseur) : le jour courant, pour une agence ou les deux.</summary>
public sealed record SaveProfitSnapshotCommand(Guid ActorId, Guid? AgencyId) : ICommand<ProfitSnapshotView>;

/// <summary>Photos enregistrées d'une période (30 derniers jours par défaut).</summary>
public sealed record ListProfitSnapshotsQuery(Guid ActorId, Guid? AgencyId, DateOnly? From, DateOnly? To)
    : IQuery<IReadOnlyList<ProfitSnapshotView>>;

/// <summary>
/// Règles validées le 21 octobre 2026 : bénéfice d'un transfert selon les formules validées (déjà calculé par les Transferts) ;
/// un transfert compte pour l'agence qui l'a envoyé ; transferts annulés exclus ; jour = date d'opération ;
/// photo du jour courant seulement, remplacée si on la réenregistre le même jour.
/// POINT OUVERT : les jours sont lus en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : deux premières photos simultanées du même jour se heurtent à l'index unique (500 au lieu de 409).
/// </summary>
public sealed class ProfitReportHandlers(
    ITransferProfits transfers,
    IAgencyDirectory agencies,
    IProfitSnapshotRepository snapshots,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IQueryHandler<GetProfitReportQuery, ProfitReport>,
      IQueryHandler<GetProfitDayQuery, ProfitDayDetail>,
      ICommandHandler<SaveProfitSnapshotCommand, ProfitSnapshotView>,
      IQueryHandler<ListProfitSnapshotsQuery, IReadOnlyList<ProfitSnapshotView>>
{
    public const int DefaultHistoryDays = 30;

    public async Task<Result<ProfitReport>> Handle(GetProfitReportQuery query, CancellationToken cancellationToken)
    {
        var today = Today();
        var from = query.From ?? today;
        var to = query.To ?? today;
        if (from > to)
        {
            return ProfitErrors.Filter.PeriodInvalid;
        }

        var agencyCheck = await CheckAgencyAsync(query.AgencyId, cancellationToken);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        var lines = await transfers.ListAsync(Start(from), Start(to.AddDays(1)), query.AgencyId, cancellationToken);
        var saved = (await snapshots.ListAsync(query.AgencyId, from, to, cancellationToken)).ToDictionary(s => s.Date);

        var days = lines
            .GroupBy(l => DateOnly.FromDateTime(l.OperationAt.UtcDateTime))
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var figures = Figures(g);
                var photo = saved.GetValueOrDefault(g.Key);
                return new ProfitDay(
                    g.Key, figures.TransferCount, figures.AmountXof, figures.PurchaseRate, figures.ProfitMro,
                    photo?.ProfitMro, photo?.SavedAt);
            })
            .ToList();

        return new ProfitReport(from, to, query.AgencyId, lines.Sum(l => l.ProfitMro), lines.Count, days);
    }

    public async Task<Result<ProfitDayDetail>> Handle(GetProfitDayQuery query, CancellationToken cancellationToken)
    {
        var agencyCheck = await CheckAgencyAsync(query.AgencyId, cancellationToken);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        var lines = await transfers.ListAsync(Start(query.Date), Start(query.Date.AddDays(1)), query.AgencyId, cancellationToken);
        return new ProfitDayDetail(query.Date, query.AgencyId, lines.Sum(l => l.ProfitMro), lines);
    }

    public async Task<Result<ProfitSnapshotView>> Handle(SaveProfitSnapshotCommand command, CancellationToken cancellationToken)
    {
        var agencyCheck = await CheckAgencyAsync(command.AgencyId, cancellationToken);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var lines = await transfers.ListAsync(Start(today), Start(today.AddDays(1)), command.AgencyId, cancellationToken);
        var figures = Figures(lines);

        var snapshot = await snapshots.GetAsync(command.AgencyId, today, cancellationToken);
        if (snapshot is null)
        {
            snapshot = ProfitSnapshot.Create(command.AgencyId, today, figures, command.ActorId, now);
            snapshots.Add(snapshot);
        }
        else
        {
            snapshot.Replace(figures, command.ActorId, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ProfitSnapshotView.From(snapshot);
    }

    public async Task<Result<IReadOnlyList<ProfitSnapshotView>>> Handle(ListProfitSnapshotsQuery query, CancellationToken cancellationToken)
    {
        var to = query.To ?? Today();
        var from = query.From ?? to.AddDays(-(DefaultHistoryDays - 1));
        if (from > to)
        {
            return ProfitErrors.Filter.PeriodInvalid;
        }

        var agencyCheck = await CheckAgencyAsync(query.AgencyId, cancellationToken);
        if (agencyCheck.IsFailure)
        {
            return agencyCheck.Error;
        }

        var found = await snapshots.ListAsync(query.AgencyId, from, to, cancellationToken);
        return found.Select(ProfitSnapshotView.From).ToList();
    }

    private static ProfitFigures Figures(IEnumerable<TransferProfitLine> lines)
    {
        var list = lines.ToList();
        var rates = list.Select(l => l.PurchaseRate).Distinct().ToList();
        return new ProfitFigures(list.Count, list.Sum(l => l.AmountXof), rates.Count == 1 ? rates[0] : null, list.Sum(l => l.ProfitMro));
    }

    private async Task<Result> CheckAgencyAsync(Guid? agencyId, CancellationToken cancellationToken) =>
        agencyId is { } id && await agencies.GetByIdAsync(id, cancellationToken) is null
            ? ProfitErrors.Filter.AgencyNotFound
            : Result.Success();

    private DateOnly Today() => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
