using System.Net;
using System.Text.RegularExpressions;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed partial class UnblockUserTests(ApiFixture fixture)
{
    [GeneratedRegex("^[A-Za-z2-9]{3}-[A-Za-z2-9]{3}-[A-Za-z2-9]{3}$")]
    private static partial Regex TemporaryPasswordFormat();

    // --- Déblocage réussi --------------------------------------------------

    [Fact]
    public async Task Unblocking_gives_a_new_temporary_password_and_the_old_one_stops_working()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        using var blocked = await client.BlockUserAsync(supervisorToken, agent.Id);

        using var response = await client.UnblockUserAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;
        Assert.Matches(TemporaryPasswordFormat(), temporaryPassword);

        // Règle validée : l'ancien mot de passe n'est plus valable.
        using var oldPassword = await client.LoginAsync(agent.Email, agent.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        Assert.Equal("Identity.Auth.InvalidCredentials", await oldPassword.ReadErrorCodeAsync());

        // Le nouveau fonctionne, et doit être changé.
        using var login = await client.LoginAsync(agent.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());

        using var read = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal("Active", (await read.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Unblocking_lifts_the_lock_after_five_failures()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.LoginAsync(agent.Email, "MauvaisMotDePasse9");
        }

        using var blocked = await client.BlockUserAsync(supervisorToken, agent.Id);
        using var response = await client.UnblockUserAsync(supervisorToken, agent.Id);
        var temporaryPassword = (await response.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!;

        using var login = await client.LoginAsync(agent.Email, temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var read = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            (await read.ReadJsonAsync()).GetProperty("lockedUntil").ValueKind);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Unblocking_an_active_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.UnblockUserAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Identity.Account.NotBlocked", await response.ReadErrorCodeAsync());

        // Refus = rien n'a changé : l'ancien mot de passe fonctionne toujours.
        using var login = await client.LoginAsync(agent.Email, agent.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.UnblockUserAsync(supervisorToken, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_unblock_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var colleague = await TestAccounts.CreateAsync(factory);
        await TestAccounts.BlockAsync(factory, colleague.Id);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.UnblockUserAsync(agentToken, colleague.Id);

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
