using Identity.Domain.Accounts;

namespace Identity.Application.Abstractions;

/// <summary>Jeton d'accès (JWT) envoyé à chaque requête.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Émet le JWT. Option B validée : il ne contient que l'identité (id du compte, SecurityVersion,
/// et l'indication « mot de passe à changer »), jamais les permissions, qui sont lues côté serveur.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(UserAccount account);
}