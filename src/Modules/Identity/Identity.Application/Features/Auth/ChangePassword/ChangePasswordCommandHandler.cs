using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Auth.ChangePassword;

/// <summary>
/// Changement du mot de passe par l'agent lui-même (obligatoire à la première connexion).
/// Toutes ses sessions sont fermées, puis une nouvelle est ouverte pour l'appareil en cours :
/// il continue sans se reconnecter, mais un autre appareil qui connaissait l'ancien mot de passe est déconnecté.
/// </summary>
public sealed class ChangePasswordCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    IRefreshTokenRepository refreshTokens,
    IAccessTokenIssuer accessTokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    AuthSettings settings,
    IClientInfo client)
    : ICommandHandler<ChangePasswordCommand, AuthTokens>
{
    public async Task<Result<AuthTokens>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.CurrentPassword) || string.IsNullOrEmpty(command.NewPassword))
        {
            return IdentityErrors.Password.Empty;
        }

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        if (account.Status == AccountStatus.Blocked)
        {
            return IdentityErrors.Auth.AccountBlocked;
        }

        var change = await credentials.ChangePasswordAsync(
            account.Id, command.CurrentPassword, command.NewPassword, cancellationToken);
        if (change.IsFailure)
        {
            return change.Error;
        }

        var now = clock.UtcNow;
        account.PasswordChanged();

        foreach (var token in await refreshTokens.GetActiveByUserAsync(account.Id, cancellationToken))
        {
            token.Revoke(now, RefreshTokenRevocationReason.PasswordChanged);
        }

        var refresh = RefreshToken.IssueForNewSession(account.Id, now, settings.RefreshTokenLifetime, client.Device);
        refreshTokens.Add(refresh.Token);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokens(
            accessTokens.Issue(account, refresh.Token.FamilyId),
            refresh.PlainValue,
            refresh.Token.ExpiresAt,
            account.MustChangePassword,
            refresh.Token.FamilyId);
    }
}