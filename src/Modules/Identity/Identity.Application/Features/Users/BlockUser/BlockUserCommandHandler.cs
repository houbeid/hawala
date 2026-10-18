using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Users.BlockUser;

/// <summary>
/// Bloque un compte et coupe toutes ses sessions : le jeton d'accès est refusé dès la requête suivante
/// (SecurityVersion), et plus aucun refresh token ne fonctionne.
/// Doit être exécuté dans une transaction (voir l'enregistrement en Infrastructure) : le verrou posé
/// par le comptage des Superviseurs ne tient que jusqu'à sa fin.
/// POINT OUVERT : l'audit « qui a bloqué ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class BlockUserCommandHandler(
    IUserAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<BlockUserCommand>
{
    public async Task<Result> Handle(BlockUserCommand command, CancellationToken cancellationToken)
    {
        if (command.ActorId == command.UserId)
        {
            return IdentityErrors.Account.CannotBlockSelf;
        }

        // Le verrou d'abord, la lecture du compte ensuite : si deux Superviseurs se bloquent en même temps,
        // le second attend la fin du premier et relit alors un état à jour.
        var activeSupervisorCount = await accounts.CountActiveSupervisorsForUpdateAsync(cancellationToken);

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var blocked = account.Block(activeSupervisorCount);
        if (blocked.IsFailure)
        {
            return blocked.Error;
        }

        var now = clock.UtcNow;
        foreach (var token in await refreshTokens.GetActiveByUserAsync(account.Id, cancellationToken))
        {
            token.Revoke(now, RefreshTokenRevocationReason.AccountBlocked);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
