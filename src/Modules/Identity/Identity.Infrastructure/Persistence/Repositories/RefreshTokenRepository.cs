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

    public async Task<IReadOnlyList<OpenSession>> ListOpenSessionsAsync(
        Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Le jeton actif de chaque session porte l'appareil et l'heure du dernier rafraîchissement ;
        // le premier jeton de la famille donne l'heure de connexion.
        var heads = await db.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        if (heads.Count == 0)
        {
            return [];
        }

        var families = heads.Select(t => t.FamilyId).Distinct().ToList();
        var startedAt = await db.RefreshTokens
            .AsNoTracking()
            .Where(t => families.Contains(t.FamilyId))
            .GroupBy(t => t.FamilyId)
            .Select(g => new { FamilyId = g.Key, StartedAt = g.Min(t => t.CreatedAt) })
            .ToDictionaryAsync(x => x.FamilyId, x => x.StartedAt, cancellationToken);

        return [.. heads
            .GroupBy(t => t.FamilyId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt).First())
            .Select(t => new OpenSession(t.FamilyId, startedAt[t.FamilyId], t.CreatedAt, t.ExpiresAt, t.UserAgent, t.IpAddress))
            .OrderByDescending(s => s.LastUsedAt)];
    }

    public Task<bool> IsSessionOpenAsync(Guid sessionId, CancellationToken cancellationToken) =>
        db.RefreshTokens.AnyAsync(t => t.FamilyId == sessionId && t.RevokedAt == null, cancellationToken);

    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
}