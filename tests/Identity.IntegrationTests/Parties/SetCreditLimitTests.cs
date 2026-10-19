using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Plafond de crédit d'un tiers par agence (enregistré maintenant, appliqué en phase 3).
/// Règles validées : positif ou nul ; 0 = aucun crédit ; vide = pas de plafond.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SetCreditLimitTests(ApiFixture fixture)
{
    [Fact]
    public async Task Credit_limit_is_set_per_agency_and_shown_next_to_its_balance()
    {
        var (_, client, token, partyId, nouakchott, dakar) = await ArrangeAsync();

        using var nkc = await client.SetCreditLimitAsync(token, partyId, nouakchott, 500_000.50m);
        using var dkr = await client.SetCreditLimitAsync(token, partyId, dakar, 0m);

        Assert.Equal(HttpStatusCode.NoContent, nkc.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, dkr.StatusCode);
        var party = await ReadAsync(client, token, partyId);
        Assert.Equal(500_000.50m, LimitOf(party, nouakchott).GetDecimal());
        Assert.Equal(0m, LimitOf(party, dakar).GetDecimal());
    }

    [Fact]
    public async Task Empty_credit_limit_removes_it()
    {
        var (_, client, token, partyId, nouakchott, _) = await ArrangeAsync();
        using var set = await client.SetCreditLimitAsync(token, partyId, nouakchott, 100_000m);

        using var cleared = await client.SetCreditLimitAsync(token, partyId, nouakchott, null);

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, LimitOf(await ReadAsync(client, token, partyId), nouakchott).ValueKind);
    }

    [Fact]
    public async Task Negative_or_too_precise_credit_limit_is_refused()
    {
        var (_, client, token, partyId, nouakchott, dakar) = await ArrangeAsync();

        using var negative = await client.SetCreditLimitAsync(token, partyId, nouakchott, -1m);
        using var xofDecimals = await client.SetCreditLimitAsync(token, partyId, dakar, 1_000.5m);
        using var unknownAgency = await client.SetCreditLimitAsync(token, partyId, Guid.NewGuid(), 1_000m);
        using var unknownParty = await client.SetCreditLimitAsync(token, Guid.NewGuid(), nouakchott, 1_000m);

        Assert.Equal("Parties.Party.CreditLimitNegative", await negative.ReadErrorCodeAsync());
        Assert.Equal("Money.Amount.XofNotInteger", await xofDecimals.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.AgencyAccountNotFound", await unknownAgency.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NotFound", await unknownParty.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_set_a_credit_limit()
    {
        var (factory, client, _, partyId, nouakchott, _) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.SetCreditLimitAsync(agentToken, partyId, nouakchott, 1_000m);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token, Guid PartyId, Guid Nouakchott, Guid Dakar)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var created = await client.CreatePartyAsync(token, new { name = "Plafond", type = "Client" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var partyId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return (factory, client, token, partyId, nouakchott, dakar);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string token, Guid id)
    {
        using var response = await client.GetPartyAsync(token, id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static JsonElement LimitOf(JsonElement party, Guid agencyId) =>
        party.GetProperty("balances").EnumerateArray()
            .Single(b => b.GetProperty("agencyId").GetGuid() == agencyId)
            .GetProperty("creditLimit");
}
