using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Transfers.Application.Abstractions;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Infrastructure.Persistence;
using Transfers.Infrastructure.Persistence.Repositories;
using Transfers.Infrastructure.Security;

namespace Transfers.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Enregistre les Transferts. Appelé une fois par l'API dans Program.cs, après le grand livre, la Trésorerie et les Taux.</summary>
    public static IServiceCollection AddTransfersModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Hawala")
            ?? throw new InvalidOperationException("ConnectionStrings:Hawala absente de la configuration.");

        services.TryAddSingleton<IClock, SystemClock>();

        // Même connexion que le grand livre : un transfert et son écriture sont enregistrés ensemble.
        services.AddSharedConnection(connectionString);
        services.AddDbContext<TransfersDbContext>((sp, options) =>
            options.UseNpgsql(
                sp.GetRequiredService<SharedConnection>().Connection,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", TransfersDbContext.Schema)));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TransfersDbContext>());
        services.AddScoped<ISharedTransactionParticipant>(sp => sp.GetRequiredService<TransfersDbContext>());
        services.AddScoped<ITransferRepository, TransferRepository>();
        services.AddScoped<ITransferReferenceGenerator, TransferReferenceGenerator>();
        services.AddSingleton<IWithdrawalCodeHasher, Pbkdf2WithdrawalCodeHasher>();

        // Écrit dans deux schémas (transfers + ledger) : une seule transaction commune.
        services.AddSharedTransactional<CreateTransferCommand, CreatedTransfer, CreateTransferCommandHandler>();

        return services;
    }

    /// <summary>Au démarrage : migrations du schéma « transfers ».</summary>
    public static async Task InitializeTransfersModuleAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TransfersDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
