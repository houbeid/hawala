using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed partial class CreateUserTests(ApiFixture fixture)
{
    [GeneratedRegex("^[A-Za-z2-9]{3}-[A-Za-z2-9]{3}-[A-Za-z2-9]{3}$")]
    private static partial Regex TemporaryPasswordFormat();

    // --- Création réussie --------------------------------------------------

    [Fact]
    public async Task Supervisor_creates_an_agent_who_must_change_the_generated_password()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var email = NewEmail();

        using var response = await client.CreateUserAsync(supervisorToken, new
        {
            email,
            phone = "+221 77 123 45 67",
            fullName = "Agent Dakar Fictif",
            role = "Agent",
            agencyId = dakar,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        var temporaryPassword = created.GetProperty("temporaryPassword").GetString()!;
        Assert.Equal($"/api/v1/users/{id}", response.Headers.Location?.ToString());
        Assert.Matches(TemporaryPasswordFormat(), temporaryPassword);
        Assert.Contains(temporaryPassword, char.IsAsciiDigit);

        // Le nouvel agent se connecte (par téléphone) avec le mot de passe généré…
        using var login = await client.LoginAsync("00221771234567", temporaryPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.ReadJsonAsync()).GetProperty("mustChangePassword").GetBoolean());
        var (agentToken, _) = await login.ReadSessionAsync();

        // … doit le changer, puis retrouve son agence.
        using var change = await client.ChangePasswordAsync(agentToken, temporaryPassword, "NouveauMotDePasse2");
        var (newToken, _) = await change.ReadSessionAsync();
        using var me = await client.MeAsync(newToken);
        var meBody = await me.ReadJsonAsync();
        Assert.Equal(id, meBody.GetProperty("id").GetGuid());
        Assert.Equal("Agent", meBody.GetProperty("role").GetString());
        Assert.Equal("DKR", meBody.GetProperty("agency").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Supervisor_creates_another_supervisor_without_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(),
            fullName = "Second Superviseur Fictif",
            role = "supervisor",   // insensible à la casse
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Two_generated_passwords_are_different()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        using var first = await client.CreateUserAsync(supervisorToken, AgentBody(nouakchott));
        using var second = await client.CreateUserAsync(supervisorToken, AgentBody(nouakchott));

        Assert.NotEqual(
            (await first.ReadJsonAsync()).GetProperty("temporaryPassword").GetString(),
            (await second.ReadJsonAsync()).GetProperty("temporaryPassword").GetString());
    }

    // --- Qui a le droit ----------------------------------------------------

    [Fact]
    public async Task Agent_cannot_create_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.CreateUserAsync(agentToken, AgentBody(agent.AgencyId!.Value));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Supervisor_with_temporary_password_cannot_create_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor, mustChangePassword: true);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.CreateUserAsync(token, AgentBody(await GetAgencyIdAsync(factory, "NKC")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_create_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.CreateUserAsync(accessToken: null, new { email = NewEmail() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Refus métier ------------------------------------------------------

    [Fact]
    public async Task Duplicate_email_or_phone_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var existing = await TestAccounts.CreateAsync(factory, withPhone: true);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var sameEmail = await client.CreateUserAsync(supervisorToken, new
        {
            email = existing.Email.ToUpperInvariant(),
            fullName = "Doublon Fictif",
            role = "Agent",
            agencyId = existing.AgencyId,
        });
        using var samePhone = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(),
            phone = existing.Phone,
            fullName = "Doublon Fictif",
            role = "Agent",
            agencyId = existing.AgencyId,
        });

        Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
        Assert.Equal("Identity.Account.EmailTaken", await sameEmail.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, samePhone.StatusCode);
        Assert.Equal("Identity.Account.PhoneTaken", await samePhone.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agency_rules_are_enforced()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");

        using var agentWithoutAgency = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(), fullName = "Agent Fictif", role = "Cashier",
        });
        using var supervisorWithAgency = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(), fullName = "Chef Fictif", role = "Supervisor", agencyId = nouakchott,
        });
        using var unknownAgency = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(), fullName = "Agent Fictif", role = "Agent", agencyId = Guid.NewGuid(),
        });

        Assert.Equal("Identity.Account.AgencyRequired", await agentWithoutAgency.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.AgencyNotAllowedForSupervisor", await supervisorWithAgency.ReadErrorCodeAsync());
        Assert.Equal("Identity.Account.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    [Theory]
    [InlineData("Patron")]
    [InlineData("2")]
    [InlineData("")]
    public async Task Unknown_role_is_refused(string role)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.CreateUserAsync(supervisorToken, new
        {
            email = NewEmail(), fullName = "Agent Fictif", role, agencyId = await GetAgencyIdAsync(factory, "NKC"),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Identity.Account.RoleInvalid", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_email_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.CreateUserAsync(supervisorToken, new
        {
            email = "pas-un-email", fullName = "Agent Fictif", role = "Agent",
            agencyId = await GetAgencyIdAsync(factory, "NKC"),
        });

        Assert.Equal("Identity.Email.Invalid", await response.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static string NewEmail() => $"nouveau-{Guid.NewGuid():N}@example.com";

    private static object AgentBody(Guid agencyId) => new
    {
        email = NewEmail(),
        fullName = "Agent Fictif",
        role = "Agent",
        agencyId,
    };

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
