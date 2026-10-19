using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Money;

/// <summary>
/// Erreurs de saisie d'un montant ou d'un taux. Codes stables, traduits par le frontend.
/// Règle validée : un montant ou un taux trop précis est refusé, jamais arrondi en silence.
/// </summary>
public static class MoneyErrors
{
    public static class Amount
    {
        public static readonly Error XofNotInteger =
            Error.Validation("Money.Amount.XofNotInteger", "Le montant en FCFA doit être un nombre entier.");

        public static readonly Error TooManyDecimals =
            Error.Validation("Money.Amount.TooManyDecimals", "Le montant en MRO a au plus 2 décimales.");
    }

    public static class Mru
    {
        public static readonly Error TooManyDecimals =
            Error.Validation("Money.Mru.TooManyDecimals", "Le montant en MRU a au plus 3 décimales (12,345 MRU = 123,45 MRO).");
    }

    public static class Rate
    {
        public static readonly Error Invalid =
            Error.Validation(
                "Money.Rate.Invalid",
                "Le taux (MRO pour 5 000 FCFA) doit être positif, avec au plus 2 décimales (ex. 3604,77).");
    }
}
