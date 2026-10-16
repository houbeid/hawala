using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Identité, dans le schéma PostgreSQL « identity ».
/// Contient les tables d'ASP.NET Identity (sans les rôles : on a nos propres modèles de rôle)
/// et nos tables : comptes et refresh tokens. Une seule unité de travail pour tout le module.
/// </summary>
public sealed class IdentityModuleDbContext(DbContextOptions<IdentityModuleDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options), IUnitOfWork
{
    public const string Schema = "identity";

    public DbSet<UserAccount> Accounts => Set<UserAccount>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(Schema);

        // Noms lisibles pour les tables d'ASP.NET Identity.
        builder.Entity<AppUser>().ToTable("credentials");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("credential_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("credential_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("credential_tokens");

        builder.ApplyConfigurationsFromAssembly(typeof(IdentityModuleDbContext).Assembly);
    }
}