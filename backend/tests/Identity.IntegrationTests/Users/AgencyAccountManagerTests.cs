using System.Net;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Users;

/// <summary>
/// Un non-Superviseur qui a reçu users.manage (règles validées le 21 octobre 2026) : il gère les comptes
/// de SON agence (lire, créer en Agent / Caissier / Consultation, modifier le profil, bloquer, débloquer,
/// réinitialiser), jamais un Superviseur ni un compte de l'autre agence (404), et ne change ni rôle, ni agence.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AgencyAccountManagerTests(ApiFixture fixture)
{
    [Fact]
    public async Task List_shows_only_accounts_of_his_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (manager, token) = await CreateManagerAsync(factory, client);
        var colleague = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");

        using var response = await client.ListUsersAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var users = (await response.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Contains(users, u => u.GetProperty("id").GetGuid() == colleague.Id);
        Assert.Contains(users, u => u.GetProperty("id").GetGuid() == manager.Id);
        Assert.DoesNotContain(users, u => u.GetProperty("id").GetGuid() == dakar.Id);
        Assert.All(users, u => Assert.Equal("NKC", u.GetProperty("agency").GetProperty("code").GetString()));
        Assert.DoesNotContain(users, u => u.GetProperty("role").GetString() == "Supervisor");
    }

    [Fact]
    public async Task Asking_for_the_other_agency_gives_an_empty_list()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (_, token) = await CreateManagerAsync(factory, client);
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");

        using var response = await client.ListUsersAsync(token, $"agencyId={dakar.AgencyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.ReadJsonAsync()).EnumerateArray());
    }

    [Fact]
    public async Task Accounts_outside_his_agency_do_not_exist_for_him()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (_, token) = await CreateManagerAsync(factory, client);
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);

        foreach (var target in new[] { dakar.Id, supervisor.Id })
        {
            using var get = await client.GetUserAsync(token, target);
            using var permissions = await client.SendGetAsync(token, $"/api/v1/users/{target}/permissions");
            using var block = await client.BlockUserAsync(token, target);
            using var unblock = await client.UnblockUserAsync(token, target);
            using var reset = await client.ResetUserPasswordAsync(token, target);
            using var profile = await client.UpdateUserProfileAsync(
                token, target, new { email = $"autre-{Guid.NewGuid():N}@example.com", fullName = "Nom Changé" });

            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, permissions.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, block.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, unblock.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, reset.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, profile.StatusCode);
        }

        // Rien n'a bougé : le compte de Dakar fonctionne toujours.
        using var login = await client.LoginAsync(dakar.Email, dakar.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task He_manages_an_account_of_his_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (_, token) = await CreateManagerAsync(factory, client);
        var colleague = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");

        using var get = await client.GetUserAsync(token, colleague.Id);
        using var permissions = await client.SendGetAsync(token, $"/api/v1/users/{colleague.Id}/permissions");
        using var profile = await client.UpdateUserProfileAsync(
            token, colleague.Id, new { email = colleague.Email, fullName = "Collègue Renommé" });
        using var block = await client.BlockUserAsync(token, colleague.Id);
        using var unblock = await client.UnblockUserAsync(token, colleague.Id);
        using var reset = await client.ResetUserPasswordAsync(token, colleague.Id);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, profile.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, block.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unblock.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
    }

    [Fact]
    public async Task He_creates_agency_roles_in_his_agency_only()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (manager, token) = await CreateManagerAsync(factory, client);
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");

        using var cashier = await client.CreateUserAsync(token, NewUser("Cashier", manager.AgencyId));
        using var readOnly = await client.CreateUserAsync(token, NewUser("ReadOnly", manager.AgencyId));
        using var supervisor = await client.CreateUserAsync(token, NewUser("Supervisor", agencyId: null));
        using var otherAgency = await client.CreateUserAsync(token, NewUser("Agent", dakar.AgencyId));

        Assert.Equal(HttpStatusCode.Created, cashier.StatusCode);
        Assert.Equal(HttpStatusCode.Created, readOnly.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, supervisor.StatusCode);
        Assert.Equal("Identity.Account.SupervisorOnly", await supervisor.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, otherAgency.StatusCode);
        Assert.Equal("Identity.Account.OtherAgency", await otherAgency.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task He_changes_neither_role_nor_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (manager, token) = await CreateManagerAsync(factory, client);
        var colleague = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");

        using var role = await client.ChangeUserRoleAsync(
            token, colleague.Id, new { role = "Cashier", agencyId = manager.AgencyId });
        using var agency = await client.ChangeUserAgencyAsync(token, colleague.Id, new { agencyId = dakar.AgencyId });

        Assert.Equal(HttpStatusCode.Forbidden, role.StatusCode);
        Assert.Equal("Identity.Account.SupervisorOnly", await role.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, agency.StatusCode);
        Assert.Equal("Identity.Account.SupervisorOnly", await agency.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Moved_to_the_other_agency_he_manages_the_other_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var (manager, _) = await CreateManagerAsync(factory, client);
        var nouakchott = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        await TestAccounts.MoveToAgencyAsync(factory, manager.Id, "DKR");
        var (token, _) = await client.LoginOkAsync(manager.Email, manager.Password);

        using var ownAgency = await client.GetUserAsync(token, dakar.Id);
        using var formerAgency = await client.GetUserAsync(token, nouakchott.Id);

        Assert.Equal(HttpStatusCode.OK, ownAgency.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, formerAgency.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Agent de Nouakchott à qui un Superviseur a donné users.manage ; renvoie son jeton à jour.</summary>
    private static async Task<(TestAccount Manager, string Token)> CreateManagerAsync(HawalaApiFactory factory, HttpClient client)
    {
        var manager = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.UsersManage];

        using var grant = await client.PutJsonAsync(supervisorToken, $"/api/v1/users/{manager.Id}/permissions", new { permissions });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var (token, _) = await client.LoginOkAsync(manager.Email, manager.Password);
        return (manager, token);
    }

    private static object NewUser(string role, Guid? agencyId) => new
    {
        email = $"nouveau-{Guid.NewGuid():N}@example.com",
        fullName = "Nouveau Compte",
        role,
        agencyId,
    };
}
