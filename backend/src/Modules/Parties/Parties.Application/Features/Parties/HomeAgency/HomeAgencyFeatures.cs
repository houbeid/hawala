using Audit.Contracts;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Organization.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Parties.HomeAgency;

/// <summary>Le Superviseur rattache un tiers à une agence, ou aux deux (permission parties.manage).</summary>
/// <param name="AgencyId">Agence de rattachement ; null = les deux agences.</param>
public sealed record SetPartyHomeAgencyCommand(Guid ActorId, Guid PartyId, Guid? AgencyId) : ICommand;

/// <summary>
/// Mise en service (2 novembre 2026) : rattache d'après leurs soldes les tiers encore rattachés aux deux agences.
/// Un solde dans une seule agence = cette agence ; aucun solde ou des soldes dans les deux = les deux agences.
/// </summary>
public sealed record AssignHomeAgenciesCommand(Guid ActorId) : ICommand<AssignedHomeAgencies>;

/// <param name="Assigned">Tiers rattachés à une agence.</param>
/// <param name="KeptBoth">Tiers laissés aux deux agences.</param>
public sealed record AssignedHomeAgencies(int Assigned, int KeptBoth);

/// <summary>
/// Rattachement d'un tiers (décision du patron, 2 novembre 2026) : un agent ne voit que les tiers de son agence et ceux
/// des deux ; le Superviseur voit tout et change le rattachement. Tracé dans l'audit (« Parties.Party.AgencyChanged »,
/// code d'agence ou « both »). Rien n'est écrit au grand livre : les comptes du tiers restent ouverts dans les deux agences.
/// </summary>
public sealed class HomeAgencyHandlers(
    IPartyRepository parties,
    IAgencyDirectory agencies,
    ILedger ledger,
    IUnitOfWork unitOfWork,
    IAuditLog audit)
    : ICommandHandler<SetPartyHomeAgencyCommand>, ICommandHandler<AssignHomeAgenciesCommand, AssignedHomeAgencies>
{
    public const string AgencyChanged = "Parties.Party.AgencyChanged";

    private const string Both = "both";

    public async Task<Result> Handle(SetPartyHomeAgencyCommand command, CancellationToken cancellationToken)
    {
        var party = await parties.GetByIdAsync(command.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var codes = await AgencyCodesAsync(cancellationToken);
        return await AssignAsync(party, command.AgencyId, command.ActorId, codes, cancellationToken);
    }

    public async Task<Result<AssignedHomeAgencies>> Handle(AssignHomeAgenciesCommand command, CancellationToken cancellationToken)
    {
        var both = await parties.ListAttachedToBothAsync(cancellationToken);
        var accounts = both.SelectMany(p => p.AgencyAccounts).Select(a => a.LedgerAccountId).ToList();
        var balances = accounts.Count == 0
            ? []
            : await ledger.GetBalancesAsync(accounts, asOf: null, cancellationToken) is { IsSuccess: true } read
                ? read.Value.ToDictionary(b => b.AccountId, b => b.Balance.Amount)
                : null;
        if (balances is null)
        {
            return PartiesErrors.Party.AgencyAccountNotFound;
        }

        var codes = await AgencyCodesAsync(cancellationToken);
        var assigned = 0;
        foreach (var party in both)
        {
            var withBalance = party.AgencyAccounts
                .Where(a => balances.GetValueOrDefault(a.LedgerAccountId) != 0m)
                .Select(a => a.AgencyId)
                .ToList();
            if (withBalance.Count != 1)
            {
                continue;
            }

            var done = await AssignAsync(party, withBalance[0], command.ActorId, codes, cancellationToken);
            if (done.IsFailure)
            {
                return done.Error;
            }

            assigned++;
        }

        return new AssignedHomeAgencies(assigned, both.Count - assigned);
    }

    private async Task<Result> AssignAsync(
        Party party, Guid? agencyId, Guid actorId, IReadOnlyDictionary<Guid, string> codes, CancellationToken cancellationToken)
    {
        var before = party.HomeAgencyId;
        var changed = party.AssignHomeAgency(agencyId);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        if (!changed.Value)
        {
            return Result.Success();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await PartyAudit.RecordAsync(
            audit, AgencyChanged, party.Id, actorId,
            [new AuditChange("agency", Code(before, codes), Code(agencyId, codes))],
            cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> AgencyCodesAsync(CancellationToken cancellationToken) =>
        (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id, a => a.Code);

    private static string Code(Guid? agencyId, IReadOnlyDictionary<Guid, string> codes) =>
        agencyId is { } id ? codes.GetValueOrDefault(id, id.ToString()) : Both;
}
