using Hawala.SharedKernel.Results;

namespace Profit.Domain;

/// <summary>Erreurs métier du Bénéfice. Codes stables : ne jamais renommer un code publié.</summary>
public static class ProfitErrors
{
    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Profit.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");

        public static readonly Error AgencyNotFound =
            Error.Validation("Profit.Filter.AgencyNotFound", "Agence inconnue.");
    }
}
