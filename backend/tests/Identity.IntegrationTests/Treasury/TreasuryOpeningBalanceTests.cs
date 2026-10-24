using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Treasury;

/// <summary>
/// Solde de départ d'un compte de trésorerie (décision du 22 octobre 2026, phase 8) : écriture d'ouverture de la
/// différence contre « Soldes d'ouverture », à la date choisie ; solde de départ lu au grand livre et renvoyé par la fiche ;
/// même valeur = rien d'écrit ; audit avant / après ; alerte du contrôle de cohérence ; Superviseur seulement (treasury.manage).
/// Chaque test crée son compte à Nouakchott (MRO) ou à Dakar (FCFA).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TreasuryOpeningBalanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_opening_balance_is_posted_against_opening_balances_and_shown_on_the_account()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.SetTreasuryOpeningBalanceAsync(
            ctx.SupervisorToken, ctx.Account, new { amount = 2_400_000.5m, operationDate = Day(ctx.Today.AddDays(-1)) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await ctx.AccountAsync(ctx.SupervisorToken);
        Assert.Equal(2_400_000.5m, account.GetProperty("balance").GetDecimal());
        Assert.Equal(2_400_000.5m, account.GetProperty("openingBalance").GetDecimal());

        var lines = await ctx.StatementLinesAsync();
        var line = Assert.Single(lines);
        Assert.Equal("Treasury.OpeningBalance", line.GetProperty("entryType").GetString());
        Assert.Equal(2_400_000.5m, line.GetProperty("debit").GetDecimal());
        Assert.Equal(Day(ctx.Today.AddDays(-1)), line.GetProperty("operationDate").GetString());
    }

    [Fact]
    public async Task Changing_it_posts_only_the_difference_and_the_same_value_posts_nothing()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetOkAsync(1_000_000m);

        await ctx.SetOkAsync(750_000m);
        await ctx.SetOkAsync(750_000m);

        var account = await ctx.AccountAsync(ctx.SupervisorToken);
        Assert.Equal(750_000m, account.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(750_000m, account.GetProperty("balance").GetDecimal());
        var lines = await ctx.StatementLinesAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(250_000m, lines[1].GetProperty("credit").GetDecimal());
    }

    [Fact]
    public async Task Other_movements_do_not_count_as_opening_balance()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetOkAsync(500_000m);
        using var contribution = await ctx.Client.CreateCashMovementAsync(
            ctx.SupervisorToken,
            new { agencyId = ctx.Nkc, type = "OwnerContribution", treasuryAccountId = ctx.Account, amount = 100_000m },
            Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, contribution.StatusCode);

        await ctx.SetOkAsync(600_000m);

        var account = await ctx.AccountAsync(ctx.SupervisorToken);
        Assert.Equal(600_000m, account.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(700_000m, account.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Amounts_dates_and_states_are_checked()
    {
        var ctx = await ArrangeAsync();

        using var missing = await ctx.SetAsync(new { });
        using var tooPrecise = await ctx.SetAsync(new { amount = 10.005m });
        using var future = await ctx.SetAsync(new { amount = 10m, operationDate = Day(ctx.Today.AddDays(2)) });
        using var deactivate = await ctx.Client.DeactivateTreasuryAccountAsync(ctx.SupervisorToken, ctx.Account);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        using var inactive = await ctx.SetAsync(new { amount = 10m });

        Assert.Equal("Treasury.Account.OpeningBalanceRequired", await missing.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
        Assert.Equal("Ledger.Entry.FutureDate", await future.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        Assert.Equal("Treasury.Account.Inactive", await inactive.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task A_dakar_account_takes_whole_fcfa_only()
    {
        var ctx = await ArrangeAsync(agencyCode: "DKR");

        using var decimals = await ctx.SetAsync(new { amount = 1_000.5m });
        using var whole = await ctx.SetAsync(new { amount = 62_300_000m });

        Assert.Equal(HttpStatusCode.BadRequest, decimals.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, whole.StatusCode);
    }

    [Fact]
    public async Task Only_the_supervisor_sets_it_an_agent_reads_it_like_the_balance_and_the_list_leaves_it_out()
    {
        var ctx = await ArrangeAsync();
        await ctx.SetOkAsync(300_000m);

        using var byAgent = await ctx.Client.SetTreasuryOpeningBalanceAsync(ctx.AgentToken, ctx.Account, new { amount = 1m });
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);

        // L'agent a cash.budget.read : il voit le solde, donc aussi le solde de départ (même visibilité).
        var seenByAgent = await ctx.AccountAsync(ctx.AgentToken);
        Assert.Equal(300_000m, seenByAgent.GetProperty("openingBalance").GetDecimal());

        using var list = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/treasury-accounts?agencyId={ctx.Nkc}");
        var inList = (await list.ReadJsonAsync()).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == ctx.Account);
        Assert.Equal(JsonValueKind.Null, inList.GetProperty("openingBalance").ValueKind);
    }

    [Fact]
    public async Task It_is_audited_with_before_and_after_and_raises_a_coherence_alert()
    {
        var ctx = await ArrangeAsync();
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        await ctx.SetOkAsync(1_200_000m);
        await ctx.SetOkAsync(1_000_000m);

        using var audit = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/audit?entityType=TreasuryAccount&entityId={ctx.Account}");
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        var entries = (await audit.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        var latest = entries[0];
        Assert.Equal("Treasury.Account.OpeningBalanceChanged", latest.GetProperty("action").GetString());
        Assert.Equal(ctx.AccountName, latest.GetProperty("entityName").GetString());
        var change = Assert.Single(latest.GetProperty("changes").EnumerateArray());
        Assert.Equal("openingBalance", change.GetProperty("field").GetString());
        Assert.Equal("1200000", change.GetProperty("before").GetString());
        Assert.Equal("1000000", change.GetProperty("after").GetString());

        var sinceText = Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
        using var coherence = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, $"/api/v1/cash-budget/coherence?agencyId={ctx.Nkc}&since={sinceText}");
        var alerts = (await coherence.ReadJsonAsync()).GetProperty("alerts").EnumerateArray()
            .Where(a => a.GetProperty("label").GetString() == $"Solde initial : {ctx.AccountName}")
            .ToList();
        Assert.Equal(2, alerts.Count);
        Assert.All(alerts, a => Assert.Equal("OpeningBalanceChanged", a.GetProperty("kind").GetString()));
    }

    // --- Outils ------------------------------------------------------------

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<Context> ArrangeAsync(string agencyCode = "NKC")
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: agencyCode);
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, agencyCode);

        var name = $"Caisse {Guid.NewGuid():N}"[..30];
        using var created = await client.CreateTreasuryAccountAsync(supervisorToken, new { name, type = "Cash", agencyId = nkc });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var accountId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        return new Context(client, supervisorToken, agentToken, nkc, accountId, name, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string AgentToken,
        Guid Nkc,
        Guid Account,
        string AccountName,
        DateOnly Today)
    {
        public Task<HttpResponseMessage> SetAsync(object body) => Client.SetTreasuryOpeningBalanceAsync(SupervisorToken, Account, body);

        public async Task SetOkAsync(decimal amount)
        {
            using var response = await SetAsync(new { amount });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task<JsonElement> AccountAsync(string token)
        {
            using var response = await Client.GetTreasuryAccountAsync(token, Account);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<List<JsonElement>> StatementLinesAsync()
        {
            var from = Today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/treasury-accounts/{Account}/statement?from={from}&to={to}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("lines").EnumerateArray()];
        }
    }
}
