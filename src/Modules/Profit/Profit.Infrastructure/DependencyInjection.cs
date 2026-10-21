using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Profit.Application.Abstractions;
using Profit.Application.Features;
using Profit.Infrastructure.Persistence;

namespace Profit.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre le Bénéfice. Appelé une fois par l'API dans Program.cs, après les Transferts.</summary>
    public static IServiceCollection AddProfitModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Connexion propre : le Bénéfice ne fait que lire les transferts et garder ses photos.
        services.AddDbContext<ProfitDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ProfitDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ProfitDbContext>());
        services.AddScoped<IProfitSnapshotRepository, ProfitSnapshotRepository>();
        services.AddScoped<Profit.Contracts.IProfitHistory, ProfitHistory>();

        services.AddScoped<ProfitReportHandlers>();
        services.AddScoped<IQueryHandler<GetProfitReportQuery, ProfitReport>>(sp => sp.GetRequiredService<ProfitReportHandlers>());
        services.AddScoped<IQueryHandler<GetProfitDayQuery, ProfitDayDetail>>(sp => sp.GetRequiredService<ProfitReportHandlers>());
        services.AddScoped<ICommandHandler<SaveProfitSnapshotCommand, ProfitSnapshotView>>(sp => sp.GetRequiredService<ProfitReportHandlers>());
        services.AddScoped<IQueryHandler<ListProfitSnapshotsQuery, IReadOnlyList<ProfitSnapshotView>>>(sp => sp.GetRequiredService<ProfitReportHandlers>());

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « profit ».</summary>
    public static async Task InitializeProfitModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ProfitDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
