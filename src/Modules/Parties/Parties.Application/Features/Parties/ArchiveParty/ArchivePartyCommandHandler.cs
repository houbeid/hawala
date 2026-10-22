using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;

namespace Parties.Application.Features.Parties.ArchiveParty;

/// <summary>
/// Lit les soldes au grand livre, archive le tiers (refusé si un solde n'est pas nul), puis ferme ses comptes
/// au grand livre dans les deux agences : plus aucune opération ne peut l'atteindre. Une seule transaction commune.
/// Fermer les comptes est un choix de cohérence avec la désactivation d'un compte de trésorerie.
/// POINT OUVERT : pas de verrou entre la lecture des soldes et la fermeture (même limite que pour la Trésorerie).
/// POINT OUVERT : l'audit « qui a archivé ce tiers » arrivera avec le module Audit.
/// </summary>
public sealed class ArchivePartyCommandHandler(
    IPartyRepository parties,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<ArchivePartyCommand>
{
    public async Task<Result> Handle(ArchivePartyCommand command, CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(command.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var ledgerAccounts = party.AgencyAccounts.Select(a => a.LedgerAccountId).ToList();
        var balances = await ledger.GetBalancesAsync(ledgerAccounts, asOf: null, cancellationToken);
        if (balances.IsFailure)
        {
            return balances.Error;
        }

        var archived = party.Archive(balances.Value.ToDictionary(b => b.AccountId, b => b.Balance.Amount));
        if (archived.IsFailure)
        {
            return archived.Error;
        }

        foreach (var account in ledgerAccounts)
        {
            var closed = await ledger.CloseAccountAsync(account, cancellationToken);
            if (closed.IsFailure)
            {
                return closed.Error;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await PartyAudit.RecordAsync(
            audit, PartyAudit.Archived, party.Id, command.ActorId,
            [new AuditChange("isArchived", "false", "true")], cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }
}

/// <summary>Désarchive le tiers et rouvre ses comptes au grand livre, dans une seule transaction commune.</summary>
public sealed class UnarchivePartyCommandHandler(
    IPartyRepository parties,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<UnarchivePartyCommand>
{
    public async Task<Result> Handle(UnarchivePartyCommand command, CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(command.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var unarchived = party.Unarchive();
        if (unarchived.IsFailure)
        {
            return unarchived.Error;
        }

        foreach (var account in party.AgencyAccounts)
        {
            var reopened = await ledger.ReopenAccountAsync(account.LedgerAccountId, cancellationToken);
            if (reopened.IsFailure)
            {
                return reopened.Error;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var audited = await PartyAudit.RecordAsync(
            audit, PartyAudit.Unarchived, party.Id, command.ActorId,
            [new AuditChange("isArchived", "true", "false")], cancellationToken);
        if (audited.IsFailure)
        {
            return audited.Error;
        }

        return Result.Success();
    }
}
