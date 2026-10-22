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
                "Type inconnu : Receipt (encaissement), Payment (décaissement), OwnerContribution (approvisionnement), OwnerWithdrawal (retrait du patron) ou InternalTransfer (virement interne).");

        public static readonly Error AmountInvalid =
            Error.Validation("Cash.Movement.AmountInvalid", "Le montant est obligatoire et strictement positif.");

        public static readonly Error UnitInvalid =
            Error.Validation("Cash.Movement.UnitInvalid", "Unité inconnue : MRO ou MRU (MRU seulement pour une agence en ouguiya).");

        public static readonly Error PartyRequired =
            Error.Validation("Cash.Movement.PartyRequired", "Un encaissement ou un décaissement se fait avec un tiers : le choisir dans la liste.");

        public static readonly Error PartyNotAllowed =
            Error.Validation(
                "Cash.Movement.PartyNotAllowed",
                "Approvisionnement de caisse, retrait du patron et virement interne : sans tiers.");

        public static readonly Error PartyNotFound =
            Error.Validation("Cash.Movement.PartyNotFound", "Tiers introuvable.");

        public static readonly Error PartyArchived =
            Error.Validation("Cash.Movement.PartyArchived", "Ce tiers est archivé : plus aucune opération possible.");

        public static readonly Error DestinationRequired =
            Error.Validation("Cash.Movement.DestinationRequired", "Virement interne : choisir le compte de destination.");

        public static readonly Error DestinationNotAllowed =
            Error.Validation("Cash.Movement.DestinationNotAllowed", "Seul un virement interne a un compte de destination.");

        public static readonly Error SameAccount =
            Error.Validation("Cash.Movement.SameAccount", "Virement interne : le compte de destination doit être différent du compte d'origine.");

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

        public static readonly Error AlreadyCancelled =
            Error.Conflict("Cash.Movement.AlreadyCancelled", "Ce mouvement est déjà annulé.");

        public static readonly Error ReasonRequired =
            Error.Validation("Cash.Movement.ReasonRequired", "Le motif de l'annulation est obligatoire.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Cash.Movement.ReasonTooLong", "Motif : 500 caractères au plus.");

        public static readonly Error IdempotencyKeyRequired =
            Error.Validation(
                "Cash.Movement.IdempotencyKeyRequired",
                "En-tête Idempotency-Key obligatoire (100 caractères au plus) : il évite d'enregistrer deux fois le même mouvement.");
    }

    public static class Adjustment
    {
        public static readonly Error TargetInvalid =
            Error.Validation("Cash.Adjustment.TargetInvalid", "Cible inconnue : TreasuryAccount ou Party.");

        public static readonly Error TargetNotFound =
            Error.Validation(
                "Cash.Adjustment.TargetNotFound", "Compte de trésorerie ou tiers introuvable, désactivé, archivé ou d'une autre agence.");

        public static readonly Error AmountInvalid =
            Error.Validation(
                "Cash.Adjustment.AmountInvalid",
                "Montant de l'ajustement : non nul, positif pour augmenter le solde, négatif pour le baisser (FCFA entier, MRO à 2 décimales).");

        public static readonly Error ReasonRequired =
            Error.Validation("Cash.Adjustment.ReasonRequired", "Le motif de l'ajustement est obligatoire.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Cash.Adjustment.ReasonTooLong", "Motif : 500 caractères au plus.");
    }

    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Cash.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");

        public static readonly Error PageInvalid =
            Error.Validation("Cash.Filter.PageInvalid", "Le numéro de page commence à 1.");
    }
}
