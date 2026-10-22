using Transfers.Application.Abstractions;
using Transfers.Contracts;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers;

/// <summary>Implémentation de la porte publique du bénéfice : transferts non annulés de la période, sur la date d'opération.</summary>
public sealed class TransferProfits(ITransferRepository transfers) : ITransferProfits
{
    public async Task<IReadOnlyList<TransferProfitLine>> ListAsync(
        DateTimeOffset from, DateTimeOffset toExclusive, Guid? sourceAgencyId, CancellationToken cancellationToken)
    {
        var found = await transfers.ListAllAsync(
            new TransferFilter(from, toExclusive, SourceAgencyId: sourceAgencyId), cancellationToken);

        return [.. found
            .Where(t => t.Status != TransferStatus.Cancelled)
            .OrderBy(t => t.OperationAt)
            .Select(t => new TransferProfitLine(
                t.Id, t.Reference, t.SourceAgencyId, t.DestinationAgencyId, t.OperationAt,
                t.AmountXof, t.AmountMro, t.SaleRate.MroPer5000Xof, t.PurchaseRate.MroPer5000Xof, t.ProfitMro))];
    }
}
