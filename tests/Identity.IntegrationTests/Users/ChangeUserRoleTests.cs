using System.Net;
using System.Text.Json;
using Hawala.SharedKernel.Messaging;
using Identity.Application.Features.Users.ChangeUserRole;
using Identity.Domain;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed class ChangeUserRoleTests(ApiFixture fixture)
{
    // --- Changement réussi ------------------------------------------------

    [Fact]
    public async Task Promoted_agent_gets_supervisor_rights_after_refresh()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserRoleAsync(supervisorToken, agent.Id, new { role = "Supervisor" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // L'ancien jeton est refusé tout de suite ; le refresh donne un jeton avec le nouveau rôle.
        using var oldMe = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldMe.StatusCode);

        var newToken = await RefreshOkAsync(client, agentCookie);
        var me = await MeAsync(client, newToken);
        Assert.Equal("Supervisor", me.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("agency").ValueKind);

        using var list = await client.ListUsersAsync(newToken);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Demoted_supervisor_loses_supervisor_rights_after_refresh()
    {
        var factory = await fixture.GetFactoryAsync();
        var other = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (_, otherCookie) = await client.LoginOkAsync(other.Email, other.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");

        using var response = await client.ChangeUserRoleAsync(
            supervisorToken, other.Id, new { role = "Agent", agencyId = dakar });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var newToken = await RefreshOkAsync(client, otherCookie);
        var me = await MeAsync(client, newToken);
        Assert.Equal("Agent", me.GetProperty("role").GetString());
        Assert.Equal("DKR", me.GetProperty("agency").GetProperty("code").GetString());

        using var list = await client.ListUsersAsync(newToken);
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Agent_can_become_cashier_in_another_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");

        using var response = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "cashier", agencyId = dakar });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal("Cashier", user.GetProperty("role").GetString());
        Assert.Equal("DKR", user.GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Same_role_with_another_agency_moves_the_account()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");

        using var response = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Agent", agencyId = dakar });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal("Agent", user.GetProperty("role").GetString());
        Assert.Equal("DKR", user.GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Same_role_and_agency_changes_nothing_and_keeps_the_session()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Agent", agencyId = agent.AgencyId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var me = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Supervisor_cannot_change_own_role()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.ChangeUserRoleAsync(
            token, supervisor.Id, new { role = "Agent", agencyId = await GetAgencyIdAsync(factory, "NKC") });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Identity.Account.CannotChangeOwnRole", await response.ReadErrorCodeAsync());

        using var me = await client.MeAsync(token);
        Assert.Equal("Supervisor", (await me.ReadJsonAsync()).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Agency_and_role_rules_are_enforced()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        using var unknownRole = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Patron", agencyId = nouakchott });
        using var agentWithoutAgency = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Cashier" });
        using var supervisorWithAgency = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Supervisor", agencyId = nouakchott });
        using var unknownAgency = await client.ChangeUserRoleAsync(
            supervisorToken, agent.Id, new { role = "Cashier", agencyId = Guid.NewGuid() });

        Assert.Equal("Identity.Account.RoleInvalid", await unknownRole.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.AgencyRequired", await agentWithoutAgency.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.AgencyNotAllowedForSupervisor", await supervisorWithAgency.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());

        // Aucun de ces refus n'a modifié le compte.
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal("Agent", user.GetProperty("role").GetString());
        Assert.Equal("NKC", user.GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ChangeUserRoleAsync(
            supervisorToken, Guid.NewGuid(), new { role = "Supervisor" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_change_roles()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.ChangeUserRoleAsync(agentToken, agent.Id, new { role = "Supervisor" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Dernier Superviseur actif -----------------------------------------

    /// <summary>
    /// Par l'API, on ne peut pas viser son propre compte : avec un seul Superviseur actif, personne d'autre
    /// ne peut le rétrograder. Le vrai handler (avec sa transaction et son verrou) est donc appelé
    /// directement, avec un autre auteur.
    /// </summary>
    [Fact]
    public async Task Last_active_supervisor_cannot_be_demoted()
    {
        var factory = await fixture.GetFactoryAsync();
        var last = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);

        // Ne garder que lui actif ; les autres sont remis en état à la fin (LoginTests utilise
        // le Superviseur créé au démarrage).
        var others = (await TestAccounts.GetActiveSupervisorIdsAsync(factory))
            .Where(id => id != last.Id)
            .ToList();
        foreach (var id in others)
        {
            await TestAccounts.BlockAsync(factory, id);
        }

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeUserRoleCommand>>();
            var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");

            var result = await handler.Handle(
                new ChangeUserRoleCommand(ActorId: Guid.NewGuid(), last.Id, "Agent", nouakchott),
                CancellationToken.None);

            Assert.Equal(IdentityErrors.Account.LastActiveSupervisor, result.Error);
            Assert.Equal([last.Id], await TestAccounts.GetActiveSupervisorIdsAsync(factory));
        }
        finally
        {
            foreach (var id in others)
            {
                await TestAccounts.UnblockAsync(factory, id);
            }
        }
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<string> RefreshOkAsync(HttpClient client, string refreshCookie)
    {
        using var refresh = await client.RefreshAsync(refreshCookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var (token, _) = await refresh.ReadSessionAsync();
        return token;
    }

    private static async Task<JsonElement> MeAsync(HttpClient client, string token)
    {
        using var me = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        return await me.ReadJsonAsync();
    }

    private static async Task<JsonElement> ReadUserAsync(HttpClient client, string token, Guid id)
    {
        using var response = await client.GetUserAsync(token, id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }

    private static async Task<Guid> GetAgencyIdAsync(HawalaApiFactory factory, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, code);
    }
}
