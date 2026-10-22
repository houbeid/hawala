using System.Net;
using System.Text.RegularExpressions;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed partial class ResetUserPasswordTests(ApiFixture fixture)
{
    [GeneratedRegex("^[A-Za-z2-9]{3}-[A-Za-z2-9]{3}-[A-Za-z2-9]{3}$")]
    private static partial Regex TemporaryPasswordFormat();

    // --- Réinitialisation réussie ------------------------------------------

    [Fact]
    public async Task Reset_gives_a_new_temporary_password_and_cuts_every_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ResetUserPasswordAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;
        Assert.Matches(TemporaryPasswordFormat(), temporaryPassword);

        // Toutes les sessions sont coupées.
        using var me = await client.MeAsync(agentToken);
        using var refresh = await client.RefreshAsync(agentCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, refresh.StatusCode);

        // L'ancien mot de passe ne marche plus ; le nouveau doit être changé.
        using var oldPassword = await client.LoginAsync(agent.Email, agent.Password);
        Assert.Equal("Identity.Auth.InvalidCredentials", await oldPassword.ReadErrorCodeAsync());

        using var login = await client.LoginAsync(agent.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Reset_lifts_the_lock_after_five_failures()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.LoginAsync(agent.Email, "MauvaisMotDePasse9");
        }

        using var response = await client.ResetUserPasswordAsync(supervisorToken, agent.Id);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;

        using var login = await client.LoginAsync(agent.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Blocked_account_can_be_reset_but_stays_blocked()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        await TestAccounts.BlockAsync(factory, agent.Id);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ResetUserPasswordAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;

        // Le bon (nouveau) mot de passe révèle le blocage : la réinitialisation ne débloque pas.
        using var login = await client.LoginAsync(agent.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("Identity.Auth.AccountBlocked", await login.ReadErrorCodeAsync());

        using var read = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal("Blocked", (await read.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Supervisor_can_reset_another_supervisor()
    {
        var factory = await fixture.GetFactoryAsync();
        var other = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ResetUserPasswordAsync(supervisorToken, other.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;
        using var login = await client.LoginAsync(other.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Supervisor_cannot_reset_own_password()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.ResetUserPasswordAsync(token, supervisor.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Identity.Account.CannotResetOwnPassword", await response.ReadErrorCodeAsync());

        // Rien n'a changé : son mot de passe et sa session marchent encore.
        using var me = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var login = await client.LoginAsync(supervisor.Email, supervisor.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ResetUserPasswordAsync(supervisorToken, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_reset_passwords()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var colleague = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.ResetUserPasswordAsync(agentToken, colleague.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }
}
