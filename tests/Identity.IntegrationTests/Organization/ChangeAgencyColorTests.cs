using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Organization.Infrastructure;

namespace Identity.IntegrationTests.Organization;

/// <summary>
/// La base est partagée : MeTests, ListUsersTests et AgencyEndpointsTests vérifient les couleurs d'origine.
/// Tout test qui change une couleur la remet en place dans un finally.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ChangeAgencyColorTests(ApiFixture fixture)
{
    private const string NouakchottColor = "#1E8E3E";
    private const string DakarColor = "#1565C0";

    // --- Changement réussi -------------------------------------------------

    [Fact]
    public async Task Supervisor_changes_the_color_and_agents_see_it_without_reconnecting()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        try
        {
            using var response = await client.ChangeAgencyColorAsync(supervisorToken, nouakchott, "#ff8800");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("#FF8800", await ReadColorAsync(client, supervisorToken, "NKC"));

            // Même session : /me relit la couleur à chaque appel.
            using var me = await client.MeAsync(agentToken);
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
            Assert.Equal("#FF8800", (await me.ReadJsonAsync()).GetProperty("agency").GetProperty("color").GetString());
        }
        finally
        {
            await RestoreNouakchottAsync(client, supervisorToken, nouakchott);
        }
    }

    [Fact]
    public async Task Keeping_its_own_color_is_accepted()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeAgencyColorAsync(
            supervisorToken, await GetAgencyIdAsync(factory, "DKR"), DakarColor.ToLowerInvariant());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(DakarColor, await ReadColorAsync(client, supervisorToken, "DKR"));
    }

    [Fact]
    public async Task Restarting_the_api_does_not_overwrite_a_changed_color()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        try
        {
            using var response = await client.ChangeAgencyColorAsync(supervisorToken, nouakchott, "#6A1B9A");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

            // Comme un redémarrage : migrations + création des agences relancées sur la même base.
            await factory.Services.InitializeOrganizationModuleAsync();

            Assert.Equal("#6A1B9A", await ReadColorAsync(client, supervisorToken, "NKC"));
        }
        finally
        {
            await RestoreNouakchottAsync(client, supervisorToken, nouakchott);
        }
    }

    // --- Refus -------------------------------------------------------------

    [Theory]
    [InlineData(DakarColor)]
    [InlineData("#1565c0")]
    public async Task Color_of_the_other_agency_is_refused(string color)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        try
        {
            using var response = await client.ChangeAgencyColorAsync(supervisorToken, nouakchott, color);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("Organization.Color.AlreadyUsed", await response.ReadErrorCodeAsync());
            Assert.Equal(NouakchottColor, await ReadColorAsync(client, supervisorToken, "NKC"));
        }
        finally
        {
            await RestoreNouakchottAsync(client, supervisorToken, nouakchott);
        }
    }

    [Theory]
    [InlineData("rouge")]
    [InlineData("#12345")]
    [InlineData("")]
    public async Task Invalid_color_is_refused(string color)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeAgencyColorAsync(
            supervisorToken, await GetAgencyIdAsync(factory, "NKC"), color);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Organization.Color.Invalid", await response.ReadErrorCodeAsync());
        Assert.Equal(NouakchottColor, await ReadColorAsync(client, supervisorToken, "NKC"));
    }

    [Fact]
    public async Task Unknown_agency_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeAgencyColorAsync(supervisorToken, Guid.NewGuid(), "#123456");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Organization.Agency.NotFound", await response.ReadErrorCodeAsync());
    }

    // --- Qui a le droit ----------------------------------------------------

    [Fact]
    public async Task Agent_can_read_agencies_but_not_change_a_color()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var read = await client.GetAgenciesAsync(agentToken);
        using var change = await client.ChangeAgencyColorAsync(
            agentToken, await GetAgencyIdAsync(factory, "NKC"), "#123456");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, change.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_change_a_color()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.ChangeAgencyColorAsync(
            accessToken: null, await GetAgencyIdAsync(factory, "NKC"), "#123456");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static async Task RestoreNouakchottAsync(HttpClient client, string supervisorToken, Guid nouakchott)
    {
        using var restored = await client.ChangeAgencyColorAsync(supervisorToken, nouakchott, NouakchottColor);
        Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
    }

    private static async Task<string?> ReadColorAsync(HttpClient client, string token, string code)
    {
        using var response = await client.GetAgenciesAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync())
            .EnumerateArray()
            .Single(a => a.GetProperty("code").GetString() == code)
            .GetProperty("color")
            .GetString();
    }

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
