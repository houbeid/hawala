using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed class ChangeUserAgencyTests(ApiFixture fixture)
{
    // --- Déplacement réussi ------------------------------------------------

    [Fact]
    public async Task Moved_agent_gets_the_new_agency_after_refresh()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (agentToken, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");

        using var response = await client.ChangeUserAgencyAsync(supervisorToken, agent.Id, new { agencyId = dakar });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // L'ancien jeton est refusé tout de suite ; le refresh donne le nouveau périmètre.
        using var oldMe = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldMe.StatusCode);

        using var refresh = await client.RefreshAsync(agentCookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var (newToken, _) = await refresh.ReadSessionAsync();
        using var me = await client.MeAsync(newToken);
        Assert.Equal("DKR", (await me.ReadJsonAsync()).GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Same_agency_changes_nothing_and_keeps_the_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserAgencyAsync(supervisorToken, agent.Id, new { agencyId = agent.AgencyId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var me = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Supervisor_cannot_be_given_an_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        var other = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserAgencyAsync(
            supervisorToken, other.Id, new { agencyId = await GetAgencyIdAsync(factory, "DKR") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Identity.Account.AgencyNotAllowedForSupervisor", await response.ReadErrorCodeAsync());

        using var read = await client.GetUserAsync(supervisorToken, other.Id);
        Assert.Equal(JsonValueKind.Null, (await read.ReadJsonAsync()).GetProperty("agency").ValueKind);
    }

    [Fact]
    public async Task Missing_or_unknown_agency_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var missing = await client.ChangeUserAgencyAsync(supervisorToken, agent.Id, new { });
        using var unknown = await client.ChangeUserAgencyAsync(supervisorToken, agent.Id, new { agencyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Identity.Account.AgencyRequired", await missing.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Identity.Account.AgencyNotFound", await unknown.ReadErrorCodeAsync());

        using var read = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal("NKC", (await read.ReadJsonAsync()).GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserAgencyAsync(
            supervisorToken, Guid.NewGuid(), new { agencyId = await GetAgencyIdAsync(factory, "DKR") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_move_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.ChangeUserAgencyAsync(
            agentToken, agent.Id, new { agencyId = await GetAgencyIdAsync(factory, "DKR") });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }

    private static async Task<Guid> GetAgencyIdAsync(HawalaApiFactory factory, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, code);
    }
}
