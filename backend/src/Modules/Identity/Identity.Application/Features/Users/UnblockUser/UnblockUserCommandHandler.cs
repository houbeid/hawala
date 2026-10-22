using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Users.UnblockUser;

/// <summary>
/// Débloque un compte avec un nouveau mot de passe temporaire (à changer à la connexion) et lève
/// le verrouillage après échecs. Doit être exécuté dans une transaction : ASP.NET Identity enregistre
/// le nouveau mot de passe lui-même.
/// Tracé dans l'audit (« Identity.Account.Unblocked ») dans la même transaction ; jamais le mot de passe temporaire.
/// </summary>
public sealed class UnblockUserCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    ITemporaryPasswordGenerator passwords,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<UnblockUserCommand, UnblockedUser>
{
    public async Task<Result<UnblockedUser>> Handle(UnblockUserCommand command, CancellationToken cancellationToken)
    {
        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        // Hors de son périmètre, le compte n'existe pas pour lui (même réponse qu'un id inconnu).
        if (account is null || !manager.CanManage(account))
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

        var audited = await AccountAudit.RecordAsync(
            audit, AccountAudit.Unblocked, account, command.ActorId, [AccountAudit.Change("status", AccountStatus.Blocked, AccountStatus.Active)], cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return new UnblockedUser(temporaryPassword);
    }
}
