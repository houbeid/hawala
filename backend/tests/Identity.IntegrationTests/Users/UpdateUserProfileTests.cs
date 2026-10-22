using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed class UpdateUserProfileTests(ApiFixture fixture)
{
    // --- Modification réussie ----------------------------------------------

    [Fact]
    public async Task Profile_is_updated_and_the_session_continues()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var newEmail = NewEmail();
        var newPhone = NewPhone();

        using var response = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = newEmail,
            phone = newPhone,
            fullName = "  Nom Corrigé Fictif  ",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal(newEmail, user.GetProperty("email").GetString());
        Assert.Equal(newPhone, user.GetProperty("phone").GetString());
        Assert.Equal("Nom Corrigé Fictif", user.GetProperty("fullName").GetString());

        // Règle validée : la session n'est pas coupée.
        using var me = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // La prochaine connexion se fait avec le nouvel email ou le nouveau téléphone.
        using var byNewEmail = await client.LoginAsync(newEmail, agent.Password);
        using var byNewPhone = await client.LoginAsync(newPhone, agent.Password);
        using var byOldEmail = await client.LoginAsync(agent.Email, agent.Password);
        Assert.Equal(HttpStatusCode.OK, byNewEmail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byNewPhone.StatusCode);
        Assert.Equal("Identity.Auth.InvalidCredentials", await byOldEmail.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Empty_phone_removes_it()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = agent.Email,
            phone = "",
            fullName = "Agent Test",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal(JsonValueKind.Null, user.GetProperty("phone").ValueKind);

        using var byOldPhone = await client.LoginAsync(agent.Phone!, agent.Password);
        Assert.Equal("Identity.Auth.InvalidCredentials", await byOldPhone.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Keeping_own_email_and_phone_is_not_a_duplicate()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = agent.Email.ToUpperInvariant(),
            phone = agent.Phone,
            fullName = "Agent Renommé Fictif",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal(agent.Email, user.GetProperty("email").GetString());
        Assert.Equal("Agent Renommé Fictif", user.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Supervisor_can_update_own_profile()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.UpdateUserProfileAsync(token, supervisor.Id, new
        {
            email = supervisor.Email,
            fullName = "Superviseur Renommé Fictif",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var me = await client.MeAsync(token);
        Assert.Equal("Superviseur Renommé Fictif", (await me.ReadJsonAsync()).GetProperty("fullName").GetString());
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Email_or_phone_of_another_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, withPhone: true);
        var other = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var sameEmail = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = other.Email, phone = agent.Phone, fullName = "Agent Test",
        });
        using var samePhone = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = agent.Email, phone = other.Phone, fullName = "Agent Test",
        });

        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
        Assert.Equal("Identity.Account.EmailTaken", await sameEmail.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, samePhone.StatusCode);
        Assert.Equal("Identity.Account.PhoneTaken", await samePhone.ReadErrorCodeAsync());

        // Aucun refus n'a modifié le compte.
        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal(agent.Email, user.GetProperty("email").GetString());
        Assert.Equal(agent.Phone, user.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Invalid_fields_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var badEmail = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = "pas-un-email", fullName = "Agent Test",
        });
        using var badPhone = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = agent.Email, phone = "77 123 45 67", fullName = "Agent Test",
        });
        using var emptyName = await client.UpdateUserProfileAsync(supervisorToken, agent.Id, new
        {
            email = agent.Email, fullName = "   ",
        });

        Assert.Equal("Identity.Email.Invalid", await badEmail.ReadErrorCodeAsync());
        Assert.Equal("Identity.Phone.Invalid", await badPhone.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.FullNameEmpty", await emptyName.ReadErrorCodeAsync());

        var user = await ReadUserAsync(client, supervisorToken, agent.Id);
        Assert.Equal(agent.Email, user.GetProperty("email").GetString());
        Assert.Equal("Agent Test", user.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.UpdateUserProfileAsync(supervisorToken, Guid.NewGuid(), new
        {
            email = NewEmail(), fullName = "Inconnu Fictif",
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_update_profiles()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.UpdateUserProfileAsync(agentToken, agent.Id, new
        {
            email = agent.Email, fullName = "Agent Qui Se Renomme",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewEmail() => $"profil-{Guid.NewGuid():N}@example.com";

    // Même forme que TestAccounts : numéro mauritanien fictif, normalisé en E.164.
    private static string NewPhone() =>
        "+2223" + Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);

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
}
