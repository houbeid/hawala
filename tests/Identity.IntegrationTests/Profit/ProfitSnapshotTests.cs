using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Profit;

/// <summary>
/// « Enregistrer le bénéfice du jour » (validé le 21 octobre 2026) : photo du jour courant, pour une agence ou les deux,
/// remplacée si on la réenregistre le même jour ; colonne « Enregistré » dans le rapport ; historique.
/// La base est partagée et d'autres tests créent des transferts aujourd'hui : on compare la photo au rapport du même moment
/// et on mesure des écarts. Taux d'aujourd'hui : vente 3 650, achat 3 600 (5 000 000 FCFA envoyés de Nouakchott = 50 000 MRO).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProfitSnapshotTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_day_is_saved_for_an_agency_and_shown_in_the_report()
    {
        var ctx = await ArrangeAsync();
        await ctx.TransferTodayAsync();
        var before = await ctx.TodayLineAsync(ctx.Nkc);

        var saved = await ctx.SaveAsync(ctx.Nkc);

        Assert.Equal(ctx.Nkc, saved.GetProperty("agencyId").GetGuid());
        Assert.Equal(ctx.TodayText, saved.GetProperty("date").GetString());
        Assert.Equal(before.GetProperty("profitMro").GetDecimal(), saved.GetProperty("profitMro").GetDecimal());
        Assert.Equal(before.GetProperty("transferCount").GetInt32(), saved.GetProperty("transferCount").GetInt32());

        var after = await ctx.TodayLineAsync(ctx.Nkc);
        Assert.Equal(saved.GetProperty("profitMro").GetDecimal(), after.GetProperty("savedProfitMro").GetDecimal());
        Assert.Equal(JsonValueKind.String, after.GetProperty("savedAt").ValueKind);
    }

    [Fact]
    public async Task Saving_again_the_same_day_replaces_the_snapshot()
    {
        var ctx = await ArrangeAsync();
        await ctx.TransferTodayAsync();
        var first = await ctx.SaveAsync(ctx.Nkc);

        await ctx.TransferTodayAsync();
        var second = await ctx.SaveAsync(ctx.Nkc);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(50_000m, second.GetProperty("profitMro").GetDecimal() - first.GetProperty("profitMro").GetDecimal());
        Assert.Equal(1, second.GetProperty("transferCount").GetInt32() - first.GetProperty("transferCount").GetInt32());

        using var history = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/profit/snapshots?agencyId={ctx.Nkc}");
        var today = (await history.ReadJsonAsync()).EnumerateArray().Where(s => s.GetProperty("date").GetString() == ctx.TodayText);
        Assert.Equal(second.GetProperty("profitMro").GetDecimal(), Assert.Single(today).GetProperty("profitMro").GetDecimal());
    }

    [Fact]
    public async Task The_general_snapshot_is_separate_from_the_agency_snapshots()
    {
        var ctx = await ArrangeAsync();
        await ctx.TransferTodayAsync();
        var both = await ctx.TodayLineAsync(agency: null);

        var general = await ctx.SaveAsync(agency: null);
        var nkc = await ctx.SaveAsync(ctx.Nkc);

        Assert.Equal(JsonValueKind.Null, general.GetProperty("agencyId").ValueKind);
        Assert.NotEqual(general.GetProperty("id").GetGuid(), nkc.GetProperty("id").GetGuid());
        Assert.Equal(both.GetProperty("profitMro").GetDecimal(), general.GetProperty("profitMro").GetDecimal());
    }

    [Fact]
    public async Task Only_the_supervisor_saves_the_profit()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.PostJsonAsync(ctx.AgentToken, "/api/v1/profit/snapshots", new { });
        using var unknownAgency = await ctx.Client.PostJsonAsync(
            ctx.SupervisorToken, "/api/v1/profit/snapshots", new { agencyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Profit.Filter.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        // Mêmes taux que les autres tests qui fixent le taux d'aujourd'hui.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var rate = await client.SetDailyRateAsync(token, today, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        using var cash = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = nkc });
        var cashId = (await cash.ReadJsonAsync()).GetProperty("id").GetGuid();

        return new Context(client, token, agentToken, nkc, dkr, cashId, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed record Context(
        HttpClient Client, string SupervisorToken, string AgentToken, Guid Nkc, Guid Dkr, Guid NkcCash, string TodayText)
    {
        /// <summary>Nouakchott → Dakar aujourd'hui (maintenant), 5 000 000 FCFA = 3 650 000 MRO : 50 000 MRO de bénéfice.</summary>
        public async Task TransferTodayAsync()
        {
            using var response = await Client.CreateTransferAsync(
                AgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 5_000_000m,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 3_650_000m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        public async Task<JsonElement> TodayLineAsync(Guid? agency)
        {
            var query = agency is { } id ? $"?agencyId={id}" : string.Empty;
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/profit{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single((await response.ReadJsonAsync()).GetProperty("days").EnumerateArray());
        }

        public async Task<JsonElement> SaveAsync(Guid? agency)
        {
            using var response = await Client.PostJsonAsync(SupervisorToken, "/api/v1/profit/snapshots", new { agencyId = agency });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
    }
}
