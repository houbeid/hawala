using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Balance;

/// <summary>
/// Bilan par caisse (décisions du 21 octobre 2026 : deux caisses, deux capitaux, pas de total converti) : pour chaque agence,
/// budget de la caisse − (capital initial de la caisse + apports nets du patron), dans sa devise ; photo par caisse et par jour ;
/// Superviseur seulement. La base est partagée : les tests vérifient des égalités (bilan = budget de la Caisse) et des écarts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BalanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task Each_caisse_is_in_its_own_currency_with_its_cash_budget()
    {
        var ctx = await ArrangeAsync();
        var nkcBudget = await ctx.BudgetAsync(ctx.Nkc);
        var dkrBudget = await ctx.BudgetAsync(ctx.Dkr);

        var overview = await ctx.BalanceAsync();
        var nkc = Caisse(overview, ctx.Nkc);
        var dkr = Caisse(overview, ctx.Dkr);

        Assert.Equal(2, overview.GetProperty("caisses").GetArrayLength());
        Assert.False(overview.TryGetProperty("generalBalance", out _));
        Assert.Equal("MRO", nkc.GetProperty("currency").GetString());
        Assert.Equal("XOF", dkr.GetProperty("currency").GetString());
        Assert.Equal(nkcBudget, nkc.GetProperty("budget").GetDecimal());
        Assert.Equal(dkrBudget, dkr.GetProperty("budget").GetDecimal());
    }

    [Fact]
    public async Task Each_caisse_has_its_own_capital_with_its_history_and_net_result()
    {
        var ctx = await ArrangeAsync();

        await ctx.SetCapitalAsync(ctx.Nkc, 100_000_000m, "Mise en service");
        await ctx.SetCapitalAsync(ctx.Nkc, 165_500_000m, "Correction");
        await ctx.SetCapitalAsync(ctx.Dkr, 20_000_000m, null);

        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/capital");
        var capital = await response.ReadJsonAsync();
        var nkcCapital = Caisse(capital, ctx.Nkc);
        Assert.Equal(165_500_000m, nkcCapital.GetProperty("current").GetDecimal());
        Assert.Equal("MRO", nkcCapital.GetProperty("currency").GetString());
        var history = nkcCapital.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal("Correction", history[0].GetProperty("reason").GetString());
        Assert.Equal(100_000_000m, history[1].GetProperty("amount").GetDecimal());
        var dkrCapital = Caisse(capital, ctx.Dkr);
        Assert.Equal(20_000_000m, dkrCapital.GetProperty("current").GetDecimal());
        Assert.Equal("XOF", dkrCapital.GetProperty("history")[0].GetProperty("currency").GetString());

        var overview = await ctx.BalanceAsync();
        AssertNetResult(Caisse(overview, ctx.Nkc), 165_500_000m);
        AssertNetResult(Caisse(overview, ctx.Dkr), 20_000_000m);
    }

    [Fact]
    public async Task An_owner_contribution_raises_the_capital_of_its_caisse_not_the_result()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetCapitalAsync(ctx.Nkc, 165_500_000m, null);
        var before = await ctx.BalanceAsync();

        await ctx.OwnerContributionAsync(1_000_000m);
        var after = await ctx.BalanceAsync();

        var nkcBefore = Caisse(before, ctx.Nkc);
        var nkcAfter = Caisse(after, ctx.Nkc);
        Assert.Equal(1_000_000m, nkcAfter.GetProperty("budget").GetDecimal() - nkcBefore.GetProperty("budget").GetDecimal());
        Assert.Equal(1_000_000m, nkcAfter.GetProperty("capitalUsed").GetDecimal() - nkcBefore.GetProperty("capitalUsed").GetDecimal());
        Assert.Equal(nkcBefore.GetProperty("netResult").GetDecimal(), nkcAfter.GetProperty("netResult").GetDecimal());
        Assert.Equal(
            Caisse(before, ctx.Dkr).GetProperty("ownerContributions").GetDecimal(),
            Caisse(after, ctx.Dkr).GetProperty("ownerContributions").GetDecimal());
    }

    [Fact]
    public async Task Saving_keeps_one_snapshot_per_caisse_and_day_replaced_the_same_day()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetCapitalAsync(ctx.Dkr, 20_000_000m, null);
        var live = Caisse(await ctx.BalanceAsync(), ctx.Dkr);

        var first = await ctx.SaveAsync(ctx.Dkr);
        await ctx.SetCapitalAsync(ctx.Dkr, 25_000_000m, null);
        var second = await ctx.SaveAsync(ctx.Dkr);
        var nkc = await ctx.SaveAsync(ctx.Nkc);

        Assert.Equal(live.GetProperty("budget").GetDecimal(), first.GetProperty("budget").GetDecimal());
        Assert.Equal("XOF", first.GetProperty("currency").GetString());
        Assert.Equal(20_000_000m, first.GetProperty("initialCapital").GetDecimal());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(25_000_000m, second.GetProperty("initialCapital").GetDecimal());
        Assert.NotEqual(second.GetProperty("id").GetGuid(), nkc.GetProperty("id").GetGuid());

        using var dkrHistory = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/balance/snapshots?agencyId={ctx.Dkr}");
        var dkrRows = (await dkrHistory.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.All(dkrRows, r => Assert.Equal(ctx.Dkr, r.GetProperty("agencyId").GetGuid()));
        Assert.Equal(second.GetProperty("id").GetGuid(), dkrRows[^1].GetProperty("id").GetGuid());

        using var allHistory = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/snapshots");
        var ids = (await allHistory.ReadJsonAsync()).EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(second.GetProperty("id").GetGuid(), ids);
        Assert.Contains(nkc.GetProperty("id").GetGuid(), ids);
    }

    [Fact]
    public async Task Bad_inputs_and_other_roles_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var noAgency = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, "/api/v1/balance/capital", new { amount = 1m });
        using var unknownAgency = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, "/api/v1/balance/capital", new { agencyId = Guid.NewGuid(), amount = 1m });
        using var zero = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, "/api/v1/balance/capital", new { agencyId = ctx.Nkc, amount = 0m });
        using var fcfaDecimals = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, "/api/v1/balance/capital", new { agencyId = ctx.Dkr, amount = 1000.5m });
        using var saveWithoutAgency = await ctx.Client.PostJsonAsync(ctx.SupervisorToken, "/api/v1/balance/snapshots", new { });
        using var badPeriod = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, "/api/v1/balance/snapshots?from=2026-10-10&to=2026-10-01");
        using var agentReads = await ctx.Client.SendGetAsync(ctx.AgentToken, "/api/v1/balance");
        using var agentSetsCapital = await ctx.Client.PutJsonAsync(
            ctx.AgentToken, "/api/v1/balance/capital", new { agencyId = ctx.Nkc, amount = 1m });

        Assert.Equal("Balance.Agency.Required", await noAgency.ReadErrorCodeAsync());
        Assert.Equal("Balance.Agency.NotFound", await unknownAgency.ReadErrorCodeAsync());
        Assert.Equal("Balance.Capital.AmountInvalid", await zero.ReadErrorCodeAsync());
        Assert.Equal("Balance.Capital.AmountInvalid", await fcfaDecimals.ReadErrorCodeAsync());
        Assert.Equal("Balance.Agency.Required", await saveWithoutAgency.ReadErrorCodeAsync());
        Assert.Equal("Balance.Filter.PeriodInvalid", await badPeriod.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, agentReads.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, agentSetsCapital.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static JsonElement Caisse(JsonElement overviewOrList, Guid agencyId)
    {
        var rows = overviewOrList.ValueKind == JsonValueKind.Array ? overviewOrList : overviewOrList.GetProperty("caisses");
        return rows.EnumerateArray().Single(c => c.GetProperty("agencyId").GetGuid() == agencyId);
    }

    private static void AssertNetResult(JsonElement caisse, decimal initialCapital)
    {
        var capitalUsed = caisse.GetProperty("capitalUsed").GetDecimal();
        var netResult = caisse.GetProperty("netResult").GetDecimal();
        Assert.Equal(initialCapital, caisse.GetProperty("initialCapital").GetDecimal());
        Assert.Equal(initialCapital + caisse.GetProperty("ownerContributions").GetDecimal(), capitalUsed);
        Assert.Equal(caisse.GetProperty("budget").GetDecimal() - capitalUsed, netResult);
        Assert.Equal(netResult / capitalUsed * 100m, caisse.GetProperty("netResultPercent").GetDecimal());
    }

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

        return new Context(client, token, agentToken, nkc, dkr, cashId);
    }

    private sealed record Context(HttpClient Client, string SupervisorToken, string AgentToken, Guid Nkc, Guid Dkr, Guid NkcCash)
    {
        public async Task<decimal> BudgetAsync(Guid agencyId)
        {
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/cash-budget?agencyId={agencyId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("budget").GetDecimal();
        }

        public async Task<JsonElement> BalanceAsync()
        {
            using var response = await Client.SendGetAsync(SupervisorToken, "/api/v1/balance");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<JsonElement> SaveAsync(Guid agencyId)
        {
            using var response = await Client.PostJsonAsync(SupervisorToken, "/api/v1/balance/snapshots", new { agencyId });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task SetCapitalAsync(Guid agencyId, decimal amount, string? reason)
        {
            using var response = await Client.PutJsonAsync(SupervisorToken, "/api/v1/balance/capital", new { agencyId, amount, reason });
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
