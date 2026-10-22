using System.Net;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class ChangePasswordTests(ApiFixture fixture)
{
    private const string NewPassword = "NouveauMotDePasse2";

    [Fact]
    public async Task Temporary_password_only_allows_changing_the_password()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory, mustChangePassword: true);
        using var client = factory.CreateApiClient();

        using var login = await client.LoginAsync(account.Email, account.Password);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        var (temporaryToken, _) = await login.ReadSessionAsync();

        using var meBefore = await client.MeAsync(temporaryToken);
        Assert.Equal(HttpStatusCode.Forbidden, meBefore.StatusCode);

        using var change = await client.ChangePasswordAsync(temporaryToken, account.Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.False((await change.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        var (newToken, _) = await change.ReadSessionAsync();

        using var meAfter = await client.MeAsync(newToken);
        Assert.Equal(HttpStatusCode.OK, meAfter.StatusCode);
        var me = await meAfter.ReadJsonAsync();
        Assert.Equal(account.Id, me.GetProperty("id").GetGuid());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Wrong_current_password_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var change = await client.ChangePasswordAsync(token, "PasLeBonMotDePasse3", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal("Identity.Password.Incorrect", await change.ReadErrorCodeAsync());
    }

    [Theory]
    [InlineData("court12")]        // 7 caractères
    [InlineData("sanschiffre")]    // aucun chiffre
    public async Task Weak_new_password_is_refused(string weakPassword)
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var change = await client.ChangePasswordAsync(token, account.Password, weakPassword);

        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Equal("Identity.Password.TooWeak", await change.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Changing_password_closes_every_other_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();

        // Deux appareils connectés au même compte.
        var (phoneToken, _) = await client.LoginOkAsync(account.Email, account.Password);
        var (_, laptopCookie) = await client.LoginOkAsync(account.Email, account.Password);

        using var change = await client.ChangePasswordAsync(phoneToken, account.Password, NewPassword);
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var (newToken, _) = await change.ReadSessionAsync();

        // L'autre appareil ne peut plus rafraîchir sa session.
        using var laptopRefresh = await client.RefreshAsync(laptopCookie);
        Assert.Equal("Identity.RefreshToken.Revoked", await laptopRefresh.ReadErrorCodeAsync());

        // L'ancien jeton d'accès est refusé tout de suite (SecurityVersion a changé).
        using var oldTokenMe = await client.MeAsync(phoneToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldTokenMe.StatusCode);

        using var newTokenMe = await client.MeAsync(newToken);
        Assert.Equal(HttpStatusCode.OK, newTokenMe.StatusCode);

        // L'ancien mot de passe ne marche plus, le nouveau oui.
        using var oldPasswordLogin = await client.LoginAsync(account.Email, account.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPasswordLogin.StatusCode);
        using var newPasswordLogin = await client.LoginAsync(account.Email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, newPasswordLogin.StatusCode);
    }
}