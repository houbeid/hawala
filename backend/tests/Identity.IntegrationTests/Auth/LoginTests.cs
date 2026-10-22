using System.Net;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class LoginTests(ApiFixture fixture)
{
    [Fact]
    public async Task Valid_credentials_return_access_token_and_secure_refresh_cookie()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();

        using var response = await client.LoginAsync(account.Email, account.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.ReadJsonAsync();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.False(body.GetProperty("mustChangePassword").GetBoolean());

        // Le refresh token n'est jamais dans le corps JSON…
        Assert.False(body.TryGetProperty("refreshToken", out _));

        // …seulement dans un cookie que le JavaScript ne peut pas lire.
        var cookie = response.GetRefreshCookieHeader();
        Assert.NotNull(cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_works_with_phone_number_written_differently()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();

        // +222XXXXXXXX saisi avec « 00 » et des espaces.
        var typedPhone = "00" + account.Phone![1..4] + " " + account.Phone[4..];

        using var response = await client.LoginAsync(typedPhone, account.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_email()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();

        using var response = await client.LoginAsync(account.Email.ToUpperInvariant(), account.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_account_return_the_same_error()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();

        using var wrongPassword = await client.LoginAsync(account.Email, "MauvaisMotDePasse9");
        using var unknownAccount = await client.LoginAsync("inconnu@example.com", "MauvaisMotDePasse9");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownAccount.StatusCode);
        Assert.Equal("Identity.Auth.InvalidCredentials", await wrongPassword.ReadErrorCodeAsync());
        Assert.Equal("Identity.Auth.InvalidCredentials", await unknownAccount.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Malformed_login_and_empty_password_are_validation_errors()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var malformed = await client.LoginAsync("agent@", "MotDePasse1");
        using var emptyPassword = await client.LoginAsync("agent@example.com", "");

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal("Identity.Email.Invalid", await malformed.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, emptyPassword.StatusCode);
        Assert.Equal("Identity.Password.Empty", await emptyPassword.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Fifth_failure_locks_the_account_even_for_the_right_password()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var failed = await client.LoginAsync(account.Email, "MauvaisMotDePasse9");
            Assert.Equal("Identity.Auth.InvalidCredentials", await failed.ReadErrorCodeAsync());
        }

        using var fifth = await client.LoginAsync(account.Email, "MauvaisMotDePasse9");
        Assert.Equal("Identity.Auth.LockedOut", await fifth.ReadErrorCodeAsync());

        using var rightPassword = await client.LoginAsync(account.Email, account.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, rightPassword.StatusCode);
        Assert.Equal("Identity.Auth.LockedOut", await rightPassword.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Blocked_account_is_refused_but_only_revealed_after_the_right_password()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        await TestAccounts.BlockAsync(factory, account.Id);
        using var client = factory.CreateApiClient();

        using var wrongPassword = await client.LoginAsync(account.Email, "MauvaisMotDePasse9");
        using var rightPassword = await client.LoginAsync(account.Email, account.Password);

        Assert.Equal("Identity.Auth.InvalidCredentials", await wrongPassword.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, rightPassword.StatusCode);
        Assert.Equal("Identity.Auth.AccountBlocked", await rightPassword.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Seeded_supervisor_can_log_in_with_temporary_password()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.LoginAsync(
            HawalaApiFactory.SeedSupervisorEmail, HawalaApiFactory.SeedSupervisorPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
    }
}