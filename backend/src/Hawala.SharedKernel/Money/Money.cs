using System.Globalization;
using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Money;

/// <summary>
/// Un montant et sa devise. Jamais de float / double : decimal partout, numeric(18,2) en base.
/// Règles validées :
/// - le FCFA (XOF) est toujours un nombre entier ;
/// - le MRO a au plus 2 décimales ; la saisie en MRU (au plus 3 décimales) est convertie × 10 exact ;
/// - un montant trop précis est refusé, jamais arrondi.
/// Un montant peut être négatif : un solde peut l'être (caisse à découvert autorisée).
/// Refuser un montant nul ou négatif pour une opération est le travail de chaque module.
/// </summary>
public sealed record Money
{
    public const int MroMaxDecimals = 2;
    public const int MruMaxDecimals = 3;
    private const decimal MroPerMru = 10m;

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public Currency Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money Zero(Currency currency) => new(0m, currency);

    public static Result<Money> Create(decimal amount, Currency currency)
    {
        return currency switch
        {
            Currency.XOF when decimal.Truncate(amount) != amount => MoneyErrors.Amount.XofNotInteger,
            Currency.MRO when HasMoreDecimalsThan(amount, MroMaxDecimals) => MoneyErrors.Amount.TooManyDecimals,
            _ => new Money(amount, currency),
        };
    }

    /// <summary>
    /// Montant lu en MRU (banques, Bankily…) : converti en MRO × 10, exact.
    /// Au plus 3 décimales MRU, pour tenir dans les 2 décimales du MRO.
    /// </summary>
    public static Result<Money> FromMru(decimal mru) =>
        HasMoreDecimalsThan(mru, MruMaxDecimals)
            ? MoneyErrors.Mru.TooManyDecimals
            : new Money(mru * MroPerMru, Currency.MRO);

    /// <summary>
    /// Pour un calcul déjà conforme aux règles (ex. conversion d'un transfert, arrondie à l'unité).
    /// Réservé au SharedKernel : tout montant venu de l'extérieur passe par <see cref="Create"/>.
    /// </summary>
    internal static Money FromValidated(decimal amount, Currency currency) => new(amount, currency);

    /// <summary>Addition dans la même devise. MRO + XOF est un bug de programmation, jamais une saisie : exception.</summary>
    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    public Money Negate() => new(-Amount, Currency);

    public static Money operator +(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Add(right);
    }

    public static Money operator -(Money left, Money right)
    {
        ArgumentNullException.ThrowIfNull(left);
        return left.Subtract(right);
    }

    public static Money operator -(Money money)
    {
        ArgumentNullException.ThrowIfNull(money);
        return money.Negate();
    }

    public override string ToString() => $"{Amount.ToString(CultureInfo.InvariantCulture)} {Currency}";

    private static bool HasMoreDecimalsThan(decimal value, int decimals) => decimal.Round(value, decimals) != value;

    private void EnsureSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException(
                $"Opération entre deux devises ({Currency} et {other.Currency}) : passer par la Position de change.");
        }
    }
}
