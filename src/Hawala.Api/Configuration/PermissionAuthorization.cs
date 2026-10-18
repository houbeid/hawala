using System.Security.Claims;
using Identity.Contracts;
using Identity.Infrastructure.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Hawala.Api.Configuration;

/// <summary>Exigence : le compte connecté doit avoir cette permission.</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>Vérifie la permission côté serveur, à chaque requête (option B).</summary>
public sealed class PermissionAuthorizationHandler(IPermissionChecker permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!Guid.TryParse(context.User.FindFirstValue(HawalaClaimTypes.Subject), out var accountId))
        {
            return;
        }

        var cancellationToken = context.Resource is HttpContext http ? http.RequestAborted : CancellationToken.None;

        if (await permissions.HasPermissionAsync(accountId, requirement.Permission, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Nom de la politique d'une permission (une politique par permission du catalogue).</summary>
    public static string PolicyName(string permission) => $"permission:{permission}";

    /// <summary>Protège un endpoint par une permission : 403 si le compte ne l'a pas.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PolicyName(permission));
}