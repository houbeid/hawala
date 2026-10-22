using System.Net;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class RefreshAndLogoutTests(ApiFixture fixture)
{
    [Fact]
    public async Task Refresh_issues_new_tokens_and_rotates_the_cookie()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, firstCookie) = await client.LoginOkAsync(account.Email, account.Password);

        using var response = await client.RefreshAsync(firstCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (accessToken, secondCookie) = await response.ReadSessionAsync();
        Assert.NotEqual(firstCookie, secondCookie);

        using var me = await client.MeAsync(accessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Replaying_a_rotated_cookie_closes_the_whole_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, firstCookie) = await client.LoginOkAsync(account.Email, account.Password);

        using var rotation = await client.RefreshAsync(firstCookie);
        var (_, secondCookie) = await rotation.ReadSessionAsync();

        // Un « voleur » rejoue l'ancien cookie.
        using var replay = await client.RefreshAsync(firstCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("Identity.RefreshToken.Reused", await replay.ReadErrorCodeAsync());

        // Toute la session est fermée : le cookie légitime ne marche plus non plus.
        using var legitimate = await client.RefreshAsync(secondCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimate.StatusCode);
        Assert.Equal("Identity.RefreshToken.Revoked", await legitimate.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Refresh_without_or_with_unknown_cookie_is_refused_and_clears_the_cookie()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var missing = await client.RefreshAsync(refreshCookie: null);
        using var unknown = await client.RefreshAsync("valeur-inventee-pour-le-test");

        Assert.Equal("Identity.RefreshToken.Invalid", await missing.ReadErrorCodeAsync());
        Assert.Equal("Identity.RefreshToken.Invalid", await unknown.ReadErrorCodeAsync());
        Assert.NotNull(unknown.GetRefreshCookieHeader());
        Assert.Null(unknown.GetRefreshCookie());
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, cookie) = await client.LoginOkAsync(account.Email, account.Password);

        using var logout = await client.LogoutAsync(cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.NotNull(logout.GetRefreshCookieHeader());
        Assert.Null(logout.GetRefreshCookie());

        using var refresh = await client.RefreshAsync(cookie);
        Assert.Equal("Identity.RefreshToken.Revoked", await refresh.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Logout_without_cookie_still_succeeds()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var logout = await client.LogoutAsync(refreshCookie: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }
}