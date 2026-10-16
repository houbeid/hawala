using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Auth.Login;

public sealed class LoginCommandHandler(
    IUserAccountRepository accounts,
    ICredentialStore credentials,
    IRefreshTokenRepository refreshTokens,
    IAccessTokenIssuer accessTokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    AuthSettings settings)
    : ICommandHandler<LoginCommand, AuthTokens>
{
    public async Task<Result<AuthTokens>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var login = LoginIdentifier.Parse(command.Login);
        if (login.IsFailure)
        {
            return login.Error;
        }

        if (string.IsNullOrEmpty(command.Password))
        {
            return IdentityErrors.Password.Empty;
        }

        var account = await accounts.GetByLoginAsync(login.Value, cancellationToken);
        if (account is null)
        {
            await credentials.SimulatePasswordCheckAsync(command.Password, cancellationToken);
            return IdentityErrors.Auth.InvalidCredentials;
        }

        var check = await credentials.CheckPasswordAsync(account.Id, command.Password, cancellationToken);
        switch (check)
        {
            case PasswordCheckResult.LockedOut:
                return IdentityErrors.Auth.LockedOut;
            case PasswordCheckResult.Invalid:
                return IdentityErrors.Auth.InvalidCredentials;
        }

        // Vérifié seulement après le bon mot de passe : sinon on révélerait qu'un compte bloqué existe.
        if (account.Status == AccountStatus.Blocked)
        {
            return IdentityErrors.Auth.AccountBlocked;
        }

        var now = clock.UtcNow;
        var refresh = RefreshToken.IssueForNewSession(account.Id, now, settings.RefreshTokenLifetime);
        refreshTokens.Add(refresh.Token);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokens(
            accessTokens.Issue(account),
            refresh.PlainValue,
            refresh.Token.ExpiresAt,
            account.MustChangePassword);
    }
}