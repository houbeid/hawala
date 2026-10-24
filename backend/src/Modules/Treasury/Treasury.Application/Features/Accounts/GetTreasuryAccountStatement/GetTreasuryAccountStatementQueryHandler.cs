using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Ledger.Contracts;
using Treasury.Application.Abstractions;
using Treasury.Domain;

namespace Treasury.Application.Features.Accounts.GetTreasuryAccountStatement;

/// <summary>
/// Lit le relevé au grand livre (seule source des montants). Un compte de l'autre agence est « introuvable » pour un
/// agent (filtre de périmètre), sans révéler qu'il existe. Un compte désactivé garde son relevé.
/// POINT OUVERT : « aujourd'hui » est pris en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : pas de pagination des lignes ; le mois en cours par défaut limite la taille.
/// </summary>
public sealed class GetTreasuryAccountStatementQueryHandler(
    ITreasuryAccountRepository accounts,
    IPermissionChecker permissions,
    ILedger ledger,
    IClock clock)
    : IQueryHandler<GetTreasuryAccountStatementQuery, TreasuryAccountStatement>
{
    public async Task<Result<TreasuryAccountStatement>> Handle(
        GetTreasuryAccountStatementQuery query, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(query.ActorId, Permissions.CashBudgetRead, cancellationToken))
        {
            return TreasuryErrors.Statement.NotVisible;
        }

        var account = await accounts.GetByIdAsync(query.AccountId, cancellationToken);
        if (account is null)
        {
            return TreasuryErrors.Account.NotFound;
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
        return new TreasuryAccountStatement(
            account.Id,
            account.Name,
            account.Type.ToString(),
            account.AgencyId,
            s.Currency,
            account.IsActive,
            s.From,
            s.To,
            s.OpeningBalance,
            [.. s.Lines.Select(l => new TreasuryStatementLine(
                l.OperationDate, l.RecordedAt, l.EntryType, l.Label, l.Reason, l.IsReversal, l.Debit, l.Credit, l.BalanceAfter))],
            s.ClosingBalance);
    }
}
