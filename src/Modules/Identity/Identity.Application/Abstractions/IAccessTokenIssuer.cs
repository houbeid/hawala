using Identity.Domain.Accounts;

namespace Identity.Application.Abstractions;

/// <summary>Jeton d'accès (JWT) envoyé à chaque requête.</summary>
public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>
/// Émet le JWT. Option B validée : il ne contient que l'identité (id du compte, SecurityVersion,
/// l'indication « mot de passe à changer » et, depuis la phase 6, l'id de la session), jamais les permissions,
/// qui sont lues côté serveur.
/// </summary>
public interface IAccessTokenIssuer
{
    /// <param name="sessionId">Session (famille de refresh tokens) à laquelle appartient ce jeton : fermer la session le rend invalide.</param>
    AccessToken Issue(UserAccount account, Guid sessionId);
}