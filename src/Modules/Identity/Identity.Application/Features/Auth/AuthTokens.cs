using Identity.Application.Abstractions;

namespace Identity.Application.Features.Auth;

/// <summary>
/// Ce que reçoit le client après une connexion, un rafraîchissement ou un changement de mot de passe.
/// L'API place <see cref="RefreshToken"/> dans un cookie HttpOnly et ne le renvoie jamais dans le corps JSON.
/// </summary>
/// <param name="MustChangePassword">
/// Vrai tant que le mot de passe temporaire n'a pas été changé : l'interface ouvre directement
/// l'écran de changement, et le JWT n'autorise que cette action.
/// </param>
/// <param name="SessionId">Session ouverte ou prolongée (le « sid » du jeton d'accès).</param>
public sealed record AuthTokens(
    AccessToken AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool MustChangePassword,
    Guid SessionId);