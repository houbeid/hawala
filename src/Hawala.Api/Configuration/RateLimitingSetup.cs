using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hawala.Api.Configuration;

/// <summary>
/// Limitation de débit (phase 6, tour 8, règle validée le 22 octobre 2026) :
/// <list type="bullet">
/// <item>connexion : 10 tentatives par minute et par IP (en plus du verrouillage du compte après 5 échecs) ;</item>
/// <item>tout le reste : 300 requêtes par minute et par compte connecté (par IP pour une requête anonyme) ;</item>
/// <item>au-delà : 429, avec l'en-tête Retry-After et le code « Api.RateLimited ».</item>
/// </list>
/// Fenêtres fixes d'une minute, comptées en mémoire : une seule instance de l'API tourne (VPS). Les valeurs se règlent
/// dans la configuration (« RateLimits:LoginPerMinute », « RateLimits:RequestsPerMinute »), par exemple pour les tests.
/// POINT OUVERT : derrière Caddy et Cloudflare, l'IP vue ici sera celle du proxy tant que les en-têtes transférés
/// ne sont pas configurés (au déploiement) : toutes les connexions partageraient alors la même limite.
/// </summary>
public static class RateLimitingSetup
{
    public const string LoginPolicy = "login";

    private const int DefaultLoginPerMinute = 10;
    private const int DefaultRequestsPerMinute = 300;

    public static IServiceCollection AddHawalaRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var loginPerMinute = configuration.GetValue("RateLimits:LoginPerMinute", DefaultLoginPerMinute);
        var requestsPerMinute = configuration.GetValue("RateLimits:RequestsPerMinute", DefaultRequestsPerMinute);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(LoginPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                $"login:{ClientIp(http)}", _ => Window(loginPerMinute)));

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var key = http.User.TryGetAccountId(out var accountId) ? $"account:{accountId:N}" : $"ip:{ClientIp(http)}";
                return RateLimitPartition.GetFixedWindowLimiter(key, _ => Window(requestsPerMinute));
            });
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "inconnue";

    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await response.WriteAsJsonAsync(
            new
            {
                type = "https://tools.ietf.org/html/rfc6585#section-4",
                title = "Api.RateLimited",
                status = StatusCodes.Status429TooManyRequests,
                detail = "Trop de requêtes : réessayer dans un instant.",
                code = "Api.RateLimited",
            },
            cancellationToken);
    }
}
