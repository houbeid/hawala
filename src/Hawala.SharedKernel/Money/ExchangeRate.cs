using System.Globalization;
using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Money;

/// <summary>
/// Taux au format de l'app actuelle : « 5 000 FCFA = ? MRO » (ex. 3604,77).
/// Sert pour le prix de vente comme pour le prix d'achat ; c'est le module Taux qui dit lequel.
/// Règle validée : au plus 2 décimales ; un taux plus précis est refusé, jamais arrondi.
/// </summary>
public sealed record ExchangeRate
{
    public const int MaxDecimals = 2;

    /// <summary>Le taux est exprimé pour 5 000 FCFA.</summary>
    public const decimal XofUnit = 5000m;

    private ExchangeRate(decimal mroPer5000Xof) => MroPer5000Xof = mroPer5000Xof;

    public decimal MroPer5000Xof { get; }

    public static Result<ExchangeRate> Create(decimal mroPer5000Xof) =>
        mroPer5000Xof <= 0m || decimal.Round(mroPer5000Xof, MaxDecimals) != mroPer5000Xof
            ? MoneyErrors.Rate.Invalid
            : new ExchangeRate(mroPer5000Xof);

    /// <summary>
    /// Montant MRO d'un transfert : FCFA × taux / 5000, arrondi UNE SEULE FOIS à l'unité,
    /// 0,5 vers le haut (MidpointRounding.AwayFromZero, jamais l'arrondi « vers le pair » par défaut de .NET).
    /// Le résultat est enregistré avec le transfert et jamais recalculé (règles d'arrondi 2 et 3).
    /// </summary>
    public Money ToTransferMro(Money xof)
    {
        ArgumentNullException.ThrowIfNull(xof);
        if (xof.Currency != Currency.XOF)
        {
            throw new ArgumentException($"Le montant à convertir doit être en FCFA, pas en {xof.Currency}.", nameof(xof));
        }

        var mro = decimal.Round(xof.Amount * MroPer5000Xof / XofUnit, 0, MidpointRounding.AwayFromZero);
        return Money.FromValidated(mro, Currency.MRO);
    }

    public override string ToString() => MroPer5000Xof.ToString(CultureInfo.InvariantCulture);
}
