using Hawala.SharedKernel.Results;

namespace Treasury.Domain;

/// <summary>Erreurs métier de la Trésorerie. Codes stables : ne jamais renommer un code publié.</summary>
public static class TreasuryErrors
{
    public static class Account
    {
        public static readonly Error NotFound =
            Error.NotFound("Treasury.Account.NotFound", "Compte de trésorerie introuvable.");

        public static readonly Error NameEmpty =
            Error.Validation("Treasury.Account.NameEmpty", "Le nom du compte est obligatoire.");

        public static readonly Error NameTooLong =
            Error.Validation("Treasury.Account.NameTooLong", "Le nom du compte dépasse 50 caractères.");

        public static readonly Error NameTaken =
            Error.Conflict("Treasury.Account.NameTaken", "Un compte de cette agence porte déjà ce nom.");

        public static readonly Error TypeInvalid =
            Error.Validation("Treasury.Account.TypeInvalid", "Type inconnu. Valeurs possibles : Cash, Bank, Wallet.");

        public static readonly Error AgencyRequired =
            Error.Validation("Treasury.Account.AgencyRequired", "Un compte de trésorerie appartient toujours à une agence.");

        public static readonly Error AgencyNotFound =
            Error.Validation("Treasury.Account.AgencyNotFound", "Cette agence n'existe pas.");

        public static readonly Error AlreadyInactive =
            Error.Conflict("Treasury.Account.AlreadyInactive", "Ce compte est déjà désactivé.");

        public static readonly Error AlreadyActive =
            Error.Conflict("Treasury.Account.AlreadyActive", "Ce compte est déjà actif.");

        // Règle validée : sinon de l'argent « disparaîtrait » des listes.
        public static readonly Error BalanceNotZero =
            Error.Conflict(
                "Treasury.Account.BalanceNotZero",
                "Le solde doit être nul pour désactiver le compte : transférer d'abord l'argent restant.");

        public static readonly Error OpeningBalanceRequired =
            Error.Validation("Treasury.Account.OpeningBalanceRequired", "Indiquer le solde de départ du compte.");

        public static readonly Error Inactive =
            Error.Conflict("Treasury.Account.Inactive", "Compte désactivé : le réactiver avant de changer son solde de départ.");
    }

    public static class Statement
    {
        // Un relevé montre des montants : même règle que les soldes (cash.budget.read, règle validée).
        public static readonly Error NotVisible =
            Error.Forbidden("Treasury.Statement.NotVisible", "Le relevé d'un compte de trésorerie demande la permission de lire le budget de la caisse.");
    }
}
