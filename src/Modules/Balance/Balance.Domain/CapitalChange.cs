using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Balance.Domain;

/// <summary>
/// Capital initial (validé le 21 octobre 2026) : saisi et modifiable à tout moment par le Superviseur ; chaque saisie est
/// gardée (qui, quand, motif facultatif), la plus récente fait foi. Chaque photo du bilan garde le capital utilisé.
/// </summary>
public sealed class CapitalChange
{
    public const int ReasonMaxLength = 500;

    // Constructeur réservé à EF Core.
    private CapitalChange()
    {
    }

    private CapitalChange(decimal amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        Amount = amount;
        Reason = reason;
        SetBy = setBy;
        SetAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Capital initial en MRO.</summary>
    public decimal Amount { get; private set; }

    public string? Reason { get; private set; }

    public Guid SetBy { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    public static Result<CapitalChange> Create(decimal? amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        if (amount is not { } value || value <= 0m || Money.Create(value, Currency.MRO).IsFailure)
        {
            return BalanceErrors.Capital.AmountInvalid;
        }

        if ((reason?.Trim().Length ?? 0) > ReasonMaxLength)
        {
            return BalanceErrors.Capital.ReasonTooLong;
        }

        return new CapitalChange(value, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), setBy, now);
    }
}
