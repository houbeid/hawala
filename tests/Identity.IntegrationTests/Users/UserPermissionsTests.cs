using System.Net;
using System.Text.Json;
using Identity.Application.Features.Users.PermissionOverrides;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Identity.IntegrationTests.Users;

/// <summary>
/// Ajouts / retraits de permissions par compte (phase 6, règles validées le 21 octobre 2026) :
/// n'importe quelle permission pour un non-Superviseur, donnée seulement par un compte de rôle Superviseur,
/// effet immédiat, tracé dans l'audit ; un Superviseur garde tout ; un changement de rôle efface les ajustements.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UserPermissionsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Given_permission_works_right_after_refresh()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var before = await client.ListUsersAsync(agentToken);
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        using var response = await SetAsync(client, supervisorToken, agent.Id, AgentTemplateWith(Permissions.UsersManage));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Effet immédiat : l'ancien jeton est refusé, le nouveau porte la permission.
        using var oldMe = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldMe.StatusCode);

        var newToken = await RefreshAsync(client, agentCookie);
        using var after = await client.ListUsersAsync(newToken);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var me = await ReadMeAsync(client, newToken);
        Assert.Contains(Permissions.UsersManage, PermissionsOf(me));
    }

    [Fact]
    public async Task Taken_permission_is_refused_right_after_refresh()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var withoutPartiesCreate = RolePermissions.For(RoleTemplate.Agent).Where(p => p != Permissions.PartiesCreate).ToArray();

        using var response = await SetAsync(client, supervisorToken, agent.Id, withoutPartiesCreate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newToken = await RefreshAsync(client, agentCookie);
        using var create = await client.CreatePartyAsync(newToken, new { name = "Tiers Test", type = "Client" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.DoesNotContain(Permissions.PartiesCreate, PermissionsOf(await ReadMeAsync(client, newToken)));
    }

    [Fact]
    public async Task Reading_shows_where_each_permission_comes_from()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var desired = RolePermissions.For(RoleTemplate.Agent)
            .Where(p => p != Permissions.TransfersCredit)
            .Append(Permissions.ProfitView)
            .ToArray();
        using var grant = await SetAsync(client, supervisorToken, agent.Id, desired);

        using var response = await client.SendGetAsync(supervisorToken, $"/api/v1/users/{agent.Id}/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Agent", body.GetProperty("role").GetString());
        var states = body.GetProperty("permissions").EnumerateArray()
            .ToDictionary(p => p.GetProperty("permission").GetString()!, p => (p.GetProperty("granted").GetBoolean(), p.GetProperty("source").GetString()));

        Assert.Equal(Permissions.All.Count, states.Count);
        Assert.Equal((true, "Added"), states[Permissions.ProfitView]);
        Assert.Equal((false, "Removed"), states[Permissions.TransfersCredit]);
        Assert.Equal((true, "Role"), states[Permissions.TransfersCreate]);
        Assert.Equal((false, "None"), states[Permissions.TransfersCancel]);
    }

    [Fact]
    public async Task Change_is_written_to_the_audit_log()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var desired = RolePermissions.For(RoleTemplate.Agent)
            .Where(p => p != Permissions.TransfersCredit)
            .Append(Permissions.TransfersCancel)
            .ToArray();

        using var response = await SetAsync(client, supervisorToken, agent.Id, desired);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = await ReadAuditAsync(factory, agent.Id);
        var line = Assert.Single(lines);
        Assert.Equal(SetUserPermissionsCommandHandler.AuditAction, line.Action);
        Assert.Equal(supervisor.Id, line.ActorId);
        Assert.Equal(agent.AgencyId, line.AgencyId);

        using var changes = JsonDocument.Parse(line.ChangesJson);
        var byField = changes.RootElement.EnumerateArray().ToDictionary(
            c => c.GetProperty("field").GetString()!,
            c => (c.GetProperty("before").GetString(), c.GetProperty("after").GetString()));
        Assert.Equal(2, byField.Count);
        Assert.Equal(("not_granted", "granted"), byField[Permissions.TransfersCancel]);
        Assert.Equal(("granted", "not_granted"), byField[Permissions.TransfersCredit]);
    }

    [Fact]
    public async Task Same_permissions_again_change_nothing()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await SetAsync(client, supervisorToken, agent.Id, AgentTemplateWith());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Rien n'a changé : la session continue et aucune ligne d'audit n'est écrite.
        using var me = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Empty(await ReadAuditAsync(factory, agent.Id));
    }

    [Fact]
    public async Task Only_a_supervisor_role_can_change_permissions()
    {
        var factory = await fixture.GetFactoryAsync();
        var manager = await TestAccounts.CreateAsync(factory);
        var target = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, managerCookie) = await client.LoginOkAsync(manager.Email, manager.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        using var grant = await SetAsync(client, supervisorToken, manager.Id, AgentTemplateWith(Permissions.UsersManage));
        var managerToken = await RefreshAsync(client, managerCookie);

        // users.manage ne suffit pas : ni pour un autre compte, ni pour soi-même.
        using var other = await SetAsync(client, managerToken, target.Id, AgentTemplateWith(Permissions.ProfitView));
        using var self = await SetAsync(client, managerToken, manager.Id, AgentTemplateWith(Permissions.UsersManage, Permissions.ProfitView));

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal("Identity.Permissions.SupervisorOnly", await other.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Empty(await ReadAuditAsync(factory, target.Id));
    }

    [Fact]
    public async Task Without_users_manage_the_routes_are_closed()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var read = await client.SendGetAsync(agentToken, $"/api/v1/users/{agent.Id}/permissions");
        using var write = await SetAsync(client, agentToken, agent.Id, AgentTemplateWith(Permissions.UsersManage));

        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task A_supervisor_keeps_every_permission()
    {
        var factory = await fixture.GetFactoryAsync();
        var other = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await SetAsync(client, supervisorToken, other.Id, [Permissions.TransfersCreate]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Identity.Permissions.SupervisorHasAll", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_requests_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var unknown = await SetAsync(client, supervisorToken, agent.Id, AgentTemplateWith("transfers.everything"));
        using var missing = await client.PutJsonAsync(supervisorToken, $"/api/v1/users/{agent.Id}/permissions", new { });
        using var notFound = await SetAsync(client, supervisorToken, Guid.NewGuid(), AgentTemplateWith());

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal("Identity.Permissions.Unknown", await unknown.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Identity.Permissions.Required", await missing.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task Changing_the_role_clears_the_adjustments()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        using var grant = await SetAsync(client, supervisorToken, agent.Id, AgentTemplateWith(Permissions.ProfitView));

        using var role = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Cashier", agencyId = agent.AgencyId });

        Assert.Equal(HttpStatusCode.NoContent, role.StatusCode);
        using var response = await client.SendGetAsync(supervisorToken, $"/api/v1/users/{agent.Id}/permissions");
        var granted = (await response.ReadJsonAsync()).GetProperty("permissions").EnumerateArray()
            .Where(p => p.GetProperty("granted").GetBoolean())
            .Select(p => p.GetProperty("permission").GetString()!)
            .Order()
            .ToList();
        Assert.Equal(RolePermissions.For(RoleTemplate.Cashier).Order(), granted);
    }

    [Fact]
    public async Task Me_lists_the_effective_permissions()
    {
        var factory = await fixture.GetFactoryAsync();
        var cashier = await TestAccounts.CreateAsync(factory, RoleTemplate.Cashier);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(cashier.Email, cashier.Password);

        var me = await ReadMeAsync(client, token);

        Assert.Equal(RolePermissions.For(RoleTemplate.Cashier).Order(StringComparer.Ordinal), PermissionsOf(me));
    }

    // --- Outils ------------------------------------------------------------

    private static string[] AgentTemplateWith(params string[] extra) =>
        [.. RolePermissions.For(RoleTemplate.Agent).Concat(extra)];

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string token, Guid id, string[] permissions) =>
        client.PutJsonAsync(token, $"/api/v1/users/{id}/permissions", new { permissions });

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }

    private static async Task<string> RefreshAsync(HttpClient client, string cookie)
    {
        using var response = await client.RefreshAsync(cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadSessionAsync()).AccessToken;
    }

    private static async Task<JsonElement> ReadMeAsync(HttpClient client, string token)
    {
        using var response = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static List<string> PermissionsOf(JsonElement me) =>
        [.. me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];

    private static async Task<List<AuditLine>> ReadAuditAsync(HawalaApiFactory factory, Guid accountId)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Action", "ActorId", "AgencyId", "Changes"::text FROM audit.audit_log
            WHERE "EntityType" = 'UserAccount' AND "EntityId" = @id ORDER BY "OccurredAt"
            """,
            connection);
        command.Parameters.AddWithValue("id", accountId);

        var lines = new List<AuditLine>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lines.Add(new AuditLine(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.GetString(3)));
        }

        return lines;
    }

    private sealed record AuditLine(string Action, Guid? ActorId, Guid? AgencyId, string ChangesJson);
}
