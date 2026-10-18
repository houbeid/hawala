using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Application.Abstractions;
using Identity.Domain;
using Organization.Contracts;

namespace Identity.Application.Features.Users.ChangeUserAgency;

/// <summary>
/// Déplace un agent : son jeton en cours est refusé (SecurityVersion) et le prochain rafraîchissement
/// lui donne son nouveau périmètre. Ses anciennes opérations restent attachées à l'agence où il les a faites.
/// Pas de transaction ni de verrou : un Superviseur n'a pas d'agence, le nombre de Superviseurs n'est pas en jeu.
/// Pas de règle « soi-même » : un Superviseur ne peut de toute façon pas changer d'agence.
/// POINT OUVERT : l'audit « qui a déplacé ce compte » arrivera avec le module Audit.
/// </summary>
public sealed class ChangeUserAgencyCommandHandler(
    IUserAccountRepository accounts,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeUserAgencyCommand>
{
    public async Task<Result> Handle(ChangeUserAgencyCommand command, CancellationToken cancellationToken)
    {
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

        var changed = account.ChangeAgency(agencyId);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
