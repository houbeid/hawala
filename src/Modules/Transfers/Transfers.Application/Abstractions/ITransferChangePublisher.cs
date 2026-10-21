using Transfers.Domain.Transfers;

namespace Transfers.Application.Abstractions;

/// <summary>
/// Notification temps réel d'un transfert (règle validée le 21 octobre 2026) : message léger, sans montant ni nom ;
/// l'application relit la fiche par l'API, qui applique les règles de visibilité.
/// Destinataires : agence d'envoi, agence de destination et Superviseurs.
/// </summary>
public sealed record TransferChanged(
    Guid TransferId,
    string Reference,
    TransferEventType Event,
    int Version,
    TransferStatus Status,
    Guid SourceAgencyId,
    Guid DestinationAgencyId);

/// <summary>Envoie les notifications, APRÈS la validation de la transaction (jamais pour une opération annulée).</summary>
public interface ITransferChangePublisher
{
    Task PublishAsync(IReadOnlyList<TransferChanged> changes, CancellationToken cancellationToken);
}
