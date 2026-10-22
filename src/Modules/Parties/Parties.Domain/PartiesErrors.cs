using Hawala.SharedKernel.Results;

namespace Parties.Domain;

/// <summary>Erreurs métier des Tiers. Codes stables : ne jamais renommer un code publié.</summary>
public static class PartiesErrors
{
    public static class Party
    {
        public static readonly Error NotFound =
            Error.NotFound("Parties.Party.NotFound", "Tiers introuvable.");

        public static readonly Error NameEmpty =
            Error.Validation("Parties.Party.NameEmpty", "Le nom du tiers est obligatoire.");

        public static readonly Error NameTooLong =
            Error.Validation("Parties.Party.NameTooLong", "Le nom du tiers dépasse 30 caractères.");

        public static readonly Error TypeInvalid =
            Error.Validation("Parties.Party.TypeInvalid", "Type inconnu. Valeurs possibles : Client, Supplier, Staff, Other.");

        public static readonly Error NniTooLong =
            Error.Validation("Parties.Party.NniTooLong", "Le NNI dépasse 20 caractères.");

        public static readonly Error AddressTooLong =
            Error.Validation("Parties.Party.AddressTooLong", "L'adresse dépasse 200 caractères.");

        public static readonly Error NoteTooLong =
            Error.Validation("Parties.Party.NoteTooLong", "La note dépasse 500 caractères.");

        public static readonly Error AgencyAccountNotFound =
            Error.NotFound("Parties.Party.AgencyAccountNotFound", "Ce tiers n'a pas de compte dans cette agence.");

        public static readonly Error Archived =
            Error.Conflict("Parties.Party.Archived", "Ce tiers est archivé : le désarchiver d'abord.");

        public static readonly Error AlreadyArchived =
            Error.Conflict("Parties.Party.AlreadyArchived", "Ce tiers est déjà archivé.");

        public static readonly Error NotArchived =
            Error.Conflict("Parties.Party.NotArchived", "Ce tiers n'est pas archivé.");

        // Règle validée : archivage seulement à solde nul dans les deux agences.
        public static readonly Error BalanceNotZero =
            Error.Conflict("Parties.Party.BalanceNotZero", "Le solde doit être nul dans les deux agences pour archiver ce tiers.");

        public static readonly Error CreditLimitNegative =
            Error.Validation("Parties.Party.CreditLimitNegative", "Le plafond de crédit est positif ou nul (0 = aucun crédit ; vide = pas de plafond).");

        public static readonly Error AmountRequired =
            Error.Validation("Parties.Party.AmountRequired", "Le montant est obligatoire (positif : il nous doit ; négatif : on lui doit).");

        // La fiche est envoyée en entier : « dans le bilan » absent ne doit pas sortir le tiers du bilan en silence.
        public static readonly Error InBalanceSheetRequired =
            Error.Validation("Parties.Party.InBalanceSheetRequired", "Indiquer si le tiers est « dans le bilan » (true ou false).");

        // Règle validée : un doublon de téléphone ou de NNI est interdit.
        public static readonly Error PhoneTaken =
            Error.Conflict("Parties.Party.PhoneTaken", "Ce numéro appartient déjà à un autre tiers.");

        public static readonly Error NniTaken =
            Error.Conflict("Parties.Party.NniTaken", "Ce NNI appartient déjà à un autre tiers.");
    }

    public static class List
    {
        public static readonly Error BalanceSheetFilterInvalid =
            Error.Validation("Parties.List.BalanceSheetFilterInvalid", "Filtre « bilan » inconnu. Valeurs possibles : All, In, Out.");

        public static readonly Error BalanceFilterInvalid =
            Error.Validation("Parties.List.BalanceFilterInvalid", "Filtre « solde » inconnu. Valeurs possibles : All, Red, Green.");

        // Filtrer par solde révélerait qui est rouge ou vert : réservé à ceux qui voient les soldes.
        public static readonly Error BalanceNotVisible =
            Error.Forbidden("Parties.List.BalanceNotVisible", "Filtrer par solde demande la permission de voir les soldes.");

        public static readonly Error AgencyRequiredForBalanceFilter =
            Error.Validation("Parties.List.AgencyRequiredForBalanceFilter", "Choisir une agence pour filtrer par solde.");
    }

    public static class Statement
    {
        // Un relevé montre des montants : même règle que les soldes.
        public static readonly Error NotVisible =
            Error.Forbidden("Parties.Statement.NotVisible", "Le relevé d'un tiers demande la permission de voir les soldes.");

        public static readonly Error AgencyRequired =
            Error.Validation("Parties.Statement.AgencyRequired", "Choisir l'agence du relevé.");
    }

    public static class Phone
    {
        public static readonly Error Invalid =
            Error.Validation(
                "Parties.Phone.Invalid",
                "Le numéro doit être au format international, ex. +222 22 12 34 56 ou +221 77 123 45 67.");
    }

    /// <summary>« Limiter les tiers » d'un compte (phase 6).</summary>
    public static class Scope
    {
        public static readonly Error SupervisorOnly =
            Error.Forbidden("Parties.Scope.SupervisorOnly", "Seul un Superviseur fixe les tiers autorisés d'un compte.");

        public static readonly Error AccountNotFound =
            Error.NotFound("Parties.Scope.AccountNotFound", "Compte introuvable.");

        public static readonly Error SupervisorSeesAll =
            Error.Conflict("Parties.Scope.SupervisorSeesAll", "Un Superviseur voit toujours tous les tiers : il ne peut pas être limité.");

        public static readonly Error LimitedRequired =
            Error.Validation("Parties.Scope.LimitedRequired", "Indiquer si le compte est limité (true ou false).");

        public static readonly Error PartyNotFound =
            Error.Validation("Parties.Scope.PartyNotFound", "Un des tiers choisis n'existe pas.");
    }
}
