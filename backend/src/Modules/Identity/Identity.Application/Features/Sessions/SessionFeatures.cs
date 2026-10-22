using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Application.Abstractions;
using Identity.Application.Features.Users;
using Identity.Domain;
using Identity.Domain.RefreshTokens;

namespace Identity.Application.Features.Sessions;

/// <summary>Une session ouverte, telle que l'écran « Appareils connectés » l'affiche.</summary>
/// <param name="Device">Navigateur ou téléphone (User-Agent du dernier rafraîchissement) ; null s'il est inconnu.</param>
/// <param name="Current">Vrai pour la session de la requête (« cet appareil »).</param>
public sealed record SessionView(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? Device,
    string? IpAddress,
    bool Current);

/// <param name="ActorId">Compte connecté, lu dans le JWT.</param>
/// <param name="UserId">Compte dont on lit les sessions (lui-même, ou un compte qu'il gère).</param>
/// <param name="CurrentSessionId">Session de la requête (« sid » du jeton), pour marquer « cet appareil ».</param>
public sealed record ListSessionsQuery(Guid ActorId, Guid UserId, Guid? CurrentSessionId) : IQuery<IReadOnlyList<SessionView>>;

/// <param name="ActorId">Compte connecté, lu dans le JWT.</param>
/// <param name="UserId">Propriétaire de la session (lui-même, ou un compte qu'il gère).</param>
public sealed record CloseSessionCommand(Guid ActorId, Guid UserId, Guid SessionId) : ICommand;

/// <summary>
/// Règles validées le 22 octobre 2026 : chaque compte voit et ferme ses propres sessions ; le Superviseur celles de tous ;
/// un compte qui a users.manage, celles des comptes de son agence (comme pour le blocage). Fermer une session révoque
/// tous ses jetons : son jeton d'accès est refusé à la requête suivante (contrôle du « sid »).
/// Une session fermée par quelqu'un d'autre que son propriétaire est tracée dans l'audit.
/// </summary>
public sealed class SessionHandlers(
    IUserAccountRepository accounts,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    IAuditLog audit,
    IClock clock)
    : IQueryHandler<ListSessionsQuery, IReadOnlyList<SessionView>>,
      ICommandHandler<CloseSessionCommand>
{
    public const string AuditAction = "Identity.Session.Revoked";

    public async Task<Result<IReadOnlyList<SessionView>>> Handle(ListSessionsQuery query, CancellationToken cancellationToken)
    {
        var allowed = await EnsureAllowedAsync(query.ActorId, query.UserId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var sessions = await refreshTokens.ListOpenSessionsAsync(query.UserId, clock.UtcNow, cancellationToken);
        return Result.Success<IReadOnlyList<SessionView>>(
            [.. sessions.Select(s => new SessionView(
                s.SessionId, s.StartedAt, s.LastUsedAt, s.ExpiresAt, s.UserAgent, s.IpAddress, s.SessionId == query.CurrentSessionId))]);
    }

    public async Task<Result> Handle(CloseSessionCommand command, CancellationToken cancellationToken)
    {
        var allowed = await EnsureAllowedAsync(command.ActorId, command.UserId, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        var tokens = (await refreshTokens.GetActiveByFamilyAsync(command.SessionId, cancellationToken))
            .Where(t => t.UserId == command.UserId)
            .ToList();
        if (tokens.Count == 0)
        {
            return IdentityErrors.Session.NotFound;
        }

        var now = clock.UtcNow;
        foreach (var token in tokens)
        {
            token.Revoke(now, RefreshTokenRevocationReason.SessionClosed);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (command.ActorId != command.UserId)
        {
            var recorded = await audit.RecordAsync(
                new AuditRecord(
                    AuditAction, "UserAccount", command.UserId, command.ActorId, allowed.Value,
                    Changes: [new AuditChange($"session:{command.SessionId}", "open", "closed")]),
                cancellationToken);
            if (recorded.IsFailure)
            {
                return recorded.Error;
            }
        }

        return Result.Success();
    }

    /// <summary>Soi-même, ou un compte qu'il gère (Superviseur : tous ; users.manage : son agence). Renvoie l'agence du compte.</summary>
    private async Task<Result<Guid?>> EnsureAllowedAsync(Guid actorId, Guid userId, CancellationToken cancellationToken)
    {
        var account = await accounts.GetByIdAsync(userId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        if (actorId != userId)
        {
            var manager = await AccountManager.LoadAsync(accounts, actorId, cancellationToken);
            if (!manager.CanManage(account))
            {
                return IdentityErrors.Account.NotFound;
            }
        }

        return account.AgencyId;
    }
}
