using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Treasury.Application.Abstractions;
using Hawala.SharedKernel.Messaging;
using Treasury.Application.Features.Accounts;
using Treasury.Application.Features.Accounts.CreateTreasuryAccount;
using Treasury.Application.Features.Accounts.DeactivateTreasuryAccount;
using Treasury.Application.Features.Accounts.ReactivateTreasuryAccount;
using Treasury.Application.Features.Accounts.RenameTreasuryAccount;
using Treasury.Application.Features.Accounts.SetTreasuryOpeningBalance;
using Treasury.Application.Features.Accounts.GetTreasuryAccount;
using Treasury.Application.Features.Accounts.GetTreasuryAccountStatement;
using Treasury.Application.Features.Accounts.ListTreasuryAccounts;
using Treasury.Infrastructure.Persistence;
using Treasury.Infrastructure.Persistence.Repositories;

namespace Treasury.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre la Trésorerie. Appelé une fois par l'API dans Program.cs, après le grand livre.</summary>
    public static IServiceCollection AddTreasuryModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Même connexion que le grand livre : un compte de trésorerie et son compte au grand livre
        // sont créés dans la même transaction.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<TreasuryDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TreasuryDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TreasuryDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<TreasuryDbContext>());
        services.AddScoped<ITreasuryAccountRepository, TreasuryAccountRepository>();
        services.AddScoped<Treasury.Contracts.ITreasuryDirectory, TreasuryDirectory>();

        services.AddScoped<IQueryHandler<ListTreasuryAccountsQuery, IReadOnlyList<TreasuryAccountSummary>>, ListTreasuryAccountsQueryHandler>();
        services.AddScoped<IQueryHandler<GetTreasuryAccountQuery, TreasuryAccountSummary>, GetTreasuryAccountQueryHandler>();
        services.AddScoped<IQueryHandler<GetTreasuryAccountStatementQuery, TreasuryAccountStatement>, GetTreasuryAccountStatementQueryHandler>();

        // Une seule table, un seul enregistrement : pas de transaction explicite.
        services.AddScoped<ICommandHandler<RenameTreasuryAccountCommand>, RenameTreasuryAccountCommandHandler>();

        // Écrivent dans deux schémas (treasury + ledger) : une seule transaction commune.
        services.AddSharedTransactional<CreateTreasuryAccountCommand, Guid, CreateTreasuryAccountCommandHandler>();
        services.AddSharedTransactional<DeactivateTreasuryAccountCommand, DeactivateTreasuryAccountCommandHandler>();
        services.AddSharedTransactional<ReactivateTreasuryAccountCommand, ReactivateTreasuryAccountCommandHandler>();

        // Solde de départ (phase 8) : écriture d'ouverture au grand livre et ligne d'audit, dans la même transaction.
        services.AddSharedTransactional<SetTreasuryOpeningBalanceCommand, SetTreasuryOpeningBalanceCommandHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « treasury ».</summary>
    public static async Task InitializeTreasuryModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TreasuryDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
