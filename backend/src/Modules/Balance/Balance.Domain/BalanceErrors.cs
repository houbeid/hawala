using Hawala.SharedKernel.Results;

namespace Balance.Domain;

/// <summary>Erreurs métier du Bilan. Codes stables : ne jamais renommer un code publié.</summary>
public static class BalanceErrors
{
    public static class Capital
    {
        public static readonly Error AmountInvalid =
            Error.Validation("Balance.Capital.AmountInvalid", "Le capital initial d'une caisse est positif, dans sa devise : MRO à 2 décimales au plus, FCFA entier.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Balance.Capital.ReasonTooLong", "Motif : 500 caractères au plus.");
    }

    public static class GeneralCapital
    {
        public static readonly Error AmountInvalid =
            Error.Validation("Balance.GeneralCapital.AmountInvalid", "Le capital général est positif, en MRO, à 2 décimales au plus.");
    }

    public static class Rate
    {
        public static readonly Error Required =
            Error.Validation("Balance.Rate.Required", "Indiquer le taux du bilan (MRO pour 5 000 FCFA).");
    }

    public static class General
    {
        public static readonly Error CaissesMissing =
            Error.Validation("Balance.General.CaissesMissing", "Le bilan général demande une caisse en MRO et une caisse en FCFA.");
    }

    public static class Agency
    {
        public static readonly Error Required =
            Error.Validation("Balance.Agency.Required", "Choisir la caisse (l'agence).");

        public static readonly Error NotFound =
            Error.Validation("Balance.Agency.NotFound", "Caisse (agence) inconnue.");
    }

    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Balance.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");
    }
}
