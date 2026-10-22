using Audit.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Audit.Infrastructure;

/// <summary>
/// IP et appareil lus dans la requête HTTP en cours ; null hors requête (démarrage, tâche planifiée, test direct).
/// POINT OUVERT : derrière Caddy et Cloudflare, l'IP vue ici sera celle du proxy tant que les en-têtes
/// transférés (UseForwardedHeaders, proxies de confiance) ne sont pas configurés : à régler au déploiement.
/// </summary>
internal sealed class HttpAuditRequestContext(IHttpContextAccessor accessor) : IAuditRequestContext
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = accessor.HttpContext?.Request.Headers[HeaderNames.UserAgent].ToString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
    }
}
