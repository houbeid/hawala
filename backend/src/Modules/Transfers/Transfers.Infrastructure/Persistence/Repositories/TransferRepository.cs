using Hawala.SharedKernel.Money;
using Microsoft.EntityFrameworkCore;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Persistence.Repositories;

internal sealed class TransferRepository(TransfersDbContext db) : ITransferRepository
{
    private const string LikeEscape = "\\";

    public void Add(Transfer transfer) => db.Transfers.Add(transfer);

    public Task LockCreatorAsync(Guid createdBy, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({createdBy.ToString()}, 0))", cancellationToken);

    // Sans le filtre de périmètre : ce sont les transferts de ce compte, quel que soit ce que voit l'appelant.
    public async Task<(decimal Paid, decimal Credit)> SumCreatedAsync(
        Guid createdBy, Guid sourceAgencyId, DateTimeOffset from, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        var totals = await db.Transfers
            .IgnoreQueryFilters()
            .Where(t => t.CreatedBy == createdBy
                && t.SourceAgencyId == sourceAgencyId
                && t.Status != TransferStatus.Cancelled
                && t.CreatedAt >= from
                && t.CreatedAt < toExclusive)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Paid = g.Sum(t => t.SourceCurrency == Currency.MRO ? t.AmountMro : t.AmountXof),
                Credit = g.Sum(t => t.CreditAmount),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null ? (0m, 0m) : (totals.Paid, totals.Credit);
    }

    // Sans le filtre de périmètre : la clé appartient au compte, même s'il a changé d'agence depuis.
    public Task<Transfer?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Transfers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CreatedBy == createdBy && t.IdempotencyKey == idempotencyKey, cancellationToken);

    public Task<Transfer?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Transfers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<Transfer?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        // Verrou d'abord (sans filtre : la ligne existe ou non), puis lecture suivie AVEC le filtre de périmètre.
        await db.Database
            .SqlQuery<Guid>($"""
                SELECT "Id" AS "Value"
                FROM transfers.transfers
                WHERE "Id" = {id}
                FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

        return await db.Transfers.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Transfer>> ListAllAsync(TransferFilter filter, CancellationToken cancellationToken) =>
        await Filtered(filter).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Transfer>> ListChangedSinceAsync(
        Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken) =>
        await db.Transfers.AsNoTracking()
            .Where(t => (t.SourceAgencyId == agencyId || t.DestinationAgencyId == agencyId) && t.Events.Any(e => e.At >= since))
            .ToListAsync(cancellationToken);

    public Task<int> CountVisibleAsync(CancellationToken cancellationToken) =>
        db.Transfers.CountAsync(cancellationToken);

    public async Task<(IReadOnlyList<Transfer> Items, int TotalCount)> ListAsync(
        TransferFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(t => t.OperationAt)
            .ThenByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    private IQueryable<Transfer> Filtered(TransferFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.Transfers.AsNoTracking()
            .Where(t => t.OperationAt >= filter.From && t.OperationAt < filter.ToExclusive);

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }

        if (filter.SourceAgencyId is { } source)
        {
            query = query.Where(t => t.SourceAgencyId == source);
        }

        if (filter.DestinationAgencyId is { } destination)
        {
            query = query.Where(t => t.DestinationAgencyId == destination);
        }

        if (filter.EitherAgencyId is { } either)
        {
            query = query.Where(t => t.SourceAgencyId == either || t.DestinationAgencyId == either);
        }

        if (filter.Search is { } search)
        {
            // « % » et « _ » saisis par l'agent sont cherchés tels quels, pas comme jokers.
            var pattern = "%" + search.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
                .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
                .Replace("_", LikeEscape + "_", StringComparison.Ordinal) + "%";

            query = query.Where(t =>
                EF.Functions.ILike(t.Reference, pattern, LikeEscape)
                || EF.Functions.ILike(t.SenderInfo, pattern, LikeEscape)
                || EF.Functions.ILike(t.BeneficiaryInfo, pattern, LikeEscape)
                || (t.Note != null && EF.Functions.ILike(t.Note, pattern, LikeEscape)));
        }

        return query;
    }
}
