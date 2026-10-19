using System.Data;
using System.Data.Common;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Hawala.SharedInfrastructure.Persistence;

/// <summary>
/// Une commande = une transaction, même sur plusieurs modules (option A validée).
/// Ouvre la connexion partagée, démarre UNE transaction, y rattache tous les DbContext des modules d'argent,
/// exécute la commande, puis valide si elle réussit. Échec ou exception : rien n'est enregistré, dans aucun schéma.
/// </summary>
public sealed class SharedTransactionCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    SharedConnection connection,
    IEnumerable<ISharedTransactionParticipant> participants)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        await using var scope = await SharedTransactionScope.BeginAsync(connection, participants, cancellationToken);

        var result = await inner.Handle(command, cancellationToken);

        if (result.IsSuccess)
        {
            await scope.CommitAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>Même chose pour une commande sans valeur de retour.</summary>
public sealed class SharedTransactionCommandHandler<TCommand>(
    ICommandHandler<TCommand> inner,
    SharedConnection connection,
    IEnumerable<ISharedTransactionParticipant> participants)
    : ICommandHandler<TCommand>
    where TCommand : ICommand
{
    public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
    {
        await using var scope = await SharedTransactionScope.BeginAsync(connection, participants, cancellationToken);

        var result = await inner.Handle(command, cancellationToken);

        if (result.IsSuccess)
        {
            await scope.CommitAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>La transaction commune et ses participants ; annule tout à la libération si elle n'a pas été validée.</summary>
internal sealed class SharedTransactionScope : IAsyncDisposable
{
    private readonly DbTransaction _transaction;
    private readonly List<ISharedTransactionParticipant> _participants;

    private SharedTransactionScope(DbTransaction transaction, List<ISharedTransactionParticipant> participants)
    {
        _transaction = transaction;
        _participants = participants;
    }

    public static async Task<SharedTransactionScope> BeginAsync(
        SharedConnection connection,
        IEnumerable<ISharedTransactionParticipant> participants,
        CancellationToken cancellationToken)
    {
        if (connection.Connection.State != ConnectionState.Open)
        {
            await connection.Connection.OpenAsync(cancellationToken);
        }

        var transaction = await connection.Connection.BeginTransactionAsync(cancellationToken);
        var list = participants.ToList();
        foreach (var participant in list)
        {
            participant.UseSharedTransaction(transaction);
        }

        return new SharedTransactionScope(transaction, list);
    }

    public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        // Les DbContext se détachent ; une transaction non validée est annulée par sa libération.
        foreach (var participant in _participants)
        {
            participant.UseSharedTransaction(null);
        }

        await _transaction.DisposeAsync();
    }
}

public static class SharedTransactionRegistration
{
    /// <summary>
    /// Enregistre une commande d'un module d'argent dans la transaction commune :
    /// le handler réel, enveloppé par <see cref="SharedTransactionCommandHandler{TCommand, TResponse}"/>.
    /// </summary>
    public static IServiceCollection AddSharedTransactional<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(sp =>
            new SharedTransactionCommandHandler<TCommand, TResponse>(
                sp.GetRequiredService<THandler>(),
                sp.GetRequiredService<SharedConnection>(),
                sp.GetServices<ISharedTransactionParticipant>()));
        return services;
    }

    /// <summary>Même chose pour une commande sans valeur de retour.</summary>
    public static IServiceCollection AddSharedTransactional<TCommand, THandler>(this IServiceCollection services)
        where TCommand : ICommand
        where THandler : class, ICommandHandler<TCommand>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand>>(sp =>
            new SharedTransactionCommandHandler<TCommand>(
                sp.GetRequiredService<THandler>(),
                sp.GetRequiredService<SharedConnection>(),
                sp.GetServices<ISharedTransactionParticipant>()));
        return services;
    }
}
