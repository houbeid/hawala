using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Treasury;

/// <summary>
/// Relevé d'un compte de trésorerie (décision du 22 octobre 2026) : lu au grand livre, solde au début, chaque écriture
/// avec le solde après, solde à la fin ; cash.budget.read exigée ; périmètre par agence. Chaque test crée son compte.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TreasuryStatementTests(ApiFixture fixture)
{
    [Fact]
    public async Task Statement_lists_every_movement_with_the_balance_after_each_line()
    {
        var ctx = await ArrangeAsync();
        await ctx.MovementAsync("OwnerContribution", 100_000m, ctx.Today.AddDays(-5));
        await ctx.MovementAsync("OwnerWithdrawal", 30_000m, ctx.Today.AddDays(-2));

        var body = await ctx.StatementOkAsync(ctx.SupervisorToken, $"from={Day(ctx.Today.AddDays(-30))}&to={Day(ctx.Today)}");

        Assert.Equal(ctx.AccountName, body.GetProperty("accountName").GetString());
        Assert.Equal("Cash", body.GetProperty("type").GetString());
        Assert.Equal("MRO", body.GetProperty("currency").GetString());
        Assert.Equal(0m, body.GetProperty("openingBalance").GetDecimal());
        var lines = body.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("Cash.OwnerContribution", lines[0].GetProperty("entryType").GetString());
        Assert.Equal(100_000m, lines[0].GetProperty("debit").GetDecimal());
        Assert.Equal(100_000m, lines[0].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal("Cash.OwnerWithdrawal", lines[1].GetProperty("entryType").GetString());
        Assert.Equal(30_000m, lines[1].GetProperty("credit").GetDecimal());
        Assert.Equal(70_000m, lines[1].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(70_000m, body.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Opening_balance_carries_what_happened_before_the_period()
    {
        var ctx = await ArrangeAsync();
        await ctx.MovementAsync("OwnerContribution", 100_000m, ctx.Today.AddDays(-5));
        await ctx.MovementAsync("OwnerWithdrawal", 30_000m, ctx.Today.AddDays(-2));

        var body = await ctx.StatementOkAsync(ctx.SupervisorToken, $"from={Day(ctx.Today.AddDays(-3))}&to={Day(ctx.Today)}");

        Assert.Equal(100_000m, body.GetProperty("openingBalance").GetDecimal());
        Assert.Single(body.GetProperty("lines").EnumerateArray());
        Assert.Equal(70_000m, body.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Without_dates_the_period_is_the_current_month_and_an_inverted_period_is_refused()
    {
        var ctx = await ArrangeAsync();

        var body = await ctx.StatementOkAsync(ctx.AgentToken, query: null);
        using var inverted = await ctx.StatementAsync(ctx.AgentToken, $"from={Day(ctx.Today)}&to={Day(ctx.Today.AddDays(-1))}");

        Assert.Equal(Day(new DateOnly(ctx.Today.Year, ctx.Today.Month, 1)), body.GetProperty("from").GetString());
        Assert.Equal(Day(ctx.Today), body.GetProperty("to").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, inverted.StatusCode);
        Assert.Equal("Ledger.Statement.PeriodInvalid", await inverted.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_agent_of_the_other_agency_gets_not_found()
    {
        var ctx = await ArrangeAsync();
        var dakarAgent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "DKR");
        var (dakarToken, _) = await ctx.Client.LoginOkAsync(dakarAgent.Email, dakarAgent.Password);

        using var response = await ctx.StatementAsync(dakarToken, query: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Treasury.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Without_cash_budget_read_the_statement_is_refused()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent).Where(p => p != Permissions.CashBudgetRead)];
        using var set = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/permissions", new { permissions });
        Assert.True(set.IsSuccessStatusCode, await set.Content.ReadAsStringAsync());
        var (agentToken, _) = await ctx.Client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await ctx.StatementAsync(agentToken, query: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Treasury.Statement.NotVisible", await response.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        Guid nouakchott;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        }

        var name = $"Relevé {Guid.NewGuid():N}"[..20];
        using var created = await client.CreateTreasuryAccountAsync(supervisorToken, new { name, type = "Cash", agencyId = nouakchott });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        return new Context(factory, client, supervisorToken, agentToken, nouakchott, accountId, name, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string AgentToken,
        Guid Nouakchott,
        Guid AccountId,
        string AccountName,
        DateOnly Today)
    {
        public async Task MovementAsync(string type, decimal amount, DateOnly day)
        {
            using var response = await Client.CreateCashMovementAsync(
                SupervisorToken,
                new
                {
                    agencyId = Nouakchott,
                    type,
                    treasuryAccountId = AccountId,
                    amount,
                    operationAt = new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        public Task<HttpResponseMessage> StatementAsync(string token, string? query) =>
            Client.SendGetAsync(token, $"/api/v1/treasury-accounts/{AccountId}/statement{(query is null ? string.Empty : "?" + query)}");

        public async Task<JsonElement> StatementOkAsync(string token, string? query)
        {
            using var response = await StatementAsync(token, query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
    }
}
