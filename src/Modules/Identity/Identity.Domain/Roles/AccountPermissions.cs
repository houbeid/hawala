using Identity.Contracts;

namespace Identity.Domain.Roles;

/// <summary>
/// Permissions effectives d'un compte = modèle de son rôle + ajouts − retraits.
/// Un Superviseur a toujours tout (règle validée le 21 octobre 2026) : ses ajustements, s'il en avait, sont ignorés.
/// Une permission ne change jamais le périmètre : un agent reste sur son agence quoi qu'on lui donne.
/// </summary>
public static class AccountPermissions
{
    public static IReadOnlySet<string> Effective(RoleTemplate role, IEnumerable<PermissionOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var template = RolePermissions.For(role);
        if (role == RoleTemplate.Supervisor)
        {
            return template;
        }

        var effective = new HashSet<string>(template, StringComparer.Ordinal);
        foreach (var adjustment in overrides)
        {
            if (adjustment.Kind == PermissionOverrideKind.Granted)
            {
                effective.Add(adjustment.Permission);
            }
            else
            {
                effective.Remove(adjustment.Permission);
            }
        }

        // Une permission retirée du catalogue (code publié jamais renommé, mais on ne sait jamais) n'est pas accordée.
        effective.IntersectWith(Permissions.All);
        return effective;
    }

    /// <summary>
    /// Ajustements qui donnent exactement <paramref name="desired"/> à partir du modèle du rôle.
    /// Triés par code, pour un résultat stable.
    /// </summary>
    public static IReadOnlyList<PermissionOverride> OverridesFor(RoleTemplate role, IReadOnlySet<string> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);

        var template = RolePermissions.For(role);
        var granted = desired.Where(p => !template.Contains(p))
            .Select(p => new PermissionOverride(p, PermissionOverrideKind.Granted));
        var revoked = template.Where(p => !desired.Contains(p))
            .Select(p => new PermissionOverride(p, PermissionOverrideKind.Revoked));

        return [.. granted.Concat(revoked).OrderBy(o => o.Permission, StringComparer.Ordinal)];
    }
}
