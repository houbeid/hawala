using System.Net;
using Hawala.Api.Configuration;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Identity.IntegrationTests.Auth;

/// <summary>
/// CORS du hub seulement (déploiement Railway + Vercel, 1er novembre 2026) : l'interface servie par Vercel ouvre le temps
/// réel directement sur l'API ; une autre origine est refusée ; le reste de l'API n'a jamais de CORS ; pas de cookie
/// autorisé (le hub s'authentifie par le jeton).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class HubCorsTests(ApiFixture fixture)
{
    private const string Interface = "https://hawala-preprod.vercel.app";

    [Fact]
    public async Task The_interface_origin_may_open_the_hub()
    {
        await using var api = WithHubOrigin(await fixture.GetFactoryAsync());
        using var client = api.CreateClient();

        using var response = await PreflightAsync(client, "/hubs/live/negotiate?negotiateVersion=1", Interface);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(Interface, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task Another_origin_may_not_open_the_hub()
    {
        await using var api = WithHubOrigin(await fixture.GetFactoryAsync());
        using var client = api.CreateClient();

        using var response = await PreflightAsync(client, "/hubs/live/negotiate?negotiateVersion=1", "https://ailleurs.example.com");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task The_rest_of_the_api_never_answers_cross_origin()
    {
        await using var api = WithHubOrigin(await fixture.GetFactoryAsync());
        using var client = api.CreateClient();

        using var response = await PreflightAsync(client, "/api/v1/auth/login", Interface);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public void A_badly_written_origin_stops_the_start()
    {
        Assert.Equal(["https://a.example.com", "http://localhost:5173"], HubCorsSetup.ParseOrigins(" https://a.example.com/ , http://localhost:5173"));
        Assert.Empty(HubCorsSetup.ParseOrigins(null));
        Assert.Throws<InvalidOperationException>(() => HubCorsSetup.ParseOrigins("hawala-preprod.vercel.app"));
        Assert.Throws<InvalidOperationException>(() => HubCorsSetup.ParseOrigins("https://a.example.com/chemin"));
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,x-requested-with,x-signalr-user-agent");
        return await client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> WithHubOrigin(HawalaApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting(HubCorsSetup.OriginsKey, Interface));
}
