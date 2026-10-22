using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Organization.Contracts;

namespace Identity.Application.Features.Users.ChangeUserAgency;

/// <summary>
/// Déplace un agent : son jeton en cours est refusé (SecurityVersion) et le prochain rafraîchissement
/// lui donne son nouveau périmètre. Ses anciennes opérations restent attachées à l'agence où il les a faites.
/// Pas de verrou : un Superviseur n'a pas d'agence, le nombre de Superviseurs n'est pas en jeu. Transaction commune pour l'audit.
/// Pas de règle « soi-même » : un Superviseur ne peut de toute façon pas changer d'agence.
/// Tracé dans l'audit (« Identity.Account.AgencyChanged ») dans la même transaction, s'il change d'agence.
/// </summary>
public sealed class ChangeUserAgencyCommandHandler(
    IUserAccountRepository accounts,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<ChangeUserAgencyCommand>
{
    public async Task<Result> Handle(ChangeUserAgencyCommand command, CancellationToken cancellationToken)
    {
        // Règle validée le 21 octobre 2026 : déplacer un compte reste réservé au rôle Superviseur.
        var manager = await AccountManager.LoadAsync(accounts, command.ActorId, cancellationToken);
        if (!manager.IsSupervisor)
        {
            return IdentityErrors.Account.SupervisorOnly;
        }

        if (command.AgencyId is not { } agencyId || agencyId == Guid.Empty)
        {
            return IdentityErrors.Account.AgencyRequired;
        }

        if (await agencies.GetByIdAsync(agencyId, cancellationToken) is null)
        {
            return IdentityErrors.Account.AgencyNotFound;
        }

        var account = await accounts.GetByIdAsync(command.UserId, cancellationToken);
        if (account is null)
        {
            return IdentityErrors.Account.NotFound;
        }

        var agencyBefore = account.AgencyId;
        var changed = account.ChangeAgency(agencyId);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (account.AgencyId == agencyBefore)
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await AccountAudit.RecordAsync(
            audit, AccountAudit.AgencyChanged, account, command.ActorId, [AccountAudit.Change("agencyId", agencyBefore, account.AgencyId)], cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }
}
