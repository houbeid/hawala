using Audit.Contracts;
using Cash.Application.Abstractions;
using Cash.Domain;
using Cash.Domain.Adjustments;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Contracts;
using Treasury.Contracts;

namespace Cash.Application.Features.Adjustments;

/// <summary>Ajustement de solde par le Superviseur (permission cash.manage).</summary>
/// <param name="Target">TreasuryAccount ou Party.</param>
/// <param name="TargetId">Id du compte de trésorerie (de l'agence) ou du tiers.</param>
/// <param name="Amount">Différence signée : positive = le solde monte, négative = il baisse.</param>
/// <param name="Reason">Motif obligatoire.</param>
/// <param name="OperationAt">Date de l'ajustement ; maintenant par défaut.</param>
public sealed record CreateBalanceAdjustmentCommand(
    Guid ActorId,
    Guid? AgencyId,
    string? Target,
    Guid? TargetId,
    decimal? Amount,
    string? Reason,
    DateTimeOffset? OperationAt) : ICommand<Guid>;

/// <summary>
/// Règle validée (21 octobre 2026) : sur un compte de trésorerie ou sur un tiers, contre « Écarts et ajustements » de l'agence,
/// tracé (motif, auteur). Montant positif : débit de la cible / crédit Écarts ; négatif : l'inverse.
/// Le contrôle de cohérence signalera chaque ajustement depuis le dernier budget enregistré.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// </summary>
public sealed class CreateBalanceAdjustmentCommandHandler(
    IBalanceAdjustmentRepository adjustments,
    IAgencyDirectory agencies,
    ITreasuryDirectory treasury,
    IPartyDirectory parties,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<CreateBalanceAdjustmentCommand, Guid>
{
    public const string EntryType = "Cash.Adjustment";

    public async Task<Result<Guid>> Handle(CreateBalanceAdjustmentCommand command, CancellationToken cancellationToken)
    {
        if (!EnumNames.TryParse<AdjustmentTarget>(command.Target, out var target))
        {
            return CashErrors.Adjustment.TargetInvalid;
        }

        if (command.AgencyId is not { } agencyId || await agencies.GetByIdAsync(agencyId, cancellationToken) is not { } agency)
        {
            return CashErrors.Movement.AgencyRequired;
        }

        var now = clock.UtcNow;
        var created = BalanceAdjustment.Create(
            agency.Id, target, command.TargetId ?? Guid.Empty, command.Amount ?? 0m, agency.Currency,
            command.Reason, command.OperationAt ?? now, command.ActorId, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var adjustment = created.Value;
        var targetAccount = await ResolveTargetAsync(adjustment, cancellationToken);
        if (targetAccount.IsFailure)
        {
            return targetAccount.Error;
        }

        var gaps = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.Adjustments, agency.Id, agency.Currency, cancellationToken);
        if (gaps.IsFailure)
        {
            return gaps.Error;
        }

        var raises = adjustment.Amount > 0m;
        var entry = await ledger.PostAsync(
            new PostEntryRequest(
                DateOnly.FromDateTime(adjustment.OperationAt.UtcDateTime),
                EntryType,
                "Cash",
                adjustment.Id,
                command.ActorId,
                Label(adjustment.Reason),
                [
                    new PostEntryLine(targetAccount.Value, raises ? EntrySide.Debit : EntrySide.Credit, adjustment.AbsoluteMoney),
                    new PostEntryLine(gaps.Value, raises ? EntrySide.Credit : EntrySide.Debit, adjustment.AbsoluteMoney),
                ]),
            cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        adjustment.AttachEntry(entry.Value);
        adjustments.Add(adjustment);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Correction tracée dans l'audit (phase 6) : l'entité est la cible (caisse ou tiers), la différence signée en « amount ».
        var audited = await audit.RecordAsync(
            new AuditRecord(
                "Cash.Adjustment.Created",
                target == AdjustmentTarget.Party ? "Party" : "TreasuryAccount",
                adjustment.TargetId,
                command.ActorId,
                agency.Id,
                adjustment.Reason,
                [new AuditChange("adjustment", null, adjustment.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))]),
            cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return adjustment.Id;
    }

    /// <summary>Libellé lisible dans le relevé, coupé à 200 caractères (le motif complet reste sur l'ajustement).</summary>
    private static string Label(string reason)
    {
        var label = $"Ajustement de solde : {reason}";
        return label.Length <= LabelMaxLength ? label : label[..LabelMaxLength];
    }

    private const int LabelMaxLength = 200;

    /// <summary>Compte au grand livre de la cible : compte de trésorerie actif de l'agence, ou tiers non archivé dans l'agence.</summary>
    private async Task<Result<Guid>> ResolveTargetAsync(BalanceAdjustment adjustment, CancellationToken cancellationToken)
    {
        if (adjustment.Target == AdjustmentTarget.TreasuryAccount)
        {
            var account = (await treasury.GetByIdsAsync([adjustment.TargetId], cancellationToken)).SingleOrDefault();
            return account is { IsActive: true } && account.AgencyId == adjustment.AgencyId
                ? account.LedgerAccountId
                : CashErrors.Adjustment.TargetNotFound;
        }

        var party = await parties.GetAgencyAccountAsync(adjustment.TargetId, adjustment.AgencyId, cancellationToken);
        return party is { IsArchived: false } ? party.LedgerAccountId : CashErrors.Adjustment.TargetNotFound;
    }
}
