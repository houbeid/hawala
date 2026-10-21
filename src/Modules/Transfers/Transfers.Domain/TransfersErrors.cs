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

    public static class Delivery
    {
        public static readonly Error CodeRequired =
            Error.Validation("Transfers.Delivery.CodeRequired", "Saisir le code de retrait donné par le bénéficiaire.");

        public static readonly Error CodeLocked =
            Error.Conflict(
                "Transfers.Delivery.CodeLocked",
                "Transfert bloqué après 5 codes faux : seul le Superviseur peut le débloquer.");

        public static readonly Error AlreadyDelivered =
            Error.Conflict("Transfers.Delivery.AlreadyDelivered", "Ce transfert est déjà livré.");

        public static readonly Error Cancelled =
            Error.Conflict("Transfers.Delivery.Cancelled", "Ce transfert est annulé : il ne peut pas être livré.");

        public static readonly Error NotDestinationAgency =
            Error.Forbidden("Transfers.Delivery.NotDestinationAgency", "Seule l'agence de destination livre ce transfert.");

        public static readonly Error DateBeforeTransfer =
            Error.Validation("Transfers.Delivery.DateBeforeTransfer", "La livraison ne peut pas être datée avant le transfert.");

        public static readonly Error TotalMismatch =
            Error.Validation("Transfers.Delivery.TotalMismatch", "Le total livré doit être égal au montant à livrer.");

        public static readonly Error NoteTooLong =
            Error.Validation("Transfers.Delivery.NoteTooLong", "Note de livraison : 500 caractères au plus.");

        /// <summary>Code faux ; l'API ajoute le nombre d'essais restants (« remainingAttempts ») à la réponse.</summary>
        public static readonly Error WrongCode =
            Error.Validation("Transfers.Delivery.WrongCode", "Code de retrait faux.");
    }

    public static class Cancellation
    {
        public static readonly Error ReasonRequired =
            Error.Validation("Transfers.Cancellation.ReasonRequired", "Le motif de l'annulation est obligatoire.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Transfers.Cancellation.ReasonTooLong", "Motif de l'annulation : 500 caractères au plus.");

        public static readonly Error AlreadyCancelled =
            Error.Conflict("Transfers.Cancellation.AlreadyCancelled", "Ce transfert est déjà annulé.");

        public static readonly Error Delivered =
            Error.Conflict("Transfers.Cancellation.Delivered", "Un transfert livré ne peut pas être annulé.");

        public static readonly Error DateBeforeTransfer =
            Error.Validation("Transfers.Cancellation.DateBeforeTransfer", "L'annulation ne peut pas être datée avant le transfert.");
    }

    public static class Filter
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Transfers.Filter.PeriodInvalid", "La date de début doit précéder la date de fin.");

        public static readonly Error StatusInvalid =
            Error.Validation("Transfers.Filter.StatusInvalid", "Statut inconnu : NotDelivered, Delivered ou Cancelled.");

        public static readonly Error DirectionInvalid =
            Error.Validation("Transfers.Filter.DirectionInvalid", "Sens inconnu : Sent ou Received.");

        public static readonly Error DirectionNeedsAgency =
            Error.Validation("Transfers.Filter.DirectionNeedsAgency", "Choisir l'agence pour filtrer les transferts envoyés ou reçus.");

        public static readonly Error AgencyNotAllowed =
            Error.Forbidden("Transfers.Filter.AgencyNotAllowed", "Un agent ne voit que les transferts de son agence.");

        public static readonly Error AgencyRequired =
            Error.Validation("Transfers.Filter.AgencyRequired", "Choisir l'agence des cartes.");

        public static readonly Error AgencyNotFound =
            Error.Validation("Transfers.Filter.AgencyNotFound", "Agence inconnue.");

        public static readonly Error PageInvalid =
            Error.Validation("Transfers.Filter.PageInvalid", "Le numéro de page commence à 1.");
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
