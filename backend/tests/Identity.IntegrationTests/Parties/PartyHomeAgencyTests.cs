using System.Net;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Rattachement d'un tiers à une agence (décisions du patron, 2 novembre 2026) : un agent ne voit que les tiers de son
/// agence et ceux des deux ; le Superviseur voit tout et change le rattachement ; un doublon rattaché à l'autre agence est
/// refusé avec un message clair. Marqueurs en lettres seulement (la recherche compare les chiffres aux téléphones).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PartyHomeAgencyTests(ApiFixture fixture)
{
    [Fact]
    public async Task An_agent_sees_only_the_parties_of_his_agency_and_those_of_both()
    {
        var ctx = await ArrangeAsync();
        var inNkc = await ctx.CreateAsync(ctx.SupervisorToken, $"Nkc {ctx.Tag}", ctx.Nkc);
        var inDkr = await ctx.CreateAsync(ctx.SupervisorToken, $"Dkr {ctx.Tag}", ctx.Dkr);
        var both = await ctx.CreateAsync(ctx.SupervisorToken, $"Both {ctx.Tag}", agency: null);

        Assert.Equal([both, inDkr], await ctx.ListAsync(ctx.DkrAgentToken));
        Assert.Equal([both, inNkc], await ctx.ListAsync(ctx.NkcAgentToken));
        Assert.Equal([both, inDkr, inNkc], await ctx.ListAsync(ctx.SupervisorToken));

        using var hidden = await ctx.Client.GetPartyAsync(ctx.DkrAgentToken, inNkc);
        using var visible = await ctx.Client.GetPartyAsync(ctx.DkrAgentToken, inDkr);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(ctx.Dkr, (await visible.ReadJsonAsync()).GetProperty("homeAgencyId").GetGuid());
    }

    [Fact]
    public async Task A_party_created_by_an_agent_belongs_to_his_agency_and_a_duplicate_elsewhere_is_explained()
    {
        var ctx = await ArrangeAsync();
        var phone = NewPhone();
        var created = await ctx.CreateAsync(ctx.NkcAgentToken, $"Agent {ctx.Tag}", ctx.Dkr, phone);

        using var read = await ctx.Client.GetPartyAsync(ctx.SupervisorToken, created);
        Assert.Equal(ctx.Nkc, (await read.ReadJsonAsync()).GetProperty("homeAgencyId").GetGuid());

        using var fromDakar = await ctx.Client.CreatePartyAsync(ctx.DkrAgentToken, new { name = "Doublon", type = "Client", phone });
        using var fromNouakchott = await ctx.Client.CreatePartyAsync(ctx.NkcAgentToken, new { name = "Doublon", type = "Client", phone });
        using var bySupervisor = await ctx.Client.CreatePartyAsync(ctx.SupervisorToken, new { name = "Doublon", type = "Client", phone });

        Assert.Equal(HttpStatusCode.Conflict, fromDakar.StatusCode);
        Assert.Equal("Parties.Party.ExistsInOtherAgency", await fromDakar.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.PhoneTaken", await fromNouakchott.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.PhoneTaken", await bySupervisor.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task The_supervisor_changes_the_home_agency_and_it_is_audited()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreateAsync(ctx.SupervisorToken, $"Move {ctx.Tag}", ctx.Nkc);

        using var toDakar = await ctx.SetAgencyAsync(ctx.SupervisorToken, party, ctx.Dkr);
        using var byAgent = await ctx.SetAgencyAsync(ctx.NkcAgentToken, party, null);
        using var unknown = await ctx.SetAgencyAsync(ctx.SupervisorToken, party, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NoContent, toDakar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Parties.Party.AgencyInvalid", await unknown.ReadErrorCodeAsync());
        Assert.Equal([party], await ctx.ListAsync(ctx.DkrAgentToken));
        Assert.Empty(await ctx.ListAsync(ctx.NkcAgentToken));

        using var toBoth = await ctx.SetAgencyAsync(ctx.SupervisorToken, party, null);
        Assert.Equal(HttpStatusCode.NoContent, toBoth.StatusCode);
        Assert.Equal([party], await ctx.ListAsync(ctx.NkcAgentToken));

        using var audit = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, $"/api/v1/audit?entityId={party}&action=Parties.Party.AgencyChanged");
        var changes = (await audit.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("changes")[0])
            .Select(c => $"{c.GetProperty("before").GetString()}>{c.GetProperty("after").GetString()}")
            .ToList();
        Assert.Equal(["DKR>both", "NKC>DKR"], changes);
    }

    [Fact]
    public async Task An_off_balance_sheet_party_is_seen_by_the_supervisor_only()
    {
        var ctx = await ArrangeAsync();
        var name = $"Hors {ctx.Tag}";
        var party = await ctx.CreateAsync(ctx.SupervisorToken, name, agency: null);
        using var update = await ctx.Client.UpdatePartyAsync(
            ctx.SupervisorToken, party, new { name, type = "Client", inBalanceSheet = false });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        Assert.Equal([party], await ctx.ListAsync(ctx.SupervisorToken));
        Assert.Empty(await ctx.ListAsync(ctx.NkcAgentToken));
        Assert.Empty(await ctx.ListAsync(ctx.DkrAgentToken));

        using var byAgent = await ctx.Client.GetPartyAsync(ctx.NkcAgentToken, party);
        using var bySupervisor = await ctx.Client.GetPartyAsync(ctx.SupervisorToken, party);
        Assert.Equal(HttpStatusCode.NotFound, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySupervisor.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewPhone() => $"+2224{Random.Shared.Next(1_000_000, 9_999_999)}";

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        var tag = new string([.. Enumerable.Range(0, 10).Select(_ => (char)('a' + Random.Shared.Next(26)))]);
        return new Context(client, token, nkcToken, dkrToken, nkc, dkr, tag);
    }

    private sealed record Context(
        HttpClient Client, string SupervisorToken, string NkcAgentToken, string DkrAgentToken, Guid Nkc, Guid Dkr, string Tag)
    {
        public async Task<Guid> CreateAsync(string token, string name, Guid? agency, string? phone = null)
        {
            using var response = await Client.CreatePartyAsync(token, new { name, type = "Client", phone, agencyId = agency });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        /// <summary>Ids des tiers visibles portant le marqueur du test, triés par nom.</summary>
        public async Task<List<Guid>> ListAsync(string token)
        {
            using var response = await Client.ListPartiesAsync(token, $"search={Tag}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];
        }

        public Task<HttpResponseMessage> SetAgencyAsync(string token, Guid party, Guid? agencyId) =>
            Client.SetPartyHomeAgencyAsync(token, party, agencyId);
    }
}
