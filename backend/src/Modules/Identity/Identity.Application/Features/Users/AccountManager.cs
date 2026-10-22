using Identity.Application.Abstractions;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Application.Features.Users;

/// <summary>
/// Celui qui gère les comptes (auteur de la commande, lu dans le JWT) et ce qu'il a le droit de toucher.
/// Règles validées le 21 octobre 2026 : un Superviseur gère tous les comptes ; un autre rôle qui a reçu
/// users.manage ne gère que les comptes de SON agence (jamais un Superviseur, qui n'a pas d'agence),
/// sans changer de rôle, d'agence ni de permissions. Un compte hors de son périmètre lui est invisible (404).
/// </summary>
internal sealed record AccountManager(Guid Id, bool IsSupervisor, Guid? AgencyId)
{
    /// <summary>Auteur introuvable (ne devrait pas arriver après la validation de la session) : il ne gère rien.</summary>
    public static async Task<AccountManager> LoadAsync(
        IUserAccountRepository accounts, Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await accounts.GetByIdAsync(actorId, cancellationToken);

        return actor is null
            ? new AccountManager(actorId, IsSupervisor: false, AgencyId: null)
            : new AccountManager(actor.Id, actor.Role == RoleTemplate.Supervisor, actor.AgencyId);
    }

    public bool CanManage(UserAccount target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return IsSupervisor
            || (AgencyId is { } agencyId && target.Role != RoleTemplate.Supervisor && target.AgencyId == agencyId);
    }
}
