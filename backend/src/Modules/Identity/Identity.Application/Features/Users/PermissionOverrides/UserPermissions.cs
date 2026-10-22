using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Application.Features.Users.PermissionOverrides;

/// <summary>Toutes les permissions du catalogue pour un compte : accordée ou non, et d'où vient cet état.</summary>
public sealed record UserPermissions(Guid UserId, string Role, IReadOnlyList<UserPermissionState> Permissions);

/// <param name="Source">« Role » (modèle du rôle), « Added » (ajoutée à ce compte), « Removed » (retirée), « None » (ni l'un ni l'autre).</param>
public sealed record UserPermissionState(string Permission, bool Granted, string Source);

internal static class UserPermissionsMapper
{
    public static UserPermissions ToView(UserAccount account)
    {
        var template = RolePermissions.For(account.Role);
        var effective = account.EffectivePermissions;

        var states = Identity.Contracts.Permissions.All
            .Select(permission => new UserPermissionState(
                permission,
                effective.Contains(permission),
                SourceOf(permission, template, effective)))
            .ToList();

        return new UserPermissions(account.Id, account.Role.ToString(), states);
    }

    private static string SourceOf(string permission, IReadOnlySet<string> template, IReadOnlySet<string> effective) =>
        (template.Contains(permission), effective.Contains(permission)) switch
        {
            (true, true) => "Role",
            (true, false) => "Removed",
            (false, true) => "Added",
            (false, false) => "None",
        };
}
