using Identity.Domain.RefreshTokens;

namespace Identity.Application.Abstractions;

/// <summary>Accès aux refresh tokens (stockés hachés). Enregistrement par <see cref="IUnitOfWork"/>.</summary>
public interface IRefreshTokenRepository
{
    /// <summary>Retrouve un jeton par le hash du jeton présenté (voir <see cref="RefreshToken.HashOf"/>).</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Jetons non révoqués d'une session : à révoquer en cas de réutilisation détectée.</summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    /// <summary>Jetons non révoqués d'un compte : à révoquer au blocage ou au changement de mot de passe.</summary>
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sessions ouvertes d'un compte (au moins un jeton ni révoqué ni expiré), de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<OpenSession>> ListOpenSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>La session a-t-elle encore un jeton non révoqué ? (fermée = tous ses jetons révoqués)</summary>
    Task<bool> IsSessionOpenAsync(Guid sessionId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}

/// <summary>Une session ouverte : une connexion sur un appareil.</summary>
/// <param name="StartedAt">Connexion (premier jeton de la famille).</param>
/// <param name="LastUsedAt">Dernier rafraîchissement (au plus 15 minutes de retard sur l'activité réelle).</param>
/// <param name="UserAgent">Appareil du dernier rafraîchissement.</param>
public sealed record OpenSession(
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? UserAgent,
    string? IpAddress);
