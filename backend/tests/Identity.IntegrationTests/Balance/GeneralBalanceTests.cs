using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Balance;

/// <summary>
/// Bilan général (décisions du 31 octobre 2026) : Nouakchott en MRO + Dakar converti au taux du bilan envoyé par l'écran ;
/// résultat net = bilan général − (capital général en MRO + apports nets du patron des deux caisses) ; une photo par jour
/// avec son taux ; Superviseur seulement. La base est partagée : les tests comparent au budget de la Caisse et mesurent des écarts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GeneralBalanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task General_balance_adds_nouakchott_and_dakar_converted_at_the_given_rate()
    {
        var ctx = await ArrangeAsync();
        var nkcBudget = await ctx.BudgetAsync(ctx.Nkc);
        var dkrBudget = await ctx.BudgetAsync(ctx.Dkr);

        var general = await ctx.GeneralAsync(3600m);

        Assert.Equal(3600m, general.GetProperty("rate").GetDecimal());
        Assert.Equal("MRO", general.GetProperty("nouakchott").GetProperty("currency").GetString());
        Assert.Equal("XOF", general.GetProperty("dakar").GetProperty("currency").GetString());
        Assert.Equal(nkcBudget, general.GetProperty("nouakchott").GetProperty("budget").GetDecimal());
        Assert.Equal(dkrBudget, general.GetProperty("dakar").GetProperty("budget").GetDecimal());
        Assert.Equal(dkrBudget * 0.72m, general.GetProperty("dakarInMro").GetProperty("budget").GetDecimal());
        Assert.Equal(nkcBudget + dkrBudget * 0.72m, general.GetProperty("generalBalance").GetDecimal());

        // Chaque composant total = Nouakchott + Dakar converti ; le total redonne le bilan général.
        foreach (var name in new[] { "cash", "banks", "debtors", "creditors", "undelivered" })
        {
            Assert.Equal(
                general.GetProperty("nouakchott").GetProperty(name).GetDecimal()
                    + general.GetProperty("dakarInMro").GetProperty(name).GetDecimal(),
                general.GetProperty("total").GetProperty(name).GetDecimal());
        }

        Assert.Equal(general.GetProperty("generalBalance").GetDecimal(), general.GetProperty("total").GetProperty("budget").GetDecimal());
    }

    [Fact]
    public async Task General_capital_and_owner_contributions_give_the_net_result_and_the_capital_is_audited()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetGeneralCapitalAsync(165_500_000m, "Mise en service");
        await ctx.SetGeneralCapitalAsync(170_000_000m, "Correction");

        using (var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/general/capital"))
        {
            var capital = await response.ReadJsonAsync();
            Assert.Equal(170_000_000m, capital.GetProperty("current").GetDecimal());
            Assert.Equal("Correction", capital.GetProperty("history")[0].GetProperty("reason").GetString());
            Assert.Equal(165_500_000m, capital.GetProperty("history")[1].GetProperty("amount").GetDecimal());
        }

        var before = await ctx.GeneralAsync(3675.25m);
        Assert.Equal(170_000_000m, before.GetProperty("capital").GetDecimal());
        var capitalUsed = before.GetProperty("capitalUsed").GetDecimal();
        Assert.Equal(170_000_000m + before.GetProperty("ownerContributionsMro").GetDecimal(), capitalUsed);
        var netResult = before.GetProperty("netResult").GetDecimal();
        Assert.Equal(before.GetProperty("generalBalance").GetDecimal() - capitalUsed, netResult);
        Assert.Equal(netResult / capitalUsed * 100m, before.GetProperty("netResultPercent").GetDecimal());

        // Un approvisionnement de Nouakchott fait monter le bilan et le capital pris en compte, pas le résultat.
        await ctx.OwnerContributionAsync(1_000_000m);
        var after = await ctx.GeneralAsync(3675.25m);
        Assert.Equal(1_000_000m, after.GetProperty("generalBalance").GetDecimal() - before.GetProperty("generalBalance").GetDecimal());
        Assert.Equal(1_000_000m, after.GetProperty("capitalUsed").GetDecimal() - capitalUsed);
        Assert.Equal(netResult, after.GetProperty("netResult").GetDecimal());

        using var audit = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/audit?action=Balance.GeneralCapital");
        var lines = (await audit.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Where(l => l.GetProperty("actor").GetProperty("id").GetGuid() == ctx.SupervisorId)
            .ToList();
        Assert.Contains(lines, l => l.GetProperty("reason").GetString() == "Correction");
    }

    [Fact]
    public async Task Saving_keeps_one_general_snapshot_per_day_with_its_rate_and_each_caisse_in_its_currency()
    {
        var ctx = await ArrangeAsync();

        var first = await ctx.SaveGeneralAsync(3600m);
        var second = await ctx.SaveGeneralAsync(3675.25m);
        var live = await ctx.GeneralAsync(3675.25m);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(3675.25m, second.GetProperty("rate").GetDecimal());
        Assert.Equal(live.GetProperty("generalBalance").GetDecimal(), second.GetProperty("generalBalance").GetDecimal());
        Assert.Equal("XOF", second.GetProperty("dakar").GetProperty("currency").GetString());
        Assert.Equal(live.GetProperty("dakar").GetProperty("budget").GetDecimal(), second.GetProperty("dakar").GetProperty("budget").GetDecimal());
        Assert.Equal("MRO", second.GetProperty("total").GetProperty("currency").GetString());
        Assert.Equal(live.GetProperty("total").GetProperty("debtors").GetDecimal(), second.GetProperty("total").GetProperty("debtors").GetDecimal());

        using var history = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/general/snapshots");
        var rows = (await history.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Equal(second.GetProperty("id").GetGuid(), rows[^1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Bad_inputs_and_other_roles_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var noRate = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/general");
        using var tooPrecise = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/general?rate=3600.123");
        using var saveWithoutRate = await ctx.Client.PostJsonAsync(ctx.SupervisorToken, "/api/v1/balance/general/snapshots", new { });
        using var zeroCapital = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, "/api/v1/balance/general/capital", new { amount = 0m });
        using var badPeriod = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, "/api/v1/balance/general/snapshots?from=2026-10-10&to=2026-10-01");
        using var agentReads = await ctx.Client.SendGetAsync(ctx.AgentToken, "/api/v1/balance/general?rate=3600");
        using var agentSetsCapital = await ctx.Client.PutJsonAsync(ctx.AgentToken, "/api/v1/balance/general/capital", new { amount = 1m });

        Assert.Equal("Balance.Rate.Required", await noRate.ReadErrorCodeAsync());
        Assert.Equal("Money.Rate.Invalid", await tooPrecise.ReadErrorCodeAsync());
        Assert.Equal("Balance.Rate.Required", await saveWithoutRate.ReadErrorCodeAsync());
        Assert.Equal("Balance.GeneralCapital.AmountInvalid", await zeroCapital.ReadErrorCodeAsync());
        Assert.Equal("Balance.Filter.PeriodInvalid", await badPeriod.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, agentReads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentSetsCapital.StatusCode);
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

        using var cash = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = nkc });
        var cashId = (await cash.ReadJsonAsync()).GetProperty("id").GetGuid();

        return new Context(client, token, supervisor.Id, agentToken, nkc, dkr, cashId);
    }

    private sealed record Context(
        HttpClient Client, string SupervisorToken, Guid SupervisorId, string AgentToken, Guid Nkc, Guid Dkr, Guid NkcCash)
    {
        public async Task<decimal> BudgetAsync(Guid agencyId)
        {
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/cash-budget?agencyId={agencyId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("budget").GetDecimal();
        }

        public async Task<JsonElement> GeneralAsync(decimal rate)
        {
            using var response = await Client.SendGetAsync(
                SupervisorToken, $"/api/v1/balance/general?rate={rate.ToString(CultureInfo.InvariantCulture)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<JsonElement> SaveGeneralAsync(decimal rate)
        {
            using var response = await Client.PostJsonAsync(SupervisorToken, "/api/v1/balance/general/snapshots", new { rate });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task SetGeneralCapitalAsync(decimal amount, string? reason)
        {
            using var response = await Client.PutJsonAsync(SupervisorToken, "/api/v1/balance/general/capital", new { amount, reason });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task OwnerContributionAsync(decimal amount)
        {
            using var response = await Client.CreateCashMovementAsync(
                AgentToken, new { type = "OwnerContribution", treasuryAccountId = NkcCash, amount }, Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }
}
