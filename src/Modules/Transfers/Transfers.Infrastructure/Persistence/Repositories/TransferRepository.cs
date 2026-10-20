using Microsoft.EntityFrameworkCore;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Persistence.Repositories;

internal sealed class TransferRepository(TransfersDbContext db) : ITransferRepository
{
    public void Add(Transfer transfer) => db.Transfers.Add(transfer);

    // Sans le filtre de périmètre : la clé appartient au compte, même s'il a changé d'agence depuis.
    public Task<Transfer?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken) =>
        db.Transfers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.CreatedBy == createdBy && t.IdempotencyKey == idempotencyKey, cancellationToken);
}
