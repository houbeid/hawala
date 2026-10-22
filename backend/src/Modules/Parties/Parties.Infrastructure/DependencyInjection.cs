using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedInfrastructure.Security;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Parties.Application.Abstractions;
using Hawala.SharedKernel.Messaging;
using Parties.Application.Features.Parties;
using Parties.Application.Features.Parties.ArchiveParty;
using Parties.Application.Features.Parties.CreateParty;
using Parties.Application.Features.Parties.GetParty;
using Parties.Application.Features.Parties.GetPartyStatement;
using Parties.Application.Features.Parties.ListParties;
using Parties.Application.Features.Parties.SetCreditLimit;
using Parties.Application.Features.Parties.SetOpeningBalance;
using Parties.Application.Features.Parties.UpdateParty;
using Parties.Application.Features.Scopes;
using Parties.Infrastructure.Persistence;
using Parties.Infrastructure.Persistence.Repositories;

namespace Parties.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre les Tiers. Appelé une fois par l'API dans Program.cs, après le grand livre.</summary>
    public static IServiceCollection AddPartiesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Même connexion que le grand livre : un tiers et ses comptes au grand livre sont créés ensemble.
        services.AddSharedConnection(connectionString);
        services.AddFieldProtection(configuration);
        services.AddDbContext<PartiesDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PartiesDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PartiesDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<PartiesDbContext>());
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<IPartyScopeRepository, PartyScopeRepository>();
        services.AddScoped<PartyVisibility>();
        services.AddScoped<Parties.Contracts.IPartyCreditAccounts, PartyCreditAccounts>();
        services.AddScoped<Parties.Contracts.IPartyDirectory, PartyDirectory>();

        services.AddScoped<IQueryHandler<ListPartiesQuery, PartyList>, ListPartiesQueryHandler>();
        services.AddScoped<IQueryHandler<GetPartyQuery, PartySummary>, GetPartyQueryHandler>();
        services.AddScoped<IQueryHandler<GetPartyStatementQuery, PartyStatement>, GetPartyStatementQueryHandler>();

        // Une seule table, mais la ligne d'audit doit être dans la même transaction (phase 6).
        services.AddSharedTransactional<UpdatePartyCommand, UpdatePartyCommandHandler>();
        services.AddSharedTransactional<SetCreditLimitCommand, SetCreditLimitCommandHandler>();

        // Écrit dans deux schémas (parties + ledger) : une seule transaction commune.
        services.AddSharedTransactional<CreatePartyCommand, Guid, CreatePartyCommandHandler>();
        services.AddSharedTransactional<SetOpeningBalanceCommand, SetOpeningBalanceCommandHandler>();
        services.AddSharedTransactional<ArchivePartyCommand, ArchivePartyCommandHandler>();
        services.AddSharedTransactional<UnarchivePartyCommand, UnarchivePartyCommandHandler>();

        // « Limiter les tiers » : la règle et sa ligne d'audit dans la même transaction.
        services.AddSharedTransactional<SetAccountPartyScopeCommand, AccountPartyScopeView, AccountPartyScopeHandlers>();
        services.AddScoped<IQueryHandler<GetAccountPartyScopeQuery, AccountPartyScopeView>>(
            sp => sp.GetRequiredService<AccountPartyScopeHandlers>());

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « parties ».</summary>
    public static async Task InitializePartiesModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<PartiesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        // Phase 6 : les téléphones et NNI encore en clair (saisis avant le chiffrement) sont chiffrés une fois.
        await EncryptLegacyIdentifiersAsync(db, cancellationToken);
    }

    /// <summary>
    /// Chiffre les valeurs écrites avant la phase 6 (sans le préfixe de chiffrement) et calcule leurs empreintes.
    /// Sans effet sur une base déjà à jour. Les tiers sont lus par EF (la lecture accepte une ancienne valeur en clair),
    /// puis marqués modifiés : l'enregistrement les chiffre et remplit les empreintes.
    /// </summary>
    private static async Task EncryptLegacyIdentifiersAsync(PartiesDbContext db, CancellationToken cancellationToken)
    {
        var legacyIds = await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value" FROM parties.parties
                WHERE ("Phone" IS NOT NULL AND "Phone" NOT LIKE 'v1:%')
                   OR ("Nni" IS NOT NULL AND "Nni" NOT LIKE 'v1:%')
                """)
            .ToListAsync(cancellationToken);
        if (legacyIds.Count == 0)
        {
            return;
        }

        var parties = await db.Parties.Where(p => legacyIds.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var party in parties)
        {
            db.Entry(party).Property(p => p.Phone).IsModified = party.Phone is not null;
            db.Entry(party).Property(p => p.Nni).IsModified = party.Nni is not null;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
