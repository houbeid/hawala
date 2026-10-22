using System.Net;
using System.Text.Json;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Parties.Application.Features.Scopes;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// « Limiter les tiers » (phase 6, règles validées le 21 octobre 2026) : un compte limité ne voit que ses tiers
/// (liste, fiche, relevé) et ne les utilise qu'eux ; un tiers qu'il crée entre dans sa liste ; seul un Superviseur
/// fixe la liste, effet immédiat, tracé dans l'audit. Le budget de la caisse compte toujours tous les tiers de l'agence.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountPartyScopeTests(ApiFixture fixture)
{
    [Fact]
    public async Task Limited_account_sees_only_its_parties_right_away()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        Assert.Equal(HttpStatusCode.OK, limit.StatusCode);

        // Sans nouvelle connexion : la liste est relue à chaque requête.
        var list = await ctx.ListAsync(ctx.AgentToken);
        var ids = list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Equal([ctx.Allowed], ids);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        using var allowed = await ctx.Client.GetPartyAsync(ctx.AgentToken, ctx.Allowed);
        using var hidden = await ctx.Client.GetPartyAsync(ctx.AgentToken, ctx.Hidden);
        using var hiddenStatement = await ctx.Client.GetPartyStatementAsync(ctx.AgentToken, ctx.Hidden, $"agencyId={ctx.Agent.AgencyId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenStatement.StatusCode);
    }

    [Fact]
    public async Task Without_a_rule_an_agent_sees_every_party()
    {
        var ctx = await ArrangeAsync();

        using var allowed = await ctx.Client.GetPartyAsync(ctx.AgentToken, ctx.Allowed);
        using var other = await ctx.Client.GetPartyAsync(ctx.AgentToken, ctx.Hidden);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Lifting_the_limit_shows_everything_again()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        using var lift = await ctx.SetScopeAsync(ctx.Agent.Id, limited: false, ctx.Allowed);

        Assert.Equal(HttpStatusCode.OK, lift.StatusCode);
        var view = await lift.ReadJsonAsync();
        Assert.False(view.GetProperty("limited").GetBoolean());
        Assert.Empty(view.GetProperty("parties").EnumerateArray());
        using var other = await ctx.Client.GetPartyAsync(ctx.AgentToken, ctx.Hidden);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task A_party_created_by_a_limited_account_joins_its_list()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        var created = await ctx.CreatePartyAsync(ctx.AgentToken, "Nouveau");

        using var get = await ctx.Client.GetPartyAsync(ctx.AgentToken, created);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var scope = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/users/{ctx.Agent.Id}/party-scope");
        var parties = (await scope.ReadJsonAsync()).GetProperty("parties").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(created, parties);
        Assert.Contains(ctx.Allowed, parties);
    }

    [Fact]
    public async Task A_hidden_party_cannot_be_used_for_a_cash_movement()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);
        var cash = await ctx.CreateCashAsync();

        using var hidden = await ctx.ReceiptAsync(cash, ctx.Hidden);
        using var allowed = await ctx.ReceiptAsync(cash, ctx.Allowed);

        Assert.Equal("Cash.Movement.PartyNotFound", await hidden.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
    }

    [Fact]
    public async Task The_cash_budget_still_counts_every_party_of_the_agency()
    {
        var ctx = await ArrangeAsync();
        using var opening = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, $"/api/v1/parties/{ctx.Hidden}/agencies/{ctx.Agent.AgencyId}/opening-balance", new { amount = 1_000m });
        Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        using var budget = await ctx.Client.SendGetAsync(ctx.AgentToken, $"/api/v1/cash-budget?agencyId={ctx.Agent.AgencyId}");

        Assert.Equal(HttpStatusCode.OK, budget.StatusCode);
        var debtors = (await budget.ReadJsonAsync()).GetProperty("debtors").GetProperty("items").EnumerateArray();
        Assert.Contains(debtors, d => d.GetProperty("id").GetGuid() == ctx.Hidden);
    }

    [Fact]
    public async Task Change_is_written_to_the_audit_log()
    {
        var ctx = await ArrangeAsync();

        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);
        using var again = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var line = Assert.Single(await ReadAuditAsync(ctx.Factory, ctx.Agent.Id));
        Assert.Equal(ctx.Supervisor.Id, line.ActorId);
        Assert.Equal(ctx.Agent.AgencyId, line.AgencyId);
        using var changes = JsonDocument.Parse(line.ChangesJson);
        var byField = changes.RootElement.EnumerateArray().ToDictionary(
            c => c.GetProperty("field").GetString()!,
            c => (c.GetProperty("before").GetString(), c.GetProperty("after").GetString()));
        Assert.Equal(("false", "true"), byField["limited"]);
        Assert.Equal(("not_allowed", "allowed"), byField[$"party:{ctx.Allowed}"]);
    }

    [Fact]
    public async Task Only_a_supervisor_sets_the_list()
    {
        var ctx = await ArrangeAsync();
        var manager = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.UsersManage];
        using var grant = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, $"/api/v1/users/{manager.Id}/permissions", new { permissions });
        var (managerToken, _) = await ctx.Client.LoginOkAsync(manager.Email, manager.Password);

        using var set = await ctx.Client.PutJsonAsync(
            managerToken, $"/api/v1/users/{ctx.Agent.Id}/party-scope", new { limited = true, partyIds = new[] { ctx.Allowed } });
        using var read = await ctx.Client.SendGetAsync(managerToken, $"/api/v1/users/{ctx.Agent.Id}/party-scope");
        using var withoutUsersManage = await ctx.Client.SendGetAsync(ctx.AgentToken, $"/api/v1/users/{ctx.Agent.Id}/party-scope");

        Assert.Equal(HttpStatusCode.Forbidden, set.StatusCode);
        Assert.Equal("Parties.Scope.SupervisorOnly", await set.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withoutUsersManage.StatusCode);
    }

    [Fact]
    public async Task Invalid_requests_are_refused()
    {
        var ctx = await ArrangeAsync();
        var otherSupervisor = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Supervisor);

        using var supervisor = await ctx.SetScopeAsync(otherSupervisor.Id, limited: true, ctx.Allowed);
        using var unknownParty = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, Guid.NewGuid());
        using var unknownAccount = await ctx.SetScopeAsync(Guid.NewGuid(), limited: true, ctx.Allowed);
        using var missingLimited = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, $"/api/v1/users/{ctx.Agent.Id}/party-scope", new { partyIds = new[] { ctx.Allowed } });

        Assert.Equal(HttpStatusCode.Conflict, supervisor.StatusCode);
        Assert.Equal("Parties.Scope.SupervisorSeesAll", await supervisor.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, unknownParty.StatusCode);
        Assert.Equal("Parties.Scope.PartyNotFound", await unknownParty.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknownAccount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingLimited.StatusCode);
        Assert.Equal("Parties.Scope.LimitedRequired", await missingLimited.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task A_promoted_account_sees_everything_despite_its_old_list()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.SetScopeAsync(ctx.Agent.Id, limited: true, ctx.Allowed);

        using var promote = await ctx.Client.ChangeUserRoleAsync(ctx.SupervisorToken, ctx.Agent.Id, new { role = "Supervisor" });
        Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        var (token, _) = await ctx.Client.LoginOkAsync(ctx.Agent.Email, ctx.Agent.Password);

        using var other = await ctx.Client.GetPartyAsync(token, ctx.Hidden);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        var ctx = new Context(factory, client, supervisor, supervisorToken, agent, agentToken, $"s{Guid.NewGuid():N}"[..10]);
        return ctx with
        {
            Allowed = await ctx.CreatePartyAsync(supervisorToken, "Autorisé"),
            Hidden = await ctx.CreatePartyAsync(supervisorToken, "Caché"),
        };
    }

    private static async Task<List<AuditLine>> ReadAuditAsync(HawalaApiFactory factory, Guid accountId)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "ActorId", "AgencyId", "Changes"::text FROM audit.audit_log
            WHERE "Action" = @action AND "EntityId" = @id ORDER BY "OccurredAt"
            """,
            connection);
        command.Parameters.AddWithValue("action", AccountPartyScopeHandlers.AuditAction);
        command.Parameters.AddWithValue("id", accountId);

        var lines = new List<AuditLine>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(new AuditLine(
                reader.IsDBNull(0) ? null : reader.GetGuid(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2)));
        }

        return lines;
    }

    private sealed record AuditLine(Guid? ActorId, Guid? AgencyId, string ChangesJson);

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        TestAccount Supervisor,
        string SupervisorToken,
        TestAccount Agent,
        string AgentToken,
        string Tag)
    {
        public Guid Allowed { get; init; }

        public Guid Hidden { get; init; }

        public Task<HttpResponseMessage> SetScopeAsync(Guid accountId, bool limited, params Guid[] partyIds) =>
            Client.PutJsonAsync(SupervisorToken, $"/api/v1/users/{accountId}/party-scope", new { limited, partyIds });

        public async Task<Guid> CreatePartyAsync(string token, string name)
        {
            using var response = await Client.CreatePartyAsync(token, new { name = $"{name} {Tag}", type = "Client" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> ListAsync(string token)
        {
            using var response = await Client.ListPartiesAsync(token, $"search={Tag}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<Guid> CreateCashAsync()
        {
            using var response = await Client.CreateTreasuryAccountAsync(
                SupervisorToken, new { name = $"Caisse {Tag}", type = "cash", agencyId = Agent.AgencyId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public Task<HttpResponseMessage> ReceiptAsync(Guid cash, Guid party) =>
            Client.CreateCashMovementAsync(
                AgentToken,
                new { type = "Receipt", treasuryAccountId = cash, partyId = party, amount = 100m, note = $"Test {Tag}" },
                Guid.NewGuid().ToString("N"));
    }
}
