using Microsoft.AspNetCore.SignalR;
using Transfers.Application.Abstractions;

namespace Hawala.Api.Realtime;

/// <summary>
/// Envoie « TransferChanged » à l'agence d'envoi, à l'agence de destination et aux Superviseurs.
/// Appelé après la validation de la transaction : une notification perdue ne doit jamais faire échouer l'opération,
/// l'application relit de toute façon la liste à la reconnexion.
/// </summary>
public sealed class SignalRTransferChangePublisher(IHubContext<LiveHub> hub, ILogger<SignalRTransferChangePublisher> logger)
    : ITransferChangePublisher
{
    public async Task PublishAsync(IReadOnlyList<TransferChanged> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        foreach (var change in changes)
        {
            try
            {
                await hub.Clients
                    .Groups(
                        LiveHub.AgencyGroup(change.SourceAgencyId),
                        LiveHub.AgencyGroup(change.DestinationAgencyId),
                        LiveHub.SupervisorsGroup)
                    .SendAsync(LiveHub.TransferChangedMessage, change, cancellationToken);
            }
#pragma warning disable CA1031 // Notification seulement : l'opération est déjà validée, on journalise et on continue.
            catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
            {
                logger.LogWarning(exception, "Notification temps réel du transfert {TransferId} non envoyée.", change.TransferId);
            }
        }
    }
}
