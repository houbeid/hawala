using Identity.Application.Abstractions;
using Identity.Domain.RefreshTokens;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Identity.Infrastructure.Sessions;

/// <summary>
/// Appareil lu dans la requête HTTP en cours ; inconnu hors requête.
/// POINT OUVERT : derrière Caddy et Cloudflare, l'IP sera celle du proxy tant que les en-têtes transférés
/// ne sont pas configurés (au déploiement), comme pour l'audit.
/// </summary>
internal sealed class HttpClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    public SessionDevice Device
    {
        get
        {
            var http = accessor.HttpContext;
            return http is null
                ? SessionDevice.Unknown
                : new SessionDevice(http.Request.Headers[HeaderNames.UserAgent].ToString(), http.Connection.RemoteIpAddress?.ToString());
        }
    }
}
