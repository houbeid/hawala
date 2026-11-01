namespace Hawala.Api.Configuration;

/// <summary>
/// CORS du hub temps réel seulement (déploiement Railway + Vercel, 1er novembre 2026). L'interface passe par le relais de
/// Vercel pour /api (même origine, le cookie de session marche), mais ce relais ne transmet pas les WebSocket : le client
/// SignalR se connecte directement à l'API, d'une autre origine. Il s'authentifie par son jeton (« access_token »), sans
/// cookie : aucune autorisation d'envoi de cookies. Origines permises dans « Cors:HubOrigins » (adresses séparées par des
/// virgules, ex. https://hawala-preprod.vercel.app) ; vide = aucune. Le reste de l'API n'a jamais de CORS. Une adresse
/// mal écrite arrête le démarrage.
/// </summary>
public static class HubCorsSetup
{
    public const string PolicyName = "hub";
    public const string OriginsKey = "Cors:HubOrigins";

    public static IServiceCollection AddHawalaHubCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var origins = ParseOrigins(configuration[OriginsKey]);

        services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            policy.WithOrigins([.. origins]).AllowAnyHeader().WithMethods("GET", "POST");
        }));
        return services;
    }

    public static IReadOnlyList<string> ParseOrigins(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var origins = new List<string>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || uri.PathAndQuery != "/")
            {
                throw new InvalidOperationException($"{OriginsKey} : « {part} » n'est pas une origine (ex. https://hawala-preprod.vercel.app).");
            }

            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }

        return origins;
    }
}
