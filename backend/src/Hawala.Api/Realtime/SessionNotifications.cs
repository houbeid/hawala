using Microsoft.AspNetCore.SignalR;

namespace Hawala.Api.Realtime;

/// <summary>
/// Prévient en direct les appareils dont la session vient d'être fermée (phase 6). Appelé par les routes APRÈS le succès
/// de la commande (sa transaction est validée). Une notification perdue ne fait jamais échouer l'opération : l'API refuse
/// de toute façon les requêtes de ces sessions.
/// </summary>
public sealed class SessionNotifications(IHubContext<LiveHub> hub, ILogger<SessionNotifications> logger)
{
    public sealed record SessionClosed(Guid SessionId);

    public sealed record SignedOut(Guid AccountId);

    public Task SessionClosedAsync(Guid sessionId, CancellationToken cancellationToken) =>
        SendAsync(LiveHub.SessionGroup(sessionId), LiveHub.SessionClosedMessage, new SessionClosed(sessionId), cancellationToken);

    public Task SignedOutAsync(Guid accountId, CancellationToken cancellationToken) =>
        SendAsync(LiveHub.AccountGroup(accountId), LiveHub.SignedOutMessage, new SignedOut(accountId), cancellationToken);

    private async Task SendAsync(string group, string message, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.Group(group).SendAsync(message, payload, cancellationToken);
        }
#pragma warning disable CA1031 // Notification seulement : l'opération est déjà validée, on journalise et on continue.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogWarning(exception, "Notification temps réel « {Message} » non envoyée.", message);
        }
    }
}
