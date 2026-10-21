using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Balance;

/// <summary>
/// Bilan général (décisions du 21 octobre 2026) : budget de l'agence en MRO + budget de l'agence en FCFA converti au taux du bilan ;
/// capital pris en compte = capital initial + apports nets du patron ; résultat net ; photo du jour ; Superviseur seulement.
/// La base est partagée : les tests vérifient des égalités (bilan = budgets de la Caisse) et des écarts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class BalanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task General_balance_is_the_mro_budget_plus_the_fcfa_budget_converted()
    {
        var ctx = await ArrangeAsync();
        var nkcBudget = await ctx.BudgetAsync(ctx.Nkc);
        var dkrBudget = await ctx.BudgetAsync(ctx.Dkr);

        var balance = await ctx.BalanceAsync(3600m);

        Assert.Equal(3600m, balance.GetProperty("rate").GetDecimal());
        Assert.Equal(nkcBudget, balance.GetProperty("mroBudget").GetDecimal());
        Assert.Equal(dkrBudget, balance.GetProperty("xofBudget").GetDecimal());
        Assert.Equal(dkrBudget * 3600m / 5000m, balance.GetProperty("xofBudgetInMro").GetDecimal());
        Assert.Equal(nkcBudget + (dkrBudget * 3600m / 5000m), balance.GetProperty("generalBalance").GetDecimal());
    }

    [Fact]
    public async Task Initial_capital_is_kept_with_its_history_and_gives_the_net_result()
    {
        var ctx = await ArrangeAsync();

        await ctx.SetCapitalAsync(100_000_000m, "Mise en service");
        await ctx.SetCapitalAsync(165_500_000m, "Correction");

        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/capital");
        var capital = await response.ReadJsonAsync();
        Assert.Equal(165_500_000m, capital.GetProperty("current").GetDecimal());
        var history = capital.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal("Correction", history[0].GetProperty("reason").GetString());
        Assert.Equal(100_000_000m, history[1].GetProperty("amount").GetDecimal());

        var balance = await ctx.BalanceAsync(3600m);
        var capitalUsed = balance.GetProperty("capitalUsed").GetDecimal();
        Assert.Equal(165_500_000m + balance.GetProperty("ownerContributionsMro").GetDecimal(), capitalUsed);
        Assert.Equal(balance.GetProperty("generalBalance").GetDecimal() - capitalUsed, balance.GetProperty("netResult").GetDecimal());
        Assert.Equal(balance.GetProperty("netResult").GetDecimal() / capitalUsed * 100m, balance.GetProperty("netResultPercent").GetDecimal());
    }

    [Fact]
    public async Task An_owner_contribution_raises_the_capital_not_the_result()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetCapitalAsync(165_500_000m, null);
        var before = await ctx.BalanceAsync(3600m);

        await ctx.OwnerContributionAsync(1_000_000m);
        var after = await ctx.BalanceAsync(3600m);

        Assert.Equal(1_000_000m, after.GetProperty("generalBalance").GetDecimal() - before.GetProperty("generalBalance").GetDecimal());
        Assert.Equal(1_000_000m, after.GetProperty("capitalUsed").GetDecimal() - before.GetProperty("capitalUsed").GetDecimal());
        Assert.Equal(before.GetProperty("netResult").GetDecimal(), after.GetProperty("netResult").GetDecimal());
    }

    [Fact]
    public async Task Saving_the_balance_keeps_the_day_and_replaces_it_the_same_day()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetCapitalAsync(165_500_000m, null);
        var live = await ctx.BalanceAsync(3500m);

        var first = await ctx.SaveAsync(3500m);
        var second = await ctx.SaveAsync(3600m);

        Assert.Equal(live.GetProperty("generalBalance").GetDecimal(), first.GetProperty("generalBalance").GetDecimal());
        Assert.Equal(3500m, first.GetProperty("rate").GetDecimal());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(3600m, second.GetProperty("rate").GetDecimal());
        Assert.Equal(165_500_000m, second.GetProperty("initialCapital").GetDecimal());

        using var history = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance/snapshots");
        var last = (await history.ReadJsonAsync()).EnumerateArray().Last();
        Assert.Equal(second.GetProperty("id").GetGuid(), last.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Bad_inputs_and_other_roles_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var badRate = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/balance?rate=3600.123");
        using var badCapital = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, "/api/v1/balance/capital", new { amount = 0m });
        using var agentReads = await ctx.Client.SendGetAsync(ctx.AgentToken, "/api/v1/balance");
        using var agentSetsCapital = await ctx.Client.PutJsonAsync(ctx.AgentToken, "/api/v1/balance/capital", new { amount = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.Equal("Balance.Capital.AmountInvalid", await badCapital.ReadErrorCodeAsync());
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

        public async Task<JsonElement> BalanceAsync(decimal rate)
        {
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/balance?rate={rate}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<JsonElement> SaveAsync(decimal rate)
        {
            using var response = await Client.PostJsonAsync(SupervisorToken, "/api/v1/balance/snapshots", new { rate });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task SetCapitalAsync(decimal amount, string? reason)
        {
            using var response = await Client.PutJsonAsync(SupervisorToken, "/api/v1/balance/capital", new { amount, reason });
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
