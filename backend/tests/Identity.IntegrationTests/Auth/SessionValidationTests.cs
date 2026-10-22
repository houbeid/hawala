using System.Net;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class SessionValidationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Blocking_an_account_rejects_its_access_token_immediately()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (token, cookie) = await client.LoginOkAsync(account.Email, account.Password);

        using var before = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        await TestAccounts.BlockAsync(factory, account.Id);

        // Le JWT n'a pas expiré, mais il est refusé dès la requête suivante (option B).
        using var after = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        using var refresh = await client.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.Forbidden, refresh.StatusCode);
        Assert.Equal("Identity.Auth.AccountBlocked", await refresh.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Missing_or_forged_token_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var missing = await client.MeAsync(accessToken: null);
        using var forged = await client.MeAsync("pas-un-vrai-jeton");

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
    }
}