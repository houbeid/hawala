using Hawala.SharedKernel.Messaging;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.GetTransferHistory;

/// <summary>Historique d'un transfert, pour ceux qui voient le transfert (404 sinon).</summary>
public sealed record GetTransferHistoryQuery(Guid ActorId, Guid TransferId) : IQuery<IReadOnlyList<TransferEventView>>;

/// <param name="AuthorId">Compte qui a fait l'action.</param>
/// <param name="Changes">Avant / après des champs changés, sans ceux que le lecteur n'a pas le droit de voir.</param>
public sealed record TransferEventView(
    TransferEventType Type,
    DateTimeOffset At,
    Guid AuthorId,
    string? Reason,
    IReadOnlyList<TransferFieldChange> Changes);
