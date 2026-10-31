using Audit.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Audit.Infrastructure;

/// <summary>
/// IP et appareil lus dans la requête HTTP en cours ; null hors requête (démarrage, tâche planifiée, test direct).
/// Derrière Caddy, l'IP est celle du visiteur : l'API lit les en-têtes transférés des proxies de confiance (UseForwardedHeaders).
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
