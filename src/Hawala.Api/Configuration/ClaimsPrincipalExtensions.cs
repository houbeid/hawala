using System.Security.Claims;
using Identity.Infrastructure.Tokens;

namespace Hawala.Api.Configuration;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Compte connecté, lu dans le JWT (« sub »). L'identité d'un auteur vient toujours d'ici,
    /// jamais du corps de la requête.
    /// </summary>
    public static bool TryGetAccountId(this ClaimsPrincipal user, out Guid accountId) =>
        Guid.TryParse(user.FindFirstValue(HawalaClaimTypes.Subject), out accountId);
}
