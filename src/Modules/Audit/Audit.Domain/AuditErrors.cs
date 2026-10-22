using Hawala.SharedKernel.Results;

namespace Audit.Domain;

/// <summary>
/// Erreurs du journal d'audit. Codes stables : ne jamais renommer un code publié.
/// Elles signalent un bug du module appelant (ligne mal construite), jamais une saisie de l'utilisateur.
/// </summary>
public static class AuditErrors
{
    public static class Entry
    {
        public static readonly Error ActionEmpty =
            Error.Validation("Audit.Entry.ActionEmpty", "Le code de l'action est obligatoire.");

        public static readonly Error ActionTooLong =
            Error.Validation("Audit.Entry.ActionTooLong", "Le code de l'action dépasse 100 caractères.");

        public static readonly Error EntityTypeEmpty =
            Error.Validation("Audit.Entry.EntityTypeEmpty", "Le type de l'entité est obligatoire.");

        public static readonly Error EntityTypeTooLong =
            Error.Validation("Audit.Entry.EntityTypeTooLong", "Le type de l'entité dépasse 100 caractères.");

        public static readonly Error ReasonTooLong =
            Error.Validation("Audit.Entry.ReasonTooLong", "Le motif dépasse 500 caractères.");
    }

    public static class List
    {
        public static readonly Error PeriodInvalid =
            Error.Validation("Audit.List.PeriodInvalid", "La date de fin doit être après ou égale à la date de début.");

        public static readonly Error PageInvalid =
            Error.Validation("Audit.List.PageInvalid", "La page commence à 1.");
    }

    public static class Change
    {
        public static readonly Error FieldEmpty =
            Error.Validation("Audit.Change.FieldEmpty", "Le nom du champ changé est obligatoire.");

        public static readonly Error FieldTooLong =
            Error.Validation("Audit.Change.FieldTooLong", "Le nom du champ changé dépasse 100 caractères.");

        public static readonly Error FieldDuplicate =
            Error.Validation("Audit.Change.FieldDuplicate", "Un même champ apparaît deux fois dans les changements.");

        public static readonly Error ValueTooLong =
            Error.Validation("Audit.Change.ValueTooLong", "Une valeur avant / après dépasse 4 000 caractères.");

        public static readonly Error Unchanged =
            Error.Validation("Audit.Change.Unchanged", "Un champ listé comme changé a la même valeur avant et après.");
    }
}
