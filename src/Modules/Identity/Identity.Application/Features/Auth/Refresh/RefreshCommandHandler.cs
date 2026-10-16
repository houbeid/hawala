using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Auth.Refresh;

public sealed class RefreshCommandHandler(
    IUserAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    IAccessTokenIssuer accessTokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    AuthSettings settings)
    : ICommandHandler<RefreshCommand, AuthTokens>
{
    public async Task<Result<AuthTokens>> Handle(RefreshCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return IdentityErrors.RefreshToken.Invalid;
        }

        var token = await refreshTokens.GetByHashAsync(RefreshToken.HashOf(command.RefreshToken), cancellationToken);
        if (token is null)
        {
            return IdentityErrors.RefreshToken.Invalid;
        }

        var now = clock.UtcNow;

        var account = await accounts.GetByIdAsync(token.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.RefreshToken.Invalid;
        }

        if (account.Status == AccountStatus.Blocked)
        {
            token.Revoke(now, RefreshTokenRevocationReason.AccountBlocked);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return IdentityErrors.Auth.AccountBlocked;
        }

        var rotation = token.Rotate(now, settings.RefreshTokenLifetime);
        if (rotation.IsFailure)
        {
            if (rotation.Error == IdentityErrors.RefreshToken.Reused)
            {
                // Deux personnes ont le même jeton : on ferme toute la session, voleur compris.
                await RevokeFamilyAsync(token.FamilyId, now, cancellationToken);
            }

            return rotation.Error;
        }

        refreshTokens.Add(rotation.Value.Token);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokens(
            accessTokens.Issue(account),
            rotation.Value.PlainValue,
            rotation.Value.Token.ExpiresAt,
            account.MustChangePassword);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var family = await refreshTokens.GetActiveByFamilyAsync(familyId, cancellationToken);
        foreach (var member in family)
        {
            member.Revoke(now, RefreshTokenRevocationReason.ReuseDetected);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}