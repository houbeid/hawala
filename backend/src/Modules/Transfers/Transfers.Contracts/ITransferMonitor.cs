namespace Transfers.Contracts;

/// <summary>
/// Seule porte d'entrée des autres modules vers les transferts, pour le contrôle de cohérence de la Caisse :
/// ce qu'il faut signaler pour une agence depuis un instant (le dernier budget enregistré).
/// Respecte le périmètre du compte connecté.
/// </summary>
public interface ITransferMonitor
{
    /// <summary>Événements à signaler sur les transferts envoyés ou reçus par l'agence, enregistrés depuis <paramref name="since"/>.</summary>
    Task<IReadOnlyList<TransferAlertEvent>> ListAlertsAsync(Guid agencyId, DateTimeOffset since, CancellationToken cancellationToken);
}

public enum TransferAlertKind
{
    /// <summary>Prix de vente du transfert à plus de 5 % du prix de vente du jour (règle validée, alerte sans blocage).</summary>
    UnusualRate,

    /// <summary>Transfert annulé.</summary>
    Cancelled,

    /// <summary>Transfert livré sans code par le Superviseur.</summary>
    DeliveredWithoutCode,
}

/// <param name="At">Instant de l'événement (création ou modification, annulation, livraison).</param>
/// <param name="Reason">Motif (annulation, livraison sans code) ; null sinon.</param>
/// <param name="SaleRate">Taux inhabituel : prix de vente du transfert ; null sinon.</param>
/// <param name="DailySaleRate">Taux inhabituel : prix de vente du jour de l'opération ; null sinon.</param>
public sealed record TransferAlertEvent(
    Guid TransferId,
    string Reference,
    TransferAlertKind Kind,
    DateTimeOffset At,
    string? Reason,
    decimal? SaleRate,
    decimal? DailySaleRate);
