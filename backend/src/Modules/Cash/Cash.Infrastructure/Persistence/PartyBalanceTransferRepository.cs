using Cash.Application.Abstractions;
using Cash.Domain.PartyTransfers;
using Microsoft.EntityFrameworkCore;

namespace Cash.Infrastructure.Persistence;

internal sealed class PartyBalanceTransferRepository(CashDbContext db) : IPartyBalanceTransferRepository
{
    public void Add(PartyBalanceTransfer transfer) => db.PartyTransfers.Add(transfer);

    // Sans filtre de périmètre : la clé appartient au compte, quelle que soit l'agence.
    public Task<PartyBalanceTransfer?> FindByIdempotencyKeyAsync(Guid createdBy, string key, CancellationToken cancellationToken) =>
        db.PartyTransfers.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(t => t.CreatedBy == createdBy && t.IdempotencyKey == key, cancellationToken);
}
