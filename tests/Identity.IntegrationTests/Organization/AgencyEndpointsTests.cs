using System.Net;
using System.Text.Json;
using Identity.IntegrationTests.Infrastructure;
using Organization.Infrastructure;

namespace Identity.IntegrationTests.Organization;

[Collection(ApiCollection.Name)]
public sealed class AgencyEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Both_agencies_are_created_at_startup_with_their_currency_and_color()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var response = await client.GetAgenciesAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agencies = (await response.ReadJsonAsync()).EnumerateArray().ToList();

        // Triées par code : DKR puis NKC.
        Assert.Equal(2, agencies.Count);
        AssertAgency(agencies[0], "DKR", "Dakar", "Senegal", "XOF", "#1565C0", "Africa/Dakar");
        AssertAgency(agencies[1], "NKC", "Nouakchott", "Mauritania", "MRO", "#1E8E3E", "Africa/Nouakchott");
    }

    [Fact]
    public async Task Seeding_again_does_not_duplicate_the_agencies()
    {
        var factory = await fixture.GetFactoryAsync();

        // Comme un redémarrage de l'API : migrations + seed relancés sur la même base.
        await factory.Services.InitializeOrganizationModuleAsync();

        var account = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var response = await client.GetAgenciesAsync(token);

        Assert.Equal(2, (await response.ReadJsonAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Agencies_require_authentication()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.GetAgenciesAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Temporary_password_account_cannot_read_agencies()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory, mustChangePassword: true);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var response = await client.GetAgenciesAsync(token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static void AssertAgency(
        JsonElement agency, string code, string name, string country, string currency, string color, string timeZoneId)
    {
        Assert.NotEqual(Guid.Empty, agency.GetProperty("id").GetGuid());
        Assert.Equal(code, agency.GetProperty("code").GetString());
        Assert.Equal(name, agency.GetProperty("name").GetString());
        Assert.Equal(country, agency.GetProperty("country").GetString());

        // Devise en texte grâce au convertisseur global d'énumérations.
        Assert.Equal(currency, agency.GetProperty("currency").GetString());
        Assert.Equal(color, agency.GetProperty("color").GetString());
        Assert.Equal(timeZoneId, agency.GetProperty("timeZoneId").GetString());
    }
}