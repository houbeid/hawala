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
    private static readonly HashSet<string> Supervisor = [.. Permissions.All];
    private static readonly HashSet<string> None = [];

    public static IReadOnlySet<string> For(RoleTemplate role) => role switch
    {
        RoleTemplate.Supervisor => Supervisor,
        _ => None,
    };
}