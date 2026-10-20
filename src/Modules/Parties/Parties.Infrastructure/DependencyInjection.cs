using Hawala.SharedInfrastructure.Persistence;
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
        services.AddDbContext<PartiesDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", PartiesDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PartiesDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<PartiesDbContext>());
        services.AddScoped<IPartyRepository, PartyRepository>();
        services.AddScoped<Parties.Contracts.IPartyCreditAccounts, PartyCreditAccounts>();

        services.AddScoped<IQueryHandler<ListPartiesQuery, PartyList>, ListPartiesQueryHandler>();
        services.AddScoped<IQueryHandler<GetPartyQuery, PartySummary>, GetPartyQueryHandler>();
        services.AddScoped<IQueryHandler<GetPartyStatementQuery, PartyStatement>, GetPartyStatementQueryHandler>();

        // Une seule table, un seul enregistrement : pas de transaction explicite.
        services.AddScoped<ICommandHandler<UpdatePartyCommand>, UpdatePartyCommandHandler>();
        services.AddScoped<ICommandHandler<SetCreditLimitCommand>, SetCreditLimitCommandHandler>();

        // Écrit dans deux schémas (parties + ledger) : une seule transaction commune.
        services.AddSharedTransactional<CreatePartyCommand, Guid, CreatePartyCommandHandler>();
        services.AddSharedTransactional<SetOpeningBalanceCommand, SetOpeningBalanceCommandHandler>();
        services.AddSharedTransactional<ArchivePartyCommand, ArchivePartyCommandHandler>();
        services.AddSharedTransactional<UnarchivePartyCommand, UnarchivePartyCommandHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « parties ».</summary>
    public static async Task InitializePartiesModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<PartiesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
