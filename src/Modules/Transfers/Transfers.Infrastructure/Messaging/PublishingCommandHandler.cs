using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Transfers.Application.Abstractions;

namespace Transfers.Infrastructure.Messaging;

/// <summary>
/// Enveloppe la transaction commune : si la commande réussit (donc APRÈS la validation de la transaction),
/// envoie les notifications temps réel gardées pendant la requête ; sinon les oublie.
/// Le décorateur de transaction partagé n'est pas modifié : celui-ci se place autour de lui.
/// </summary>
internal sealed class PublishingCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    TransferChangeBuffer buffer,
    ITransferChangePublisher publisher)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.Handle(command, cancellationToken);
        var changes = buffer.TakeAll();
        if (result.IsSuccess && changes.Count > 0)
        {
            await publisher.PublishAsync(changes, cancellationToken);
        }

        return result;
    }
}

/// <summary>Même chose pour une commande sans valeur de retour.</summary>
internal sealed class PublishingCommandHandler<TCommand>(
    ICommandHandler<TCommand> inner,
    TransferChangeBuffer buffer,
    ITransferChangePublisher publisher)
    : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.Handle(command, cancellationToken);
        var changes = buffer.TakeAll();
        if (result.IsSuccess && changes.Count > 0)
        {
            await publisher.PublishAsync(changes, cancellationToken);
        }

        return result;
    }
}

/// <summary>Sans temps réel (ex. tâche de fond, tests) : rien n'est envoyé.</summary>
internal sealed class NullTransferChangePublisher : ITransferChangePublisher
{
    public Task PublishAsync(IReadOnlyList<TransferChanged> changes, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal static class PublishingRegistration
{
    /// <summary>Transaction commune + notifications après validation (commandes des Transferts).</summary>
    public static IServiceCollection AddPublishingTransactional<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(sp =>
            new PublishingCommandHandler<TCommand, TResponse>(
                new SharedTransactionCommandHandler<TCommand, TResponse>(
                    sp.GetRequiredService<THandler>(),
                    sp.GetRequiredService<SharedConnection>(),
                    sp.GetServices<ISharedTransactionParticipant>()),
                sp.GetRequiredService<TransferChangeBuffer>(),
                sp.GetRequiredService<ITransferChangePublisher>()));
        return services;
    }

    /// <summary>Même chose pour une commande sans valeur de retour.</summary>
    public static IServiceCollection AddPublishingTransactional<TCommand, THandler>(this IServiceCollection services)
        where TCommand : ICommand
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand>>(sp =>
            new PublishingCommandHandler<TCommand>(
                new SharedTransactionCommandHandler<TCommand>(
                    sp.GetRequiredService<THandler>(),
                    sp.GetRequiredService<SharedConnection>(),
                    sp.GetServices<ISharedTransactionParticipant>()),
                sp.GetRequiredService<TransferChangeBuffer>(),
                sp.GetRequiredService<ITransferChangePublisher>()));
        return services;
    }
}
