using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// Exécute une commande dans une transaction PostgreSQL : tout est validé si la commande réussit,
/// tout est annulé si elle échoue ou lève une exception.
/// Nécessaire quand ASP.NET Identity (qui enregistre lui-même) et nos tables changent ensemble.
/// À ne PAS utiliser pour Login et Refresh : un échec doit y rester enregistré
/// (compteur d'échecs, révocation après réutilisation d'un jeton).
/// </summary>
internal sealed class TransactionalCommandHandler<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    IdentityModuleDbContext db)
    : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var result = await inner.Handle(command, cancellationToken);

        if (result.IsSuccess)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        // En cas d'échec, la transaction non validée est annulée à sa libération.
        return result;
    }
}