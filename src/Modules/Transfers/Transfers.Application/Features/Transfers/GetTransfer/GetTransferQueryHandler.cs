using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.GetTransfer;

/// <summary>Hors périmètre = introuvable (404), comme pour les comptes de trésorerie.</summary>
public sealed class GetTransferQueryHandler(ITransferRepository transfers, TransferViews views)
    : IQueryHandler<GetTransferQuery, TransferView>
{
    public async Task<Result<TransferView>> Handle(GetTransferQuery query, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetByIdAsync(query.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var built = await views.BuildAsync(query.ActorId, [transfer], referenceAgencyId: null, cancellationToken);
        return built[0];
    }
}
