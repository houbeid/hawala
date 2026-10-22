namespace Audit.Application.Abstractions;

/// <summary>D'où vient l'action : IP et appareil de la requête en cours ; null hors d'une requête HTTP.</summary>
public interface IAuditRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }
}
