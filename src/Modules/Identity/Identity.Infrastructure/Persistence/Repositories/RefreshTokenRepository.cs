using Identity.Application.Abstractions;
using Identity.Domain.RefreshTokens;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(IdentityModuleDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}