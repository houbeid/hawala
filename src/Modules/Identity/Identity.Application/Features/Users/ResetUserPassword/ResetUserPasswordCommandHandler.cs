using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Users.ResetUserPassword;

/// <summary>
/// Règles validées : nouveau mot de passe temporaire généré (à changer à la connexion), verrouillage levé,
/// toutes les sessions coupées. Possible sur un compte bloqué et sur un autre Superviseur.
/// Un compte bloqué reste bloqué : la réinitialisation ne débloque pas (le déblocage est une action à part).
/// Doit être exécuté dans une transaction : ASP.NET Identity enregistre le nouveau mot de passe lui-même.
/// Tracé dans l'audit (« Identity.Account.PasswordReset ») : le fait seulement, jamais le mot de passe.
/// </summary>
public sealed class ResetUserPasswordCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    ITemporaryPasswordGenerator passwords,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<ResetUserPasswordCommand, ResetPassword>
{
    public async Task<Result<ResetPassword>> Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        // Son propre mot de passe se change par /auth/change-password, qui demande l'ancien.
        if (command.ActorId == command.UserId)
        {
            return IdentityErrors.Account.CannotResetOwnPassword;
        }

        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        // Hors de son périmètre, le compte n'existe pas pour lui (même réponse qu'un id inconnu).
        if (account is null || !manager.CanManage(account))
        {
            return IdentityErrors.Account.NotFound;
        }

        var temporaryPassword = passwords.Generate();
        var reset = await credentials.ResetPasswordAsync(account.Id, temporaryPassword, cancellationToken);
        if (reset.IsFailure)
        {
            return reset.Error;
        }

        account.PasswordResetBySupervisor();

        var now = clock.UtcNow;
        foreach (var token in await refreshTokens.GetActiveByUserAsync(account.Id, cancellationToken))
        {
            token.Revoke(now, RefreshTokenRevocationReason.PasswordReset);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await AccountAudit.RecordAsync(
            audit, AccountAudit.PasswordReset, account, command.ActorId, [], cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return new ResetPassword(temporaryPassword);
    }
}
