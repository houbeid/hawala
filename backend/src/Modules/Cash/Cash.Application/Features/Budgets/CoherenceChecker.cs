using Cash.Application.Abstractions;
using Cash.Domain.Budgets;
using Cash.Domain.Movements;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Organization.Contracts;
using Transfers.Contracts;

namespace Cash.Application.Features.Budgets;

/// <summary>Résultat du contrôle de cohérence d'une agence.</summary>
/// <param name="Since">Début de la période analysée : le dernier budget enregistré (ou minuit aujourd'hui s'il n'y en a pas).</param>
public sealed record CoherenceCheckView(Guid AgencyId, DateTimeOffset Since, IReadOnlyList<CoherenceAlert> Alerts);

/// <summary>
/// Contrôle de cohérence (règles validées le 21 octobre 2026) : analyse ce qui a été ENREGISTRÉ depuis le dernier budget
/// (une opération antidatée saisie après compte) et liste les alertes à vérifier, sans blocage :
/// taux inhabituel, ajustements, soldes initiaux changés, mouvements et transferts annulés, livraisons sans code,
/// comptes de trésorerie en négatif (solde actuel).
/// POINT OUVERT : autres alertes à ajouter après avis du patron.
/// </summary>
public sealed class CoherenceChecker(
    ICashMovementRepository movements,
    IBalanceAdjustmentRepository adjustments,
    ICashBudgetSnapshotRepository snapshots,
    ITransferMonitor transferMonitor,
    ILedger ledger,
    IClock clock)
{
    /// <summary>Type d'écriture du solde initial d'un tiers (module Tiers).</summary>
    public const string OpeningBalanceEntryType = "Parties.OpeningBalance";

    /// <summary>Début par défaut : l'heure d'enregistrement du dernier budget d'un jour passé, sinon minuit aujourd'hui (UTC).</summary>
    public async Task<DateTimeOffset> DefaultSinceAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var last = await snapshots.GetLastBeforeAsync(agencyId, today, cancellationToken);
        return last?.SavedAt ?? new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    public async Task<IReadOnlyList<CoherenceAlert>> CheckAsync(
        AgencySummary agency, CashBudgetView budget, DateTimeOffset since, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agency);
        ArgumentNullException.ThrowIfNull(budget);

        var alerts = new List<CoherenceAlert>();

        foreach (var transfer in await transferMonitor.ListAlertsAsync(agency.Id, since, cancellationToken))
        {
            alerts.Add(transfer.Kind switch
            {
                TransferAlertKind.UnusualRate => new CoherenceAlert(
                    CoherenceAlertKind.UnusualRate, transfer.At, transfer.TransferId,
                    $"{transfer.Reference} : {transfer.SaleRate} pour {transfer.DailySaleRate} le jour même", transfer.SaleRate, null),
                TransferAlertKind.Cancelled => new CoherenceAlert(
                    CoherenceAlertKind.TransferCancelled, transfer.At, transfer.TransferId, transfer.Reference, null, transfer.Reason),
                _ => new CoherenceAlert(
                    CoherenceAlertKind.DeliveredWithoutCode, transfer.At, transfer.TransferId, transfer.Reference, null, transfer.Reason),
            });
        }

        foreach (var adjustment in await adjustments.ListRecordedSinceAsync(agency.Id, since, cancellationToken))
        {
            alerts.Add(new CoherenceAlert(
                CoherenceAlertKind.BalanceAdjustment, adjustment.CreatedAt, adjustment.Id,
                $"Ajustement ({adjustment.Target})", adjustment.Amount, adjustment.Reason));
        }

        foreach (var entry in await ledger.ListRecordedEntriesAsync(OpeningBalanceEntryType, agency.Id, since, cancellationToken))
        {
            alerts.Add(new CoherenceAlert(
                CoherenceAlertKind.OpeningBalanceChanged, entry.RecordedAt, entry.SourceId, entry.Label, entry.Amount, null));
        }

        foreach (var movement in await movements.ListCancelledSinceAsync(agency.Id, since, cancellationToken))
        {
            alerts.Add(new CoherenceAlert(
                CoherenceAlertKind.CashMovementCancelled, movement.CancelledAt!.Value, movement.Id,
                Label(movement.Type), movement.Amount, movement.CancellationReason));
        }

        var now = clock.UtcNow;
        foreach (var account in budget.Cash.Items.Concat(budget.Banks.Items).Where(i => i.Balance < 0m))
        {
            alerts.Add(new CoherenceAlert(CoherenceAlertKind.NegativeBalance, now, account.Id, account.Name, account.Balance, null));
        }

        return [.. alerts.OrderBy(a => a.At)];
    }

    private static string Label(CashMovementType type) => type switch
    {
        CashMovementType.Receipt => "Encaissement",
        CashMovementType.Payment => "Décaissement",
        CashMovementType.OwnerContribution => "Approvisionnement de caisse",
        CashMovementType.OwnerWithdrawal => "Retrait du patron",
        _ => "Virement interne",
    };
}

/// <summary>Contrôle de cohérence avant l'enregistrement du budget (cash.budget.read).</summary>
/// <param name="Since">Début de la période ; le dernier budget enregistré par défaut.</param>
public sealed record GetCoherenceCheckQuery(Guid ActorId, Guid? AgencyId, DateTimeOffset? Since) : IQuery<CoherenceCheckView>;

public sealed class GetCoherenceCheckQueryHandler(CashBudgetCalculator calculator, CoherenceChecker checker)
    : IQueryHandler<GetCoherenceCheckQuery, CoherenceCheckView>
{
    public async Task<Result<CoherenceCheckView>> Handle(GetCoherenceCheckQuery query, CancellationToken cancellationToken)
    {
        var agency = await calculator.ResolveAgencyAsync(query.AgencyId, cancellationToken);
        if (agency.IsFailure)
        {
            return agency.Error;
        }

        var budget = await calculator.ComputeAsync(agency.Value, cancellationToken);
        if (budget.IsFailure)
        {
            return budget.Error;
        }

        var since = query.Since ?? await checker.DefaultSinceAsync(agency.Value.Id, cancellationToken);
        var alerts = await checker.CheckAsync(agency.Value, budget.Value, since, cancellationToken);
        return new CoherenceCheckView(agency.Value.Id, since, alerts);
    }
}
