using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Organization.Application.Abstractions;
using Organization.Domain;
using Organization.Domain.Agencies;

namespace Organization.Application.Agencies.ChangeAgencyColor;

/// <summary>
/// Règle validée : chaque agence a sa propre couleur. Vérifiée ici car une agence ne connaît pas les autres.
/// Les sessions des agents ne sont pas touchées : /auth/me relit la couleur à chaque appel.
/// POINT OUVERT : pas d'index unique en base sur la couleur. Si deux Superviseurs donnent au même instant
/// la même couleur aux deux agences, les deux contrôles peuvent passer. Accepté (deux agences, un ou deux
/// Superviseurs) ; ajouter un index unique avec une migration si le nombre d'agences grandit.
/// POINT OUVERT : l'audit « qui a changé la couleur » arrivera avec le module Audit.
/// </summary>
public sealed class ChangeAgencyColorCommandHandler(
    IAgencyRepository agencies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ChangeAgencyColorCommand>
{
    public async Task<Result> Handle(ChangeAgencyColorCommand command, CancellationToken cancellationToken)
    {
        var color = AgencyColor.Create(command.Color);
        if (color.IsFailure)
        {
            return color.Error;
        }

        // Une seule lecture (deux agences) : sert à trouver l'agence et à comparer avec les autres.
        var all = await agencies.GetAllAsync(cancellationToken);

        var agency = all.FirstOrDefault(a => a.Id == command.AgencyId);
        if (agency is null)
        {
            return OrganizationErrors.Agency.NotFound;
        }

        // Garder sa propre couleur est accepté ; seule une autre agence compte.
        if (all.Any(a => a.Id != agency.Id && a.Color == color.Value))
        {
            return OrganizationErrors.Color.AlreadyUsed;
        }

        agency.ChangeColor(color.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
