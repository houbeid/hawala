using Hawala.SharedKernel.Results;

namespace Rates.Domain;

/// <summary>Erreurs métier des taux. Codes stables : ne jamais renommer un code publié.</summary>
public static class RatesErrors
{
    public static class Rate
    {
        public static readonly Error NotFound =
            Error.NotFound("Rates.Rate.NotFound", "Taux du jour non fixé pour cette date.");

        public static readonly Error PurchaseInvalid =
            Error.Validation(
                "Rates.Rate.PurchaseInvalid",
                "Le prix d'achat (MRO pour 5 000 FCFA) doit être positif, avec au plus 2 décimales (ex. 3600 ou 3604,77).");

        public static readonly Error SaleInvalid =
            Error.Validation(
                "Rates.Rate.SaleInvalid",
                "Le prix de vente (MRO pour 5 000 FCFA) doit être positif, avec au plus 2 décimales (ex. 3650).");

        public static readonly Error AuthorRequired =
            Error.Validation("Rates.Rate.AuthorRequired", "L'auteur du taux est obligatoire.");
    }
}
