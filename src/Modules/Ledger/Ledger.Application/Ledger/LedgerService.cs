using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Ledger.Application.Abstractions;
using Ledger.Contracts;
using Ledger.Domain;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Application.Ledger;

/// <summary>
/// Implémente <see cref="ILedger"/>, la porte d'entrée des autres modules vers le grand livre.
/// Enregistre dans la connexion partagée de la requête mais ne valide jamais la transaction :
/// c'est le décorateur de la commande appelante qui le fait (une commande = une transaction).
/// </summary>
public sealed class LedgerService(
    ILedgerAccountRepository accounts,
    IJournalEntryRepository entries,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ILedger
{
    public async Task<Result<Guid>> OpenAccountAsync(
        LedgerAccountKind kind,
        Guid ownerId,
        string name,
        Currency currency,
        Guid agencyId,
        CancellationToken cancellationToken)
    {
        var account = LedgerAccount.Open(kind, ownerId, name, currency, agencyId, clock.UtcNow);
        if (account.IsFailure)
        {
            return account.Error;
        }

        accounts.Add(account.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Value.Id;
    }

    public Task<Result> CloseAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        ChangeAccountAsync(accountId, account => account.Close(), cancellationToken);

    public Task<Result> ReopenAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        ChangeAccountAsync(accountId, account => account.Reopen(), cancellationToken);

    /// <summary>
    /// POINT OUVERT : si deux requêtes créent au même instant le même compte système d'une agence,
    /// l'index unique en base refuse la seconde (erreur 500, comme la course sur l'email des comptes).
    /// Accepté : cela n'arrive qu'une fois par agence et par rôle dans toute la vie de l'app.
    /// </summary>
    public async Task<Result<Guid>> GetOrCreateSystemAccountAsync(
        SystemAccountRole role,
        Guid agencyId,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var existing = await accounts.GetSystemAccountAsync(role, agencyId, cancellationToken);
        if (existing is not null)
        {
            // Une agence n'a qu'une devise : une autre devise ici est un bug de l'appelant.
            return existing.Currency == currency ? existing.Id : LedgerErrors.Line.CurrencyMismatch;
        }

        var account = LedgerAccount.CreateSystem(role, agencyId, currency, clock.UtcNow);
        if (account.IsFailure)
        {
            return account.Error;
        }

        accounts.Add(account.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Value.Id;
    }

    public async Task<Result<Guid>> PostAsync(PostEntryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Lines);

        // Tous les comptes de l'écriture en une seule lecture.
        var accountIds = request.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accountById = (await accounts.GetByIdsAsync(accountIds, cancellationToken)).ToDictionary(a => a.Id);
        if (accountById.Count != accountIds.Count)
        {
            return LedgerErrors.Account.NotFound;
        }

        var drafts = request.Lines
            .Select(l => new EntryLineDraft(accountById[l.AccountId], l.Side, l.Amount))
            .ToList();

        var entry = JournalEntry.Post(
            request.OperationDate,
            request.EntryType,
            request.SourceModule,
            request.SourceId,
            request.AuthorId,
            request.Label,
            drafts,
            clock.UtcNow);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        entries.Add(entry.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entry.Value.Id;
    }

    public async Task<Result<Guid>> ReverseAsync(
        Guid entryId,
        DateOnly operationDate,
        Guid authorId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var original = await entries.GetByIdAsync(entryId, cancellationToken);
        if (original is null)
        {
            return LedgerErrors.Entry.NotFound;
        }

        // Une écriture immuable ne se marque pas « contre-passée » : on cherche sa contre-passation.
        if (await entries.IsReversedAsync(entryId, cancellationToken))
        {
            return LedgerErrors.Reversal.AlreadyReversed;
        }

        var reversal = original.Reverse(operationDate, authorId, reason, clock.UtcNow);
        if (reversal.IsFailure)
        {
            return reversal.Error;
        }

        entries.Add(reversal.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return reversal.Value.Id;
    }

    public async Task<Result<AccountStatement>> GetStatementAsync(
        Guid accountId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return LedgerErrors.Statement.PeriodInvalid;
        }

        var account = (await accounts.GetByIdsAsync([accountId], cancellationToken)).SingleOrDefault();
        if (account is null)
        {
            return LedgerErrors.Account.NotFound;
        }

        // Solde à la veille du début : même calcul que les soldes, une seule requête.
        var before = await entries.GetBalancesAsync([accountId], from.AddDays(-1), cancellationToken);
        var opening = before.GetValueOrDefault(accountId);

        var running = opening;
        var lines = new List<StatementLine>();
        foreach (var line in await entries.GetAccountLinesAsync(accountId, from, to, cancellationToken))
        {
            var debit = line.Side == EntrySide.Debit ? line.Amount : 0m;
            var credit = line.Side == EntrySide.Credit ? line.Amount : 0m;
            running += debit - credit;
            lines.Add(new StatementLine(
                line.EntryId, line.OperationDate, line.RecordedAt, line.EntryType, line.Label, line.Reason,
                line.IsReversal, debit, credit, running));
        }

        return new AccountStatement(accountId, account.Currency, from, to, opening, lines, running);
    }

    private async Task<Result> ChangeAccountAsync(
        Guid accountId, Action<LedgerAccount> change, CancellationToken cancellationToken)
    {
        var account = (await accounts.GetByIdsAsync([accountId], cancellationToken)).SingleOrDefault();
        if (account is null)
        {
            return LedgerErrors.Account.NotFound;
        }

        change(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<AccountBalance>>> GetBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        var ids = accountIds.Distinct().ToList();
        var known = await accounts.GetByIdsAsync(ids, cancellationToken);
        if (known.Count != ids.Count)
        {
            return LedgerErrors.Account.NotFound;
        }

        var sums = await entries.GetBalancesAsync(ids, asOf, cancellationToken);

        // Une somme de montants valides (FCFA entiers, MRO à 2 décimales) reste valide : Create ne peut pas échouer.
        return Result.Success<IReadOnlyList<AccountBalance>>(
        [
            .. known.Select(a => new AccountBalance(
                a.Id,
                Money.Create(sums.GetValueOrDefault(a.Id), a.Currency).Value)),
        ]);
    }
}
