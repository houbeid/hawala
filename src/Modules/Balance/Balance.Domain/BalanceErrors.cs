using Hawala.SharedKernel.Results;

namespace Balance.Domain;

/// <summary>Erreurs métier du Bilan. Codes stables : ne jamais renommer un code publié.</summary>
public static class BalanceErrors
{
    public static class Capital
    {
        public static readonly Error AmountInvalid =
            Error.Validation("Balance.Capital.AmountInvalid", "Le capital initial est un montant en MRO, positif, à 2 décimales au plus.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Balance.Capital.ReasonTooLong", "Motif : 500 caractères au plus.");
    }

    public static class Rate
    {
        public static readonly Error Required =
            Error.Conflict("Balance.Rate.Required", "Pas de prix d'achat fixé aujourd'hui : saisir le taux du bilan.");
    }

    public static class Agencies
    {
        public static readonly Error Missing =
            Error.Failure("Balance.Agencies.Missing", "Il faut une agence en MRO et une agence en FCFA pour le bilan.");
    }

    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Balance.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");
    }
}
