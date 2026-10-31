using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Hawala.Api.Configuration;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Auth;

/// <summary>
/// Vraie adresse du visiteur derrière Caddy (déploiement, 31 octobre 2026) : « X-Forwarded-For » n'est cru que s'il vient
/// d'un proxy de confiance (« ReverseProxy:TrustedNetworks ») ; sinon l'adresse de la connexion fait foi. Le serveur de
/// test n'a pas d'adresse réseau : un filtre de démarrage la fixe d'après l'en-tête « X-Test-Peer », comme Kestrel le ferait.
/// Adresses de documentation uniquement (RFC 5737).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ForwardedHeadersTests(ApiFixture fixture)
{
    private const string TrustedProxy = "10.9.0.2";
    private const string Stranger = "192.0.2.10";

    [Fact]
    public async Task Behind_the_trusted_proxy_each_visitor_has_his_own_login_limit()
    {
        var factory = await fixture.GetFactoryAsync();
        await using var api = BehindProxy(factory, loginPerMinute: 2);
        using var client = api.CreateClient();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await LoginAsync(client, TrustedProxy, "198.51.100.1", "inconnu@example.com", "MauvaisMotDePasse1");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var refused = await LoginAsync(client, TrustedProxy, "198.51.100.1", "inconnu@example.com", "MauvaisMotDePasse1");
        using var otherVisitor = await LoginAsync(client, TrustedProxy, "198.51.100.2", "inconnu@example.com", "MauvaisMotDePasse1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, otherVisitor.StatusCode);
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_caller_is_ignored()
    {
        var factory = await fixture.GetFactoryAsync();
        await using var api = BehindProxy(factory, loginPerMinute: 2);
        using var client = api.CreateClient();

        // Changer l'en-tête à chaque essai ne contourne pas la limite : seule l'adresse de la connexion compte.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await LoginAsync(client, Stranger, $"198.51.100.{attempt + 10}", "inconnu@example.com", "MauvaisMotDePasse1");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var refused = await LoginAsync(client, Stranger, "198.51.100.99", "inconnu@example.com", "MauvaisMotDePasse1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    [Fact]
    public async Task The_session_keeps_the_visitor_address_not_the_proxy_one()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        await using var api = BehindProxy(factory, loginPerMinute: 100_000);
        using var client = api.CreateClient();

        using var login = await LoginAsync(client, TrustedProxy, "203.0.113.7", agent.Email, agent.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var (token, _) = await login.ReadSessionAsync();

        using var sessions = await client.SendGetAsync(token, "/api/v1/auth/sessions");
        var session = Assert.Single((await sessions.ReadJsonAsync()).EnumerateArray());
        Assert.Equal("203.0.113.7", session.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public void A_badly_written_trusted_network_stops_the_start()
    {
        Assert.Equal(2, ForwardedHeadersSetup.ParseNetworks("172.30.0.0/24, 10.0.0.0/8").Count);
        Assert.Empty(ForwardedHeadersSetup.ParseNetworks(" "));
        Assert.Throws<InvalidOperationException>(() => ForwardedHeadersSetup.ParseNetworks("172.30.0.0/99"));
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string peer, string forwardedFor, string login, string password)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { login, password }),
        };
        request.Headers.Add(PeerAddressFilter.Header, peer);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return await client.SendAsync(request);
    }

    /// <summary>Même API et même base : Caddy de confiance sur 10.9.0.0/24, limite de connexion basse.</summary>
    private static WebApplicationFactory<Program> BehindProxy(HawalaApiFactory factory, int loginPerMinute) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(ForwardedHeadersSetup.TrustedNetworksKey, "10.9.0.0/24");
            builder.UseSetting("RateLimits:LoginPerMinute", loginPerMinute.ToString(CultureInfo.InvariantCulture));
            builder.ConfigureServices(services => services.AddTransient<IStartupFilter, PeerAddressFilter>());
        });

    /// <summary>Fixe l'adresse de la connexion avant tout le reste du pipeline (UseForwardedHeaders compris).</summary>
    private sealed class PeerAddressFilter : IStartupFilter
    {
        public const string Header = "X-Test-Peer";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[Header].ToString(), out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
