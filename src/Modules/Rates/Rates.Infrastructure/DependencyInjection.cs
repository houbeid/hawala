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

        services.AddDbContext<RatesDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", RatesDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RatesDbContext>());
        services.AddScoped<IDailyRateRepository, DailyRateRepository>();
        services.AddScoped<Rates.Contracts.IDailyRates, DailyRates>();

        services.AddScoped<ICommandHandler<SetDailyRateCommand>, SetDailyRateCommandHandler>();
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
