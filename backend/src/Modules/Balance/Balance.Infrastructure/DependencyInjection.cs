using Hawala.SharedInfrastructure.Persistence;
using Balance.Application.Abstractions;
using Balance.Application.Features;
using Balance.Infrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Balance.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre le Bilan. Appelé une fois par l'API dans Program.cs, après la Caisse et le Bénéfice.</summary>
    public static IServiceCollection AddBalanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Phase 6 (tour 7d) : connexion partagée de la requête, pour que l'action et sa ligne d'audit
        // soient dans la même transaction.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<BalanceDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", BalanceDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BalanceDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<BalanceDbContext>());
        services.AddScoped<ICapitalRepository, CapitalRepository>();
        services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();

        services.AddScoped<BalanceHandlers>();
        services.AddScoped<IQueryHandler<GetBalanceQuery, BalanceOverview>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<ICommandHandler<SaveBalanceCommand, BalanceSnapshotView>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<IQueryHandler<ListBalanceSnapshotsQuery, IReadOnlyList<BalanceSnapshotView>>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddSharedTransactional<SetInitialCapitalCommand, BalanceHandlers>();
        services.AddScoped<IQueryHandler<GetCapitalQuery, IReadOnlyList<CaisseCapitalView>>>(sp => sp.GetRequiredService<BalanceHandlers>());

        // Bilan général (31 octobre 2026) : le capital général écrit une ligne d'audit, donc dans la transaction commune.
        services.AddScoped<IGeneralCapitalRepository, GeneralCapitalRepository>();
        services.AddScoped<IGeneralBalanceSnapshotRepository, GeneralBalanceSnapshotRepository>();
        services.AddScoped<GeneralBalanceHandlers>();
        services.AddScoped<IQueryHandler<GetGeneralBalanceQuery, GeneralBalanceView>>(sp => sp.GetRequiredService<GeneralBalanceHandlers>());
        services.AddScoped<ICommandHandler<SaveGeneralBalanceCommand, GeneralBalanceSnapshotView>>(sp => sp.GetRequiredService<GeneralBalanceHandlers>());
        services.AddScoped<IQueryHandler<ListGeneralBalanceSnapshotsQuery, IReadOnlyList<GeneralBalanceSnapshotView>>>(sp => sp.GetRequiredService<GeneralBalanceHandlers>());
        services.AddSharedTransactional<SetGeneralCapitalCommand, GeneralBalanceHandlers>();
        services.AddScoped<IQueryHandler<GetGeneralCapitalQuery, GeneralCapitalView>>(sp => sp.GetRequiredService<GeneralBalanceHandlers>());
        services.AddScoped<IQueryHandler<GetGeneralBalanceVariationQuery, GeneralBalanceVariationView>>(sp => sp.GetRequiredService<GeneralBalanceHandlers>());

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « balance ».</summary>
    public static async Task InitializeBalanceModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<BalanceDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
