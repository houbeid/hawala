using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Transfers.Application.Abstractions;
using Hawala.SharedKernel.Messaging;
using Transfers.Application.Features.Transfers;
using Transfers.Application.Features.Transfers.CancelTransfer;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Application.Features.Transfers.DeliverTransfer;
using Transfers.Application.Features.Transfers.DeliverWithoutCode;
using Transfers.Application.Features.Transfers.NewCode;
using Transfers.Application.Features.Transfers.UnlockCode;
using Transfers.Application.Features.Transfers.EditTransferAmounts;
using Transfers.Application.Features.Transfers.EditTransferTexts;
using Transfers.Application.Features.Transfers.GetTransfer;
using Transfers.Application.Features.Transfers.GetTransferHistory;
using Transfers.Application.Features.Transfers.GetTransferSummary;
using Transfers.Application.Features.Transfers.ListTransfers;
using Transfers.Infrastructure.Messaging;
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
        services.AddScoped<TransferViews>();
        services.AddScoped<Transfers.Contracts.ITransferMonitor, TransferMonitor>();
        services.AddScoped<Transfers.Contracts.ITransferProfits, TransferProfits>();

        // Temps réel : notifications gardées pendant la requête, envoyées après validation. Sans hub (tâche de fond) : rien.
        services.AddScoped<TransferChangeBuffer>();
        services.TryAddScoped<ITransferChangePublisher, NullTransferChangePublisher>();
        services.AddScoped<DeliveryPosting>();
        services.AddScoped<CreationPosting>();

        services.AddScoped<IQueryHandler<ListTransfersQuery, TransferList>, ListTransfersQueryHandler>();
        services.AddScoped<IQueryHandler<GetTransferQuery, TransferView>, GetTransferQueryHandler>();
        services.AddScoped<IQueryHandler<GetTransferSummaryQuery, TransferSummary>, GetTransferSummaryQueryHandler>();
        services.AddScoped<IQueryHandler<GetTransferHistoryQuery, IReadOnlyList<TransferEventView>>, GetTransferHistoryQueryHandler>();

        // Écrit dans deux schémas (transfers + ledger) : une seule transaction commune, puis notifications temps réel.
        services.AddPublishingTransactional<CreateTransferCommand, CreatedTransfer, CreateTransferCommandHandler>();

        // Livraison : transfert + grand livre ; un code faux est aussi enregistré (succès technique qui porte le refus).
        services.AddPublishingTransactional<DeliverTransferCommand, DeliveryOutcome, DeliverTransferCommandHandler>();

        // Annulation : transfert + contre-passation au grand livre, ensemble.
        services.AddPublishingTransactional<CancelTransferCommand, CancelTransferCommandHandler>();

        // Actions sur le code : transfert verrouillé pendant la commande (et grand livre pour la livraison sans code).
        services.AddPublishingTransactional<UnlockCodeCommand, UnlockCodeCommandHandler>();
        services.AddPublishingTransactional<EditTransferTextsCommand, EditTransferTextsCommandHandler>();
        services.AddPublishingTransactional<EditTransferAmountsCommand, EditTransferAmountsCommandHandler>();
        services.AddPublishingTransactional<NewCodeCommand, NewWithdrawalCode, NewCodeCommandHandler>();
        services.AddPublishingTransactional<DeliverWithoutCodeCommand, DeliveredTransfer, DeliverWithoutCodeCommandHandler>();

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
