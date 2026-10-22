namespace Identity.Domain.RefreshTokens;

/// <summary>
/// Résultat de l'émission d'un refresh token.
/// <paramref name="PlainValue"/> est la seule copie en clair du jeton : elle part dans le cookie
/// HttpOnly et n'est jamais stockée. La base ne garde que le hash (<see cref="RefreshToken.TokenHash"/>).
/// </summary>
public sealed record IssuedRefreshToken(RefreshToken Token, string PlainValue);