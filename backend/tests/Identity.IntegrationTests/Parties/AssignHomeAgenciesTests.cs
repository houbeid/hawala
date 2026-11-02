using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Mise en service (2 novembre 2026) : les tiers rattachés aux deux agences le sont d'après leurs soldes. Ce test a SA PROPRE
/// base (IClassFixture, hors de la collection partagée) : le rattachement porte sur tous les tiers de la base.
/// </summary>
public sealed class AssignHomeAgenciesTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Parties_with_a_balance_in_one_agency_only_are_attached_to_it()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var onlyNkc = await CreateAsync(client, token, "Seulement Nouakchott");
        var onlyDkr = await CreateAsync(client, token, "Seulement Dakar");
        var inBoth = await CreateAsync(client, token, "Les deux");
        var none = await CreateAsync(client, token, "Aucun solde");
        await OpenAsync(client, token, onlyNkc, nkc, 1_000m);
        await OpenAsync(client, token, onlyDkr, dkr, -5_000m);
        await OpenAsync(client, token, inBoth, nkc, 10m);
        await OpenAsync(client, token, inBoth, dkr, 20m);

        using var byAgent = await client.AssignHomeAgenciesAsync(agentToken);
        using var response = await client.AssignHomeAgenciesAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.ReadJsonAsync();
        Assert.Equal(2, result.GetProperty("assigned").GetInt32());
        Assert.Equal(2, result.GetProperty("keptBoth").GetInt32());
        Assert.Equal(nkc, (await HomeAsync(client, token, onlyNkc)).GetGuid());
        Assert.Equal(dkr, (await HomeAsync(client, token, onlyDkr)).GetGuid());
        Assert.Equal(JsonValueKind.Null, (await HomeAsync(client, token, inBoth)).ValueKind);
        Assert.Equal(JsonValueKind.Null, (await HomeAsync(client, token, none)).ValueKind);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string token, string name)
    {
        using var response = await client.CreatePartyAsync(token, new { name, type = "Client" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task OpenAsync(HttpClient client, string token, Guid party, Guid agency, decimal amount)
    {
        using var response = await client.SetOpeningBalanceAsync(token, party, agency, new { amount });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<JsonElement> HomeAsync(HttpClient client, string token, Guid party)
    {
        using var response = await client.GetPartyAsync(token, party);
        return (await response.ReadJsonAsync()).GetProperty("homeAgencyId").Clone();
    }
}
