using Ledger.Application.Abstractions;
using Ledger.Contracts;
using Ledger.Domain.Entries;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Infrastructure.Persistence.Repositories;

internal sealed class JournalEntryRepository(LedgerDbContext db) : IJournalEntryRepository
{
    public Task<JournalEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.JournalEntries
            .Include(e => e.Lines)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> IsReversedAsync(Guid entryId, CancellationToken cancellationToken) =>
        db.JournalEntries.AnyAsync(e => e.ReversesEntryId == entryId, cancellationToken);

    /// <summary>
    /// Une seule requête : SUM(débit − crédit) GROUP BY compte, filtrée par date d'opération si demandé.
    /// POINT OUVERT : le solde est recalculé à chaque lecture sur toutes les lignes du compte. Suffisant au départ
    /// (deux agences) ; si la lecture devient lente, ajouter un cache des soldes mis à jour dans la même transaction.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetBalancesAsync(
        IReadOnlyCollection<Guid> accountIds,
        DateOnly? asOf,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, decimal>();
        }

        var lines =
            from line in db.JournalLines
            join entry in db.JournalEntries on EF.Property<Guid>(line, "EntryId") equals entry.Id
            where accountIds.Contains(line.AccountId)
            select new { line.AccountId, line.Side, line.Amount, entry.OperationDate };

        if (asOf is { } date)
        {
            lines = lines.Where(l => l.OperationDate <= date);
        }

        return await lines
            .GroupBy(l => l.AccountId)
            .Select(g => new
            {
                AccountId = g.Key,
                Balance = g.Sum(l => l.Side == EntrySide.Debit ? l.Amount : -l.Amount),
            })
            .ToDictionaryAsync(b => b.AccountId, b => b.Balance, cancellationToken);
    }

    public void Add(JournalEntry entry) => db.JournalEntries.Add(entry);
}
