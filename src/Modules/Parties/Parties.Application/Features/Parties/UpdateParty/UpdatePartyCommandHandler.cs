using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Parties.Application.Abstractions;
using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties.UpdateParty;

/// <summary>
/// Une seule table, un seul enregistrement : pas de transaction commune.
/// POINT OUVERT : les comptes du tiers au grand livre gardent le nom donné à la création ; les écrans affichent
/// le nom tenu par les Tiers (même choix que pour la Trésorerie).
/// POINT OUVERT : l'audit « qui a modifié cette fiche » (avant / après) arrivera avec le module Audit.
/// </summary>
public sealed class UpdatePartyCommandHandler(
    IPartyRepository parties,
    IUnitOfWork unitOfWork)
    : ICommandHandler<UpdatePartyCommand>
{
    public async Task<Result> Handle(UpdatePartyCommand command, CancellationToken cancellationToken)
    {
        if (!EnumNames.TryParse<PartyType>(command.Type, out var type))
        {
            return PartiesErrors.Party.TypeInvalid;
        }

        if (command.InBalanceSheet is not { } inBalanceSheet)
        {
            return PartiesErrors.Party.InBalanceSheetRequired;
        }

        PartyPhone? phone = null;
        if (!string.IsNullOrWhiteSpace(command.Phone))
        {
            var parsedPhone = PartyPhone.Create(command.Phone);
            if (parsedPhone.IsFailure)
            {
                return parsedPhone.Error;
            }

            phone = parsedPhone.Value;
        }

        var party = await parties.GetByIdAsync(command.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        // Unicité vérifiée avant de modifier le tiers : un refus laisse l'entité suivie intacte.
        if (phone is not null && await parties.PhoneExistsAsync(phone, party.Id, cancellationToken))
        {
            return PartiesErrors.Party.PhoneTaken;
        }

        var nni = string.IsNullOrWhiteSpace(command.Nni) ? null : command.Nni.Trim();
        if (nni is not null && await parties.NniExistsAsync(nni, party.Id, cancellationToken))
        {
            return PartiesErrors.Party.NniTaken;
        }

        var updated = party.Update(command.Name, type, phone, nni, command.Address, command.Note, inBalanceSheet);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
