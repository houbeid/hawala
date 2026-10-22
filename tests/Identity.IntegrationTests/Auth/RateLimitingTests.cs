using System.Net;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Identity.IntegrationTests.Auth;

/// <summary>
/// Limitation de débit (phase 6, tour 8, règle validée le 22 octobre 2026) : 10 connexions par minute et par IP,
/// 300 requêtes par minute et par compte, puis 429. Éprouvée sur une copie de l'API avec des limites basses
/// (la suite normale les relève, elle se connecte des centaines de fois depuis la même adresse).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RateLimitingTests(ApiFixture fixture)
{
    [Fact]
    public async Task Too_many_login_attempts_from_one_address_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        await using var limited = Limited(factory, loginPerMinute: 3, requestsPerMinute: 100_000);
        using var client = limited.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await client.LoginAsync($"inconnu-{attempt}@example.com", "MauvaisMotDePasse1");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var refused = await client.LoginAsync("inconnu@example.com", "MauvaisMotDePasse1");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal("Api.RateLimited", await refused.ReadErrorCodeAsync());
        Assert.True(refused.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task Too_many_requests_from_one_account_are_refused_without_blocking_the_others()
    {
        var factory = await fixture.GetFactoryAsync();
        var busy = await TestAccounts.CreateAsync(factory);
        var other = await TestAccounts.CreateAsync(factory);
        await using var limited = Limited(factory, loginPerMinute: 100_000, requestsPerMinute: 5);
        using var client = limited.CreateClient();
        var (busyToken, _) = await client.LoginOkAsync(busy.Email, busy.Password);
        var (otherToken, _) = await client.LoginOkAsync(other.Email, other.Password);

        for (var request = 0; request < 5; request++)
        {
            using var allowed = await client.MeAsync(busyToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        using var refused = await client.MeAsync(busyToken);
        using var stillFine = await client.MeAsync(otherToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stillFine.StatusCode);
    }

    /// <summary>Même API et même base, limites basses : seul ce test les subit.</summary>
    private static WebApplicationFactory<Program> Limited(HawalaApiFactory factory, int loginPerMinute, int requestsPerMinute) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimits:LoginPerMinute", loginPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimits:RequestsPerMinute", requestsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
}
