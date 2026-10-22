using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;

namespace Parties.Application.Features.Parties.SetOpeningBalance;

/// <summary>
/// Règle validée : changer le solde initial passe par une écriture d'ouverture DE LA DIFFÉRENCE, contre le compte système
/// « Soldes d'ouverture » de l'agence (rattaché au capital : ni gain ni perte). Personne n'écrit un solde à la main.
/// Une seule transaction commune : le solde initial retenu et l'écriture vont ensemble.
/// Une date d'opération dans le futur est refusée par le grand livre (règle validée le 20 octobre 2026).
/// POINT OUVERT : la date du jour est prise en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit avant / après arrivera avec le module Audit ; l'écriture garde déjà l'auteur et la date.
/// </summary>
public sealed class SetOpeningBalanceCommandHandler(
    IPartyRepository parties,
    IAgencyDirectory agencies,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAuditLog audit)
    : ICommandHandler<SetOpeningBalanceCommand>
{
    public const string EntryType = "Parties.OpeningBalance";

    public async Task<Result> Handle(SetOpeningBalanceCommand command, CancellationToken cancellationToken)
    {
        if (command.Amount is not { } amount)
        {
            return PartiesErrors.Party.AmountRequired;
        }

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
        var valid = Money.Create(amount, agency.Currency);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        var before = party.AgencyAccounts.SingleOrDefault(a => a.AgencyId == agency.Id)?.OpeningBalance;
        var difference = party.ChangeOpeningBalance(agency.Id, amount);
        if (difference.IsFailure)
        {
            return difference.Error;
        }

        // Même solde initial : rien à écrire.
        if (difference.Value != 0m)
        {
            var partyLedgerAccount = party.AgencyAccounts.Single(a => a.AgencyId == agency.Id).LedgerAccountId;
            var posted = await PostDifferenceAsync(
                command, party.Id, party.Name, partyLedgerAccount, agency, difference.Value, cancellationToken);
            if (posted.IsFailure)
            {
                return posted.Error;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (difference.Value != 0m)
        {
            var audited = await PartyAudit.RecordAsync(
                audit, PartyAudit.OpeningBalanceChanged, party.Id, command.ActorId,
                [new AuditChange("openingBalance", PartyAudit.Text(before), PartyAudit.Text(amount))],
                cancellationToken, agency.Id);
            if (audited.IsFailure)
            {
                return audited.Error;
            }
        }

        return Result.Success();
    }

    private async Task<Result> PostDifferenceAsync(
        SetOpeningBalanceCommand command,
        Guid partyId,
        string partyName,
        Guid partyLedgerAccount,
        AgencySummary agency,
        decimal difference,
        CancellationToken cancellationToken)
    {
        var openingBalances = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OpeningBalances, agency.Id, agency.Currency, cancellationToken);
        if (openingBalances.IsFailure)
        {
            return openingBalances.Error;
        }

        // Positif : le tiers nous doit davantage (débit du tiers). Négatif : on lui doit davantage (crédit du tiers).
        var amount = Money.Create(Math.Abs(difference), agency.Currency).Value;
        var partySide = difference > 0m ? EntrySide.Debit : EntrySide.Credit;
        var counterSide = difference > 0m ? EntrySide.Credit : EntrySide.Debit;

        var entry = await ledger.PostAsync(
            new PostEntryRequest(
                command.OperationDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime),
                EntryType,
                "Parties",
                partyId,
                command.ActorId,
                $"Solde initial : {partyName}",
                [
                    new PostEntryLine(partyLedgerAccount, partySide, amount),
                    new PostEntryLine(openingBalances.Value, counterSide, amount),
                ]),
            cancellationToken);

        return entry.IsSuccess ? Result.Success() : entry.Error;
    }
}
