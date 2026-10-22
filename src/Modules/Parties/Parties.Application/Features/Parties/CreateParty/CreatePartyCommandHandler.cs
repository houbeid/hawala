using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties.CreateParty;

/// <summary>
/// Crée le tiers et ouvre son compte au grand livre dans CHAQUE agence (MRO à Nouakchott, FCFA à Dakar),
/// dans une seule transaction commune : si une étape échoue, rien n'existe.
/// Un compte limité (« Limiter les tiers ») qui crée un tiers le voit ensuite : il entre dans sa liste (règle du 21 octobre 2026).
/// Tracé dans l'audit (« Parties.Party.Created »), téléphone et NNI masqués.
/// </summary>
public sealed class CreatePartyCommandHandler(
    IPartyRepository parties,
    IAgencyDirectory agencies,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentAccount currentAccount,
    IAgencyScope agencyScope,
    IPartyScopeRepository scopes,
    IAuditLog audit)
    : ICommandHandler<CreatePartyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreatePartyCommand command, CancellationToken cancellationToken)
    {
        if (!EnumNames.TryParse<PartyType>(command.Type, out var type))
        {
            return PartiesErrors.Party.TypeInvalid;
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

        var party = Party.Create(
            command.Name, type, phone, command.Nni, command.Address, command.Note, clock.UtcNow);
        if (party.IsFailure)
        {
            return party.Error;
        }

        // Règle validée : doublon de téléphone ou de NNI interdit.
        if (phone is not null && await parties.PhoneExistsAsync(phone, excludingPartyId: null, cancellationToken))
        {
            return PartiesErrors.Party.PhoneTaken;
        }

        if (party.Value.Nni is { } nni && await parties.NniExistsAsync(nni, excludingPartyId: null, cancellationToken))
        {
            return PartiesErrors.Party.NniTaken;
        }

        foreach (var agency in await agencies.GetAllAsync(cancellationToken))
        {
            var account = await ledger.OpenAccountAsync(
                LedgerAccountKind.Party, party.Value.Id, party.Value.Name, agency.Currency, agency.Id, cancellationToken);
            if (account.IsFailure)
            {
                return account.Error;
            }

            party.Value.AttachAgencyAccount(agency.Id, account.Value);
        }

        parties.Add(party.Value);

        if (currentAccount.AccountId is { } creatorId && !agencyScope.SeesAllAgencies
            && await scopes.GetAsync(creatorId, cancellationToken) is { } scope)
        {
            scope.Allow(party.Value.Id, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Auteur : le compte connecté (rempli avec la session) ; null hors requête.
        var audited = await PartyAudit.RecordAsync(
            audit, PartyAudit.Created, party.Value.Id, currentAccount.AccountId,
            PartyAudit.Diff(null, PartyAudit.Snapshot.Of(party.Value)), cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return party.Value.Id;
    }
}
