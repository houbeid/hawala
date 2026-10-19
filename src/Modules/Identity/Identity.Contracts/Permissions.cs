namespace Identity.Contracts;

/// <summary>
/// Catalogue des permissions métier, vérifiées côté serveur.
/// Une permission n'est ajoutée ici que lorsqu'un endpoint réel en a besoin.
/// Les codes sont stables : ne jamais renommer un code publié.
/// </summary>
public static class Permissions
{
    /// <summary>Créer, bloquer, débloquer, déplacer un compte ; changer son rôle ; réinitialiser son mot de passe.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Modifier une agence (sa couleur).</summary>
    public const string AgenciesManage = "agencies.manage";

    /// <summary>Créer, renommer, désactiver, réactiver un compte de trésorerie (caisse, banque, wallet).</summary>
    public const string TreasuryManage = "treasury.manage";

    /// <summary>Lire le budget de la caisse, dont les soldes des comptes de trésorerie de son agence.</summary>
    public const string CashBudgetRead = "cash.budget.read";

    public static readonly IReadOnlyList<string> All = [UsersManage, AgenciesManage, TreasuryManage, CashBudgetRead];
}