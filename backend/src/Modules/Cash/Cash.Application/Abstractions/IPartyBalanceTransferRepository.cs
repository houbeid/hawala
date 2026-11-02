using Cash.Domain.PartyTransfers;

namespace Cash.Application.Abstractions;

/// <summary>Accès aux transferts de solde entre tiers. Le périmètre par agence est appliqué par le filtre global.</summary>
public interface IPartyBalanceTransferRepository
{
    void Add(PartyBalanceTransfer transfer);

    /// <summary>Transfert déjà enregistré par ce compte avec cette clé d'idempotence, ou null.</summary>
    Task<PartyBalanceTransfer?> FindByIdempotencyKeyAsync(Guid createdBy, string key, CancellationToken cancellationToken);
}
