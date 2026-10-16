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

    void Add(RefreshToken token);
}