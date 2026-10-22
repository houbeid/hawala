using Hawala.SharedKernel.Results;

namespace Ledger.Domain;

/// <summary>
/// Erreurs métier du grand livre. Codes stables : ne jamais renommer un code publié.
/// La plupart signalent un bug du module appelant (écriture mal construite) plutôt qu'une saisie :
/// elles restent des erreurs métier pour être tracées et testées comme les autres.
/// </summary>
public static class LedgerErrors
{
    public static class Account
    {
        public static readonly Error NotFound =
            Error.NotFound("Ledger.Account.NotFound", "Compte du grand livre introuvable.");

        public static readonly Error Inactive =
            Error.Conflict("Ledger.Account.Inactive", "Ce compte est fermé : il ne reçoit plus d'écriture.");

        public static readonly Error NameEmpty =
            Error.Validation("Ledger.Account.NameEmpty", "Le nom du compte est obligatoire.");

        public static readonly Error NameTooLong =
            Error.Validation("Ledger.Account.NameTooLong", "Le nom du compte dépasse 100 caractères.");

        public static readonly Error AgencyRequired =
            Error.Validation("Ledger.Account.AgencyRequired", "Un compte appartient toujours à une agence.");

        public static readonly Error OwnerRequired =
            Error.Validation("Ledger.Account.OwnerRequired", "Le compte d'une caisse ou d'un tiers doit indiquer son propriétaire.");

        public static readonly Error KindInvalid =
            Error.Validation(
                "Ledger.Account.KindInvalid",
                "Seuls les comptes de caisse et de tiers s'ouvrent ainsi ; les comptes système sont créés par le grand livre.");
    }

    public static class Entry
    {
        public static readonly Error NotFound =
            Error.NotFound("Ledger.Entry.NotFound", "Écriture introuvable.");

        public static readonly Error TooFewLines =
            Error.Validation("Ledger.Entry.TooFewLines", "Une écriture a au moins deux lignes (un débit et un crédit).");

        public static readonly Error Unbalanced =
            Error.Validation("Ledger.Entry.Unbalanced", "Écriture déséquilibrée : pour chaque devise, total des débits = total des crédits.");

        public static readonly Error LabelEmpty =
            Error.Validation("Ledger.Entry.LabelEmpty", "Le libellé de l'écriture est obligatoire.");

        public static readonly Error TypeEmpty =
            Error.Validation("Ledger.Entry.TypeEmpty", "Le type de l'écriture est obligatoire.");

        public static readonly Error SourceEmpty =
            Error.Validation("Ledger.Entry.SourceEmpty", "Le module à l'origine de l'écriture est obligatoire.");

        public static readonly Error FutureDate =
            Error.Validation("Ledger.Entry.FutureDate", "La date de l'opération ne peut pas être dans le futur.");

        public static readonly Error AuthorRequired =
            Error.Validation("Ledger.Entry.AuthorRequired", "L'auteur de l'écriture est obligatoire.");
    }

    public static class Line
    {
        public static readonly Error AmountNotPositive =
            Error.Validation("Ledger.Line.AmountNotPositive", "Le montant d'une ligne est strictement positif ; le sens dit débit ou crédit.");

        public static readonly Error CurrencyMismatch =
            Error.Validation("Ledger.Line.CurrencyMismatch", "La devise de la ligne doit être celle de son compte.");
    }

    public static class Statement
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Ledger.Statement.PeriodInvalid", "La date de début doit être avant ou égale à la date de fin.");
    }

    public static class Reversal
    {
        public static readonly Error ReasonRequired =
            Error.Validation("Ledger.Reversal.ReasonRequired", "Le motif de la contre-passation est obligatoire.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Ledger.Reversal.ReasonTooLong", "Le motif de la contre-passation dépasse 500 caractères.");

        public static readonly Error AlreadyReversed =
            Error.Conflict("Ledger.Reversal.AlreadyReversed", "Cette écriture a déjà été contre-passée.");

        public static readonly Error CannotReverseAReversal =
            Error.Conflict(
                "Ledger.Reversal.CannotReverseAReversal",
                "Une contre-passation ne peut pas être contre-passée : ressaisir l'opération.");
    }
}
