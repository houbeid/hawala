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

    /// <summary>Créer un tiers (client, fournisseur, personnel). Donnée aussi au rôle Agent (règle validée).</summary>
    public const string PartiesCreate = "parties.create";

    /// <summary>Modifier un tiers : fiche, solde initial, plafond de crédit, « dans le bilan », archivage.</summary>
    public const string PartiesManage = "parties.manage";

    /// <summary>Voir les soldes des tiers (de son agence ; des deux pour le Superviseur).</summary>
    public const string PartiesViewBalance = "parties.view_balance";

    /// <summary>Fixer ou changer les deux taux du jour (prix d'achat, prix de vente). Superviseur seulement (règle validée).</summary>
    public const string RatesSetDaily = "rates.set_daily";

    /// <summary>Voir le bénéfice, la commission et le prix d'achat. Superviseur seulement (règle validée).</summary>
    public const string ProfitView = "profit.view";

    /// <summary>Créer un transfert depuis son agence (Superviseur et Agent, règle validée).</summary>
    public const string TransfersCreate = "transfers.create";

    /// <summary>Activer le crédit sur un transfert, dans le plafond du tiers (Superviseur et Agent, règle validée).</summary>
    public const string TransfersCredit = "transfers.credit";

    /// <summary>Livrer un transfert reçu par son agence, avec le code de retrait (Superviseur, Agent, Caissier ; règle validée).</summary>
    public const string TransfersDeliver = "transfers.deliver";

    /// <summary>Annuler un transfert non livré, avec un motif (Superviseur seulement, règle validée).</summary>
    public const string TransfersCancel = "transfers.cancel";

    /// <summary>Débloquer un transfert bloqué par les codes faux, livrer sans code (Superviseur seulement, règle validée).</summary>
    public const string TransfersManageCode = "transfers.manage_code";

    /// <summary>
    /// Modifier l'argent d'un transfert non livré : montant, prix de vente, répartition, crédit, date (Superviseur seulement, règle validée).
    /// </summary>
    public const string TransfersEditRate = "transfers.edit_rate";

    /// <summary>Encaissement, décaissement, approvisionnement de caisse, retrait du patron (Superviseur, Agent, Caissier ; règle validée).</summary>
    public const string CashCreate = "cash.create";

    /// <summary>Annuler un mouvement de caisse, ajuster un solde (Superviseur seulement, règle validée).</summary>
    public const string CashManage = "cash.manage";

    /// <summary>Enregistrer le budget de la caisse du jour (Superviseur, Caissier ; règle validée).</summary>
    public const string CashBudgetSave = "cash.budget.save";

    /// <summary>Lire le bilan général, son historique et le capital initial (Superviseur seulement, règle validée).</summary>
    public const string BalanceView = "balance.view";

    /// <summary>Saisir le capital initial et enregistrer le bilan du jour (Superviseur seulement).</summary>
    public const string BalanceManage = "balance.manage";

    /// <summary>Lire le journal d'audit (Superviseur ; règle validée le 22 octobre 2026).</summary>
    public const string AuditView = "audit.view";

    public static readonly IReadOnlyList<string> All =
    [
        UsersManage, AgenciesManage, TreasuryManage, CashBudgetRead, PartiesCreate, PartiesManage, PartiesViewBalance,
        RatesSetDaily, ProfitView, TransfersCreate, TransfersCredit, TransfersDeliver, TransfersCancel, TransfersManageCode, TransfersEditRate, CashCreate, CashManage, CashBudgetSave, BalanceView, BalanceManage, AuditView,
    ];
}