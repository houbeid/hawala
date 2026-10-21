using Cash.Application.Abstractions;
using Cash.Application.Features.Movements;
using Cash.Application.Features.Movements.CreateCashMovement;
using Cash.Application.Features.Movements.GetCashMovement;
using Cash.Application.Features.Movements.ListCashMovements;
using Cash.Infrastructure.Persistence;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cash.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre la Caisse. Appelé une fois par l'API dans Program.cs, après le grand livre, la Trésorerie et les Tiers.</summary>
    public static IServiceCollection AddCashModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Même connexion que le grand livre : un mouvement et son écriture sont enregistrés ensemble.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<CashDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", CashDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CashDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<CashDbContext>());
        services.AddScoped<ICashMovementRepository, CashMovementRepository>();
        services.AddScoped<CashMovementViews>();

        services.AddScoped<IQueryHandler<ListCashMovementsQuery, CashMovementList>, ListCashMovementsQueryHandler>();
        services.AddScoped<IQueryHandler<GetCashMovementQuery, CashMovementView>, GetCashMovementQueryHandler>();

        // Écrit dans deux schémas (cash + ledger) : une seule transaction commune.
        services.AddSharedTransactional<CreateCashMovementCommand, CreatedCashMovement, CreateCashMovementCommandHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « cash ».</summary>
    public static async Task InitializeCashModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<CashDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
