using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Auth.Logout;

public sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<LogoutCommand>
{
    /// <summary>
    /// Toujours un succès, même avec un jeton absent ou inconnu : se déconnecter deux fois
    /// n'est pas une erreur, et la réponse ne doit rien révéler sur le jeton.
    /// </summary>
    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken))
        {
            return Result.Success();
        }

        var token = await refreshTokens.GetByHashAsync(RefreshToken.HashOf(command.RefreshToken), cancellationToken);
        if (token is null)
        {
            return Result.Success();
        }

        token.Revoke(clock.UtcNow, RefreshTokenRevocationReason.Logout);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}