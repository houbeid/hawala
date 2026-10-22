using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rates.Application.Abstractions;
using Rates.Application.Features;
using Rates.Application.Features.GetDailyRate;
using Rates.Application.Features.SetDailyRate;
using Rates.Infrastructure.Persistence;

namespace Rates.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre le module Taux. Appelé une fois par l'API dans Program.cs.</summary>
    public static IServiceCollection AddRatesModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Phase 6 (tour 7d) : connexion partagée de la requête, pour que l'action et sa ligne d'audit
        // soient dans la même transaction.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<RatesDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", RatesDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RatesDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<RatesDbContext>());
        services.AddScoped<IDailyRateRepository, DailyRateRepository>();
        services.AddScoped<Rates.Contracts.IDailyRates, DailyRates>();

        services.AddSharedTransactional<SetDailyRateCommand, SetDailyRateCommandHandler>();
        services.AddScoped<IQueryHandler<GetDailyRateQuery, DailyRateView>, GetDailyRateQueryHandler>();
        services.AddScoped<IQueryHandler<ListDailyRatesQuery, IReadOnlyList<DailyRateView>>, ListDailyRatesQueryHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « rates ».</summary>
    public static async Task InitializeRatesModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<RatesDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
