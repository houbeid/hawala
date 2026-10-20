using Hawala.SharedKernel.Results;

namespace Transfers.Domain;

/// <summary>Erreurs métier des transferts. Codes stables : ne jamais renommer un code publié.</summary>
public static class TransfersErrors
{
    public static class Transfer
    {
        public static readonly Error NotFound =
            Error.NotFound("Transfers.Transfer.NotFound", "Transfert introuvable.");

        public static readonly Error AmountInvalid =
            Error.Validation("Transfers.Transfer.AmountInvalid", "Le montant en FCFA est obligatoire, entier et positif.");

        public static readonly Error CorridorInvalid =
            Error.Validation(
                "Transfers.Transfer.CorridorInvalid",
                "Un transfert va d'une agence à l'autre : Nouakchott (MRO) vers Dakar (FCFA), ou l'inverse.");

        public static readonly Error SourceNotAllowed =
            Error.Forbidden("Transfers.Transfer.SourceNotAllowed", "Un transfert est créé par l'agence d'envoi : la vôtre.");

        public static readonly Error DestinationNotFound =
            Error.Validation("Transfers.Transfer.DestinationNotFound", "Agence de destination inconnue.");

        public static readonly Error SourceRequired =
            Error.Validation("Transfers.Transfer.SourceRequired", "Choisir l'agence d'envoi.");

        public static readonly Error RateNotSet =
            Error.Conflict("Transfers.Transfer.RateNotSet", "Taux du jour non fixé : le Superviseur doit le fixer avant tout transfert.");

        public static readonly Error FutureDate =
            Error.Validation("Transfers.Transfer.FutureDate", "La date de l'opération ne peut pas être dans le futur.");

        public static readonly Error SenderRequired =
            Error.Validation("Transfers.Transfer.SenderRequired", "Les informations de l'expéditeur sont obligatoires.");

        public static readonly Error BeneficiaryRequired =
            Error.Validation("Transfers.Transfer.BeneficiaryRequired", "Les informations du bénéficiaire sont obligatoires.");

        public static readonly Error TextTooLong =
            Error.Validation("Transfers.Transfer.TextTooLong", "Expéditeur ou bénéficiaire : 200 caractères au plus ; note : 500.");

        public static readonly Error IdempotencyKeyRequired =
            Error.Validation(
                "Transfers.Transfer.IdempotencyKeyRequired",
                "En-tête Idempotency-Key obligatoire (100 caractères au plus) : il évite de créer deux fois le même transfert.");
    }

    public static class Payment
    {
        public static readonly Error Required =
            Error.Validation("Transfers.Payment.Required", "Indiquer au moins une méthode de paiement, ou activer le crédit.");

        public static readonly Error AmountNotPositive =
            Error.Validation("Transfers.Payment.AmountNotPositive", "Chaque montant de la répartition est strictement positif.");

        public static readonly Error CurrencyMismatch =
            Error.Validation("Transfers.Payment.CurrencyMismatch", "La répartition est dans la devise de l'agence d'envoi.");

        public static readonly Error AccountDuplicated =
            Error.Validation("Transfers.Payment.AccountDuplicated", "Une méthode de paiement n'apparaît qu'une fois dans la répartition.");

        public static readonly Error TotalMismatch =
            Error.Validation("Transfers.Payment.TotalMismatch", "Le total réparti doit être égal au montant payé par le client (inférieur avec le crédit activé, jamais supérieur).");

        public static readonly Error AccountNotAllowed =
            Error.Validation(
                "Transfers.Payment.AccountNotAllowed",
                "Méthode de paiement inconnue, désactivée ou d'une autre agence.");
    }

    public static class Credit
    {
        public static readonly Error NotAllowed =
            Error.Forbidden("Transfers.Credit.NotAllowed", "Activer le crédit demande la permission transfers.credit.");

        public static readonly Error PartyNotFound =
            Error.Validation("Transfers.Credit.PartyNotFound", "Tiers du crédit introuvable : le choisir dans la liste ou le créer.");

        public static readonly Error PartyArchived =
            Error.Validation("Transfers.Credit.PartyArchived", "Ce tiers est archivé : il ne peut pas recevoir de crédit.");

        public static readonly Error NotNeeded =
            Error.Validation("Transfers.Credit.NotNeeded", "Tout est payé : rien à mettre à crédit, désactiver le crédit.");

        public static readonly Error LimitExceeded =
            Error.Conflict(
                "Transfers.Credit.LimitExceeded",
                "Plafond de crédit du tiers dépassé dans cette agence : le Superviseur doit d'abord relever le plafond.");
    }
}
