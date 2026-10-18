using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Users.UnblockUser;

/// <summary>
/// Débloque un compte avec un nouveau mot de passe temporaire (à changer à la connexion) et lève
/// le verrouillage après échecs. Doit être exécuté dans une transaction : ASP.NET Identity enregistre
/// le nouveau mot de passe lui-même.
/// POINT OUVERT : l'audit « qui a débloqué ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class UnblockUserCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    ITemporaryPasswordGenerator passwords,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<UnblockUserCommand, UnblockedUser>
{
    public async Task<Result<UnblockedUser>> Handle(UnblockUserCommand command, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var unblocked = account.Unblock();
        if (unblocked.IsFailure)
        {
            return unblocked.Error;
        }

        var temporaryPassword = passwords.Generate();
        var reset = await credentials.ResetPasswordAsync(account.Id, temporaryPassword, cancellationToken);
        if (reset.IsFailure)
        {
            return reset.Error;
        }

        account.PasswordResetBySupervisor();

        // Normalement déjà révoqués au blocage ; par sécurité, aucune ancienne session ne survit au déblocage.
        var now = clock.UtcNow;
        foreach (var token in await refreshTokens.GetActiveByUserAsync(account.Id, cancellationToken))
        {
            token.Revoke(now, RefreshTokenRevocationReason.PasswordReset);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new UnblockedUser(temporaryPassword);
    }
}
