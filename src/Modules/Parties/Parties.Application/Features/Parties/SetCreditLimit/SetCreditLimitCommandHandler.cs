using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Organization.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;

namespace Parties.Application.Features.Parties.SetCreditLimit;

/// <summary>
/// Enregistre le plafond ; il sera appliqué (bloquant) quand les transferts à crédit existeront (phase 3).
/// Une seule table, aucune écriture au grand livre : pas de transaction commune.
/// POINT OUVERT : l'audit « qui a changé ce plafond » (avant / après) arrivera avec le module Audit.
/// </summary>
public sealed class SetCreditLimitCommandHandler(
    IPartyRepository parties,
    IAgencyDirectory agencies,
    IUnitOfWork unitOfWork)
    : ICommandHandler<SetCreditLimitCommand>
{
    public async Task<Result> Handle(SetCreditLimitCommand command, CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(command.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var agency = await agencies.GetByIdAsync(command.AgencyId, cancellationToken);
        if (agency is null)
        {
            return PartiesErrors.Party.AgencyAccountNotFound;
        }

        // Montant dans la devise de l'agence : FCFA entier, MRO à 2 décimales au plus (refusé, jamais arrondi).
        if (command.CreditLimit is { } limit && Money.Create(limit, agency.Currency) is { IsFailure: true } invalid)
        {
            return invalid.Error;
        }

        var set = party.SetCreditLimit(agency.Id, command.CreditLimit);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
