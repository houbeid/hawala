using Transfers.Domain.Transfers;

namespace Transfers.Application.Abstractions;

public interface ITransferRepository
{
    void Add(Transfer transfer);

    /// <summary>Transfert déjà créé par ce compte avec cette clé (double clic, resynchronisation hors ligne).</summary>
    Task<Transfer?> FindByIdempotencyKeyAsync(Guid createdBy, string idempotencyKey, CancellationToken cancellationToken);
}
