using Hawala.SharedKernel.Results;

namespace Cash.Domain;

/// <summary>Erreurs métier de la Caisse. Codes stables : ne jamais renommer un code publié.</summary>
public static class CashErrors
{
    public static class Movement
    {
        public static readonly Error NotFound =
            Error.NotFound("Cash.Movement.NotFound", "Mouvement de caisse introuvable.");

        public static readonly Error TypeInvalid =
            Error.Validation(
                "Cash.Movement.TypeInvalid",
                "Type inconnu : Receipt (encaissement), Payment (décaissement), OwnerContribution (approvisionnement) ou OwnerWithdrawal (retrait du patron).");

        public static readonly Error AmountInvalid =
            Error.Validation("Cash.Movement.AmountInvalid", "Le montant est obligatoire et strictement positif.");

        public static readonly Error UnitInvalid =
            Error.Validation("Cash.Movement.UnitInvalid", "Unité inconnue : MRO ou MRU (MRU seulement pour une agence en ouguiya).");

        public static readonly Error PartyRequired =
            Error.Validation("Cash.Movement.PartyRequired", "Un encaissement ou un décaissement se fait avec un tiers : le choisir dans la liste.");

        public static readonly Error PartyNotAllowed =
            Error.Validation(
                "Cash.Movement.PartyNotAllowed",
                "Approvisionnement de caisse et retrait du patron : montant seul, sans tiers.");

        public static readonly Error PartyNotFound =
            Error.Validation("Cash.Movement.PartyNotFound", "Tiers introuvable.");

        public static readonly Error PartyArchived =
            Error.Validation("Cash.Movement.PartyArchived", "Ce tiers est archivé : plus aucune opération possible.");

        public static readonly Error AccountNotAllowed =
            Error.Validation("Cash.Movement.AccountNotAllowed", "Méthode de paiement inconnue, désactivée ou d'une autre agence.");

        public static readonly Error AgencyNotAllowed =
            Error.Forbidden("Cash.Movement.AgencyNotAllowed", "Un agent ne travaille que sur la caisse de son agence.");

        public static readonly Error AgencyRequired =
            Error.Validation("Cash.Movement.AgencyRequired", "Choisir l'agence.");

        public static readonly Error FutureDate =
            Error.Validation("Cash.Movement.FutureDate", "La date de l'opération ne peut pas être dans le futur.");

        public static readonly Error NoteTooLong =
            Error.Validation("Cash.Movement.NoteTooLong", "Note : 500 caractères au plus.");

        public static readonly Error IdempotencyKeyRequired =
            Error.Validation(
                "Cash.Movement.IdempotencyKeyRequired",
                "En-tête Idempotency-Key obligatoire (100 caractères au plus) : il évite d'enregistrer deux fois le même mouvement.");
    }

    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Cash.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");

        public static readonly Error PageInvalid =
            Error.Validation("Cash.Filter.PageInvalid", "Le numéro de page commence à 1.");
    }
}
