using Identity.Domain.RefreshTokens;

namespace Identity.Application.Abstractions;

/// <summary>Appareil de la requête en cours (User-Agent, IP) ; inconnu hors d'une requête HTTP.</summary>
public interface IClientInfo
{
    SessionDevice Device { get; }
}
