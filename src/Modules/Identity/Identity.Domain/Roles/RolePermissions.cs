using Identity.Contracts;

namespace Identity.Domain.Roles;

/// <summary>
/// Permissions données par chaque modèle de rôle.
/// Pour l'instant : le Superviseur a tout, les agents aucune permission de gestion.
/// POINT OUVERT : les permissions par défaut d'un Agent, d'un Caissier et d'un compte Consultation
/// (transferts, caisse…) sont à valider avec le patron avant la phase 3. Les ajouts / retraits
/// par compte arriveront en phase 6, par-dessus ces valeurs par défaut.
/// </summary>
public static class RolePermissions
{
    // Droits par défaut validés le 20 octobre 2026 (tableau complet dans le skill du projet).
    // Seules les permissions qui existent déjà sont listées ; celles des transferts, de la caisse et de l'export
    // s'ajouteront avec leurs endpoints, selon le même tableau.
    private static readonly HashSet<string> Supervisor = [.. Permissions.All];

    // L'agent : crée des tiers, voit les soldes des tiers et des caisses de son agence.
    private static readonly HashSet<string> Agent =
        [Permissions.PartiesCreate, Permissions.PartiesViewBalance, Permissions.CashBudgetRead];

    // Le caissier : tient la caisse ; ne voit pas les soldes des tiers.
    private static readonly HashSet<string> Cashier = [Permissions.CashBudgetRead];

    // La consultation : lit seulement.
    private static readonly HashSet<string> ReadOnly = [Permissions.PartiesViewBalance, Permissions.CashBudgetRead];

    public static IReadOnlySet<string> For(RoleTemplate role) => role switch
    {
        RoleTemplate.Supervisor => Supervisor,
        RoleTemplate.Agent => Agent,
        RoleTemplate.Cashier => Cashier,
        RoleTemplate.ReadOnly => ReadOnly,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Rôle inconnu."),
    };
}