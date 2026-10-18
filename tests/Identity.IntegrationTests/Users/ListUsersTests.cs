using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Users;

/// <summary>
/// La base est partagée entre les tests : chaque test crée ses comptes avec un mot unique dans le nom
/// et ne regarde que ceux-là (filtre « search »), jamais la taille totale de la liste.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListUsersTests(ApiFixture fixture)
{
    // --- Contenu de la liste ----------------------------------------------

    [Fact]
    public async Task List_shows_an_agent_with_its_agency_role_and_status()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var id = await CreateUserAsync(client, supervisorToken, $"Agent {tag}", "Agent", dakar);

        var user = Assert.Single(await ListAsync(client, supervisorToken, $"search={tag}"));

        Assert.Equal(id, user.GetProperty("id").GetGuid());
        Assert.Equal($"Agent {tag}", user.GetProperty("fullName").GetString());
        Assert.Equal("Agent", user.GetProperty("role").GetString());
        Assert.Equal("Active", user.GetProperty("status").GetString());
        Assert.True(user.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("lockedUntil").ValueKind);

        var agency = user.GetProperty("agency");
        Assert.Equal(dakar, agency.GetProperty("id").GetGuid());
        Assert.Equal("DKR", agency.GetProperty("code").GetString());
        Assert.Equal("#1565C0", agency.GetProperty("color").GetString());
    }

    [Fact]
    public async Task Supervisor_is_listed_without_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        await CreateUserAsync(client, supervisorToken, $"Chef {tag}", "Supervisor", agencyId: null);

        var user = Assert.Single(await ListAsync(client, supervisorToken, $"search={tag}"));

        Assert.Equal("Supervisor", user.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, user.GetProperty("agency").ValueKind);
    }

    [Fact]
    public async Task List_is_sorted_by_name()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        await CreateUserAsync(client, supervisorToken, $"Zeina {tag}", "Agent", nouakchott);
        await CreateUserAsync(client, supervisorToken, $"Amadou {tag}", "Agent", nouakchott);

        var names = (await ListAsync(client, supervisorToken, $"search={tag}"))
            .Select(u => u.GetProperty("fullName").GetString())
            .ToList();

        Assert.Equal([$"Amadou {tag}", $"Zeina {tag}"], names);
    }

    // --- Filtres -----------------------------------------------------------

    [Fact]
    public async Task Agency_filter_keeps_only_that_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        await CreateUserAsync(client, supervisorToken, $"Agent NKC {tag}", "Agent", nouakchott);
        var dakarId = await CreateUserAsync(client, supervisorToken, $"Agent DKR {tag}", "Agent", dakar);

        var user = Assert.Single(await ListAsync(client, supervisorToken, $"agencyId={dakar}&search={tag}"));

        Assert.Equal(dakarId, user.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Role_filter_keeps_only_that_role()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        await CreateUserAsync(client, supervisorToken, $"Agent {tag}", "Agent", nouakchott);
        var cashierId = await CreateUserAsync(client, supervisorToken, $"Caissier {tag}", "Cashier", nouakchott);

        var user = Assert.Single(await ListAsync(client, supervisorToken, $"role=cashier&search={tag}"));

        Assert.Equal(cashierId, user.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Blocked_accounts_are_listed_by_default_and_can_be_filtered()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        var activeId = await CreateUserAsync(client, supervisorToken, $"Actif {tag}", "Agent", nouakchott);
        var blockedId = await CreateUserAsync(client, supervisorToken, $"Bloque {tag}", "Agent", nouakchott);
        await TestAccounts.BlockAsync(factory, blockedId);

        var all = await ListAsync(client, supervisorToken, $"search={tag}");
        var blocked = Assert.Single(await ListAsync(client, supervisorToken, $"status=Blocked&search={tag}"));
        var active = Assert.Single(await ListAsync(client, supervisorToken, $"status=active&search={tag}"));

        Assert.Equal(2, all.Count);
        Assert.Equal(blockedId, blocked.GetProperty("id").GetGuid());
        Assert.Equal("Blocked", blocked.GetProperty("status").GetString());
        Assert.Equal(activeId, active.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Search_matches_name_email_and_phone_digits()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();
        var email = $"recherche-{tag}@example.com";
        // Même forme que TestAccounts : numéro mauritanien fictif en 3, 8 chiffres.
        var localNumber = "3" + Random.Shared.Next(1_000_000, 9_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var id = await CreateUserAsync(
            client, supervisorToken, $"Mariem {tag}", "Agent", await GetAgencyIdAsync(factory, "NKC"),
            email: email, phone: $"+222{localNumber}");

        var byName = await ListAsync(client, supervisorToken, $"search=mariem%20{tag}");
        var byEmail = await ListAsync(client, supervisorToken, $"search={Uri.EscapeDataString(email.ToUpperInvariant())}");
        var spacedNumber = $"{localNumber[..2]} {localNumber[2..4]} {localNumber[4..]}";
        var byPhone = await ListAsync(client, supervisorToken, $"search={Uri.EscapeDataString(spacedNumber)}");

        Assert.Equal(id, Assert.Single(byName).GetProperty("id").GetGuid());
        Assert.Equal(id, Assert.Single(byEmail).GetProperty("id").GetGuid());
        Assert.Contains(byPhone, u => u.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Locked_account_shows_when_the_lock_ends()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        for (var i = 0; i < 5; i++)
        {
            using var failed = await client.LoginAsync(agent.Email, "MauvaisMotDePasse9");
        }

        var user = Assert.Single(await ListAsync(client, supervisorToken, $"search={Uri.EscapeDataString(agent.Email)}"));
        var lockedUntil = user.GetProperty("lockedUntil").GetDateTimeOffset();

        Assert.InRange(lockedUntil, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Theory]
    [InlineData("role=Patron", "Identity.Account.RoleInvalid")]
    [InlineData("role=2", "Identity.Account.RoleInvalid")]
    [InlineData("status=Supprime", "Identity.Account.StatusInvalid")]
    public async Task Unknown_filter_value_is_refused(string queryString, string expectedCode)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ListUsersAsync(supervisorToken, queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await response.ReadErrorCodeAsync());
    }

    // --- Fiche d'un compte -------------------------------------------------

    [Fact]
    public async Task Location_returned_at_creation_leads_to_the_account()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var tag = NewTag();

        using var created = await client.CreateUserAsync(supervisorToken, new
        {
            email = $"fiche-{tag}@example.com",
            fullName = $"Fiche {tag}",
            role = "Agent",
            agencyId = await GetAgencyIdAsync(factory, "NKC"),
        });
        var id = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.GetUserAsync(supervisorToken, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"/api/v1/users/{id}", created.Headers.Location?.ToString());
        var user = await response.ReadJsonAsync();
        Assert.Equal(id, user.GetProperty("id").GetGuid());
        Assert.Equal($"fiche-{tag}@example.com", user.GetProperty("email").GetString());
        Assert.Equal("NKC", user.GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.GetUserAsync(supervisorToken, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    // --- Qui a le droit ----------------------------------------------------

    [Fact]
    public async Task Agent_cannot_list_or_read_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var list = await client.ListUsersAsync(agentToken);
        using var read = await client.GetUserAsync(agentToken, agent.Id);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_list_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.ListUsersAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewTag() => Guid.NewGuid().ToString("N")[..12];

    private static async Task<Guid> CreateUserAsync(
        HttpClient client,
        string supervisorToken,
        string fullName,
        string role,
        Guid? agencyId,
        string? email = null,
        string? phone = null)
    {
        using var response = await client.CreateUserAsync(supervisorToken, new
        {
            email = email ?? $"liste-{Guid.NewGuid():N}@example.com",
            phone,
            fullName,
            role,
            agencyId,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string token, string queryString)
    {
        using var response = await client.ListUsersAsync(token, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.ReadJsonAsync()).EnumerateArray()];
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
