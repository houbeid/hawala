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

        // Connexion propre : le Bilan lit les autres modules et garde son capital et ses photos.
        services.AddDbContext<BalanceDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", BalanceDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<BalanceDbContext>());
        services.AddScoped<ICapitalRepository, CapitalRepository>();
        services.AddScoped<IBalanceSnapshotRepository, BalanceSnapshotRepository>();

        services.AddScoped<BalanceHandlers>();
        services.AddScoped<IQueryHandler<GetBalanceQuery, BalanceView>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<ICommandHandler<SaveBalanceCommand, BalanceSnapshotView>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<IQueryHandler<ListBalanceSnapshotsQuery, IReadOnlyList<BalanceSnapshotView>>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<ICommandHandler<SetInitialCapitalCommand>>(sp => sp.GetRequiredService<BalanceHandlers>());
        services.AddScoped<IQueryHandler<GetCapitalQuery, CapitalView>>(sp => sp.GetRequiredService<BalanceHandlers>());

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
