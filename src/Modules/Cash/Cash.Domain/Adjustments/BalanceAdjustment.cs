using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Cash.Domain.Adjustments;

/// <summary>Ce que l'on corrige : le solde d'un compte de trésorerie ou celui d'un tiers dans une agence.</summary>
public enum AdjustmentTarget
{
    TreasuryAccount,
    Party,
}

/// <summary>
/// Ajustement de solde par le Superviseur (règle validée) : motif obligatoire, écriture contre « Écarts et ajustements ».
/// Montant signé, même convention que le solde initial : positif = le solde monte (caisse : plus d'argent ;
/// tiers : plus en rouge), négatif = il baisse. Personne n'écrit un solde à la place du calcul : on passe la différence.
/// </summary>
public sealed class BalanceAdjustment
{
    public const int ReasonMaxLength = 500;

    // Constructeur réservé à EF Core.
    private BalanceAdjustment()
    {
        Reason = null!;
    }

    private BalanceAdjustment(
        Guid agencyId, AdjustmentTarget target, Guid targetId, decimal amount, Currency currency,
        string reason, DateTimeOffset operationAt, Guid createdBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        Target = target;
        TargetId = targetId;
        Amount = amount;
        Currency = currency;
        Reason = reason;
        OperationAt = operationAt;
        CreatedBy = createdBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid AgencyId { get; private set; }

    public AdjustmentTarget Target { get; private set; }

    /// <summary>Id du compte de trésorerie ou du tiers.</summary>
    public Guid TargetId { get; private set; }

    /// <summary>Différence signée : positive = le solde monte.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public string Reason { get; private set; }

    public DateTimeOffset OperationAt { get; private set; }

    public Guid EntryId { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>La différence en valeur absolue, dans la devise de l'agence.</summary>
    public Money AbsoluteMoney => Money.Create(Math.Abs(Amount), Currency).Value;

    public static Result<BalanceAdjustment> Create(
        Guid agencyId,
        AdjustmentTarget target,
        Guid targetId,
        decimal amount,
        Currency currency,
        string? reason,
        DateTimeOffset operationAt,
        Guid createdBy,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(target))
        {
            return CashErrors.Adjustment.TargetInvalid;
        }

        // Zéro n'ajuste rien ; trop de décimales (FCFA entier, MRO à 2) : refusé, jamais arrondi.
        if (amount == 0m || Money.Create(Math.Abs(amount), currency).IsFailure)
        {
            return CashErrors.Adjustment.AmountInvalid;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return CashErrors.Adjustment.ReasonRequired;
        }

        if (reason.Trim().Length > ReasonMaxLength)
        {
            return CashErrors.Adjustment.ReasonTooLong;
        }

        if (operationAt > now)
        {
            return CashErrors.Movement.FutureDate;
        }

        return new BalanceAdjustment(agencyId, target, targetId, amount, currency, reason.Trim(), operationAt, createdBy, now);
    }

    /// <summary>Rattache l'écriture du grand livre, une seule fois.</summary>
    public void AttachEntry(Guid entryId)
    {
        if (entryId == Guid.Empty)
        {
            throw new ArgumentException("Id d'écriture vide.", nameof(entryId));
        }

        if (EntryId != Guid.Empty)
        {
            throw new InvalidOperationException("Cet ajustement a déjà son écriture.");
        }

        EntryId = entryId;
    }
}
