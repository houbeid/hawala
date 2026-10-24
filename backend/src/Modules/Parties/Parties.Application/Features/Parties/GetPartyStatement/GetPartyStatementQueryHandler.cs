using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain;

namespace Parties.Application.Features.Parties.GetPartyStatement;

/// <summary>
/// Lit le relevé au grand livre (seule source des montants). Un agent ne voit que son agence : une autre agence
/// est « introuvable », sans révéler que le tiers y a un compte.
/// POINT OUVERT : « aujourd'hui » est pris en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : pas de pagination des lignes ; le mois en cours par défaut limite la taille.
/// </summary>
public sealed class GetPartyStatementQueryHandler(
    IPartyRepository parties,
    IPermissionChecker permissions,
    IAgencyScope scope,
    ILedger ledger,
    IClock clock)
    : IQueryHandler<GetPartyStatementQuery, PartyStatement>
{
    public async Task<Result<PartyStatement>> Handle(GetPartyStatementQuery query, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(query.ActorId, Permissions.PartiesViewBalance, cancellationToken))
        {
            return PartiesErrors.Statement.NotVisible;
        }

        var agencyId = query.AgencyId ?? scope.AgencyId;
        if (agencyId is null)
        {
            return PartiesErrors.Statement.AgencyRequired;
        }

        var party = await parties.GetByIdAsync(query.PartyId, cancellationToken);
        if (party is null)
        {
            return PartiesErrors.Party.NotFound;
        }

        var account = party.AgencyAccounts.FirstOrDefault(a => a.AgencyId == agencyId);
        if (account is null || !scope.CanSee(agencyId.Value))
        {
            return PartiesErrors.Party.AgencyAccountNotFound;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var from = query.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = query.To ?? today;

        var statement = await ledger.GetStatementAsync(account.LedgerAccountId, from, to, cancellationToken);
        if (statement.IsFailure)
        {
            return statement.Error;
        }

        var s = statement.Value;
        return new PartyStatement(
            party.Id,
            party.Name,
            agencyId.Value,
            s.Currency,
            s.From,
            s.To,
            s.OpeningBalance,
            [.. s.Lines.Select(l => new PartyStatementLine(
                l.OperationDate, l.RecordedAt, l.EntryType, l.Label, l.Reason, l.IsReversal, l.Debit, l.Credit, l.BalanceAfter))],
            s.ClosingBalance);
    }
}
