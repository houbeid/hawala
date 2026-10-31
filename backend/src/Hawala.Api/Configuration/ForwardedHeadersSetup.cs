using IPNetwork = System.Net.IPNetwork;
using Microsoft.AspNetCore.HttpOverrides;

namespace Hawala.Api.Configuration;

/// <summary>
/// Vraie adresse du visiteur derrière Caddy (déploiement, 31 octobre 2026). Caddy ajoute « X-Forwarded-For » et
/// « X-Forwarded-Proto » ; l'API ne les croit que s'ils viennent d'un proxy de confiance : un réseau listé dans
/// « ReverseProxy:TrustedNetworks » (CIDR séparés par des virgules, ex. le réseau Docker de Caddy) ou la machine elle-même.
/// Un seul saut est lu (le dernier ajouté par Caddy) : un visiteur ne peut pas se faire passer pour une autre adresse en
/// envoyant lui-même l'en-tête. Toute l'API lit ensuite RemoteIpAddress : limitation de débit, audit, sessions.
/// Une valeur mal écrite arrête le démarrage, comme une clé absente.
/// </summary>
public static class ForwardedHeadersSetup
{
    public const string TrustedNetworksKey = "ReverseProxy:TrustedNetworks";

    public static IServiceCollection AddHawalaForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var networks = ParseNetworks(configuration[TrustedNetworksKey]);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var network in networks)
            {
                options.KnownIPNetworks.Add(network);
            }
        });
        return services;
    }

    public static IReadOnlyList<IPNetwork> ParseNetworks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var networks = new List<IPNetwork>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IPNetwork.TryParse(part, out var network))
            {
                throw new InvalidOperationException($"{TrustedNetworksKey} : « {part} » n'est pas un réseau CIDR (ex. 172.30.0.0/24).");
            }

            networks.Add(network);
        }

        return networks;
    }
}
