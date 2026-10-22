namespace Identity.Domain.Roles;

/// <summary>
/// Modèle de rôle d'un compte. Le détail des permissions de chaque modèle, et les ajouts / retraits
/// par compte, arrivent en phase 6. Ici on a seulement besoin de savoir qui est Superviseur.
/// </summary>
public enum RoleTemplate
{
    Supervisor,
    Agent,
    Cashier,
    ReadOnly,
}