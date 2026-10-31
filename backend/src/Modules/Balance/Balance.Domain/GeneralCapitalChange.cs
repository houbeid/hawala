using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Balance.Domain;

/// <summary>
/// Capital général du bilan général (décision du 31 octobre 2026) : saisi à part, en MRO, fixe (il ne bouge pas avec le taux),
/// par le Superviseur ; chaque saisie est gardée (qui, quand, motif facultatif), la plus récente fait foi. Les capitaux par caisse
/// (<see cref="CapitalChange"/>) restent pour le bilan par caisse.
/// </summary>
public sealed class GeneralCapitalChange
{
    // Constructeur réservé à EF Core.
    private GeneralCapitalChange()
    {
    }

    private GeneralCapitalChange(Money amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Amount = amount.Amount;
        Reason = reason;
        SetBy = setBy;
        SetAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Capital général, en MRO.</summary>
    public decimal Amount { get; private set; }

    public string? Reason { get; private set; }

    public Guid SetBy { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    /// <summary>Montant positif en MRO, 2 décimales au plus.</summary>
    public static Result<GeneralCapitalChange> Create(decimal? amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        if (amount is not { } value || value <= 0m || Money.Create(value, Currency.MRO) is not { IsSuccess: true } money)
        {
            return BalanceErrors.GeneralCapital.AmountInvalid;
        }

        if ((reason?.Trim().Length ?? 0) > CapitalChange.ReasonMaxLength)
        {
            return BalanceErrors.Capital.ReasonTooLong;
        }

        return new GeneralCapitalChange(money.Value, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), setBy, now);
    }
}
