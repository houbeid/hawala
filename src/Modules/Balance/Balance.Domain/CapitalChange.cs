using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Balance.Domain;

/// <summary>
/// Capital initial d'une caisse (décisions du 21 octobre 2026 : deux caisses, deux capitaux) : Nouakchott en MRO, Dakar en FCFA ;
/// saisi et modifiable à tout moment par le Superviseur ; chaque saisie est gardée (qui, quand, motif facultatif),
/// la plus récente de la caisse fait foi. Chaque photo du bilan garde le capital utilisé.
/// </summary>
public sealed class CapitalChange
{
    public const int ReasonMaxLength = 500;

    // Constructeur réservé à EF Core.
    private CapitalChange()
    {
    }

    private CapitalChange(Guid agencyId, Money amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        Id = IdGenerator.NewId(now);
        AgencyId = agencyId;
        Amount = amount.Amount;
        Currency = amount.Currency;
        Reason = reason;
        SetBy = setBy;
        SetAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>La caisse (l'agence) dont c'est le capital.</summary>
    public Guid AgencyId { get; private set; }

    /// <summary>Capital initial, dans la devise de la caisse.</summary>
    public decimal Amount { get; private set; }

    public Currency Currency { get; private set; }

    public string? Reason { get; private set; }

    public Guid SetBy { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    /// <summary>Montant positif dans la devise de la caisse (FCFA entier, MRO à 2 décimales au plus).</summary>
    public static Result<CapitalChange> Create(
        Guid agencyId, Currency currency, decimal? amount, string? reason, Guid setBy, DateTimeOffset now)
    {
        if (amount is not { } value || value <= 0m || Money.Create(value, currency) is not { IsSuccess: true } money)
        {
            return BalanceErrors.Capital.AmountInvalid;
        }

        if ((reason?.Trim().Length ?? 0) > ReasonMaxLength)
        {
            return BalanceErrors.Capital.ReasonTooLong;
        }

        return new CapitalChange(agencyId, money.Value, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), setBy, now);
    }
}
