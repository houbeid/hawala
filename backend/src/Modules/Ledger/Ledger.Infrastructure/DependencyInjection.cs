using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Time;
using Ledger.Application.Abstractions;
using Hawala.SharedKernel.Messaging;
using Ledger.Application.Ledger;
using Ledger.Application.StartDate;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Ledger.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ledger.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre tout le grand livre. Appelé une fois par l'API dans Program.cs.</summary>
    public static IServiceCollection AddLedgerModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Option A validée : le grand livre écrit sur la connexion partagée de la requête, pour être
        // dans la même transaction que le module appelant (caisse, tiers, transfert…).
        services.AddSharedConnection(connectionString);
        services.AddDbContext<LedgerDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", LedgerDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<LedgerDbContext>());

        // Rattaché à la transaction commune quand une commande d'un module d'argent l'ouvre.
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<LedgerDbContext>());
        services.AddScoped<ILedgerAccountRepository, LedgerAccountRepository>();
        services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
        services.AddScoped<ILedger, LedgerService>();

        // Date de démarrage (mise en service, phase 8).
        services.AddScoped<ILedgerSettingsRepository, LedgerSettingsRepository>();
        services.AddScoped<IQueryHandler<GetStartDateQuery, StartDateView>, GetStartDateQueryHandler>();
        services.AddSharedTransactional<SetStartDateCommand, StartDateView, SetStartDateCommandHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « ledger » (tables, index, déclencheurs d'immuabilité et d'équilibre).</summary>
    public static async Task InitializeLedgerModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
