using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Auth;

[Collection(ApiCollection.Name)]
public sealed class MeTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("NKC", "Nouakchott", "#1E8E3E")]
    [InlineData("DKR", "Dakar", "#1565C0")]
    public async Task Agent_sees_its_own_agency(string code, string name, string color)
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory, agencyCode: code);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(account.Email, account.Password);

        using var response = await client.MeAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var agency = (await response.ReadJsonAsync()).GetProperty("agency");
        Assert.Equal(account.AgencyId, agency.GetProperty("id").GetGuid());
        Assert.Equal(code, agency.GetProperty("code").GetString());
        Assert.Equal(name, agency.GetProperty("name").GetString());
        Assert.Equal(color, agency.GetProperty("color").GetString());
    }

    [Fact]
    public async Task Supervisor_has_no_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.MeAsync(token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.ReadJsonAsync();
        Assert.Equal("Supervisor", me.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("agency").ValueKind);
    }

    [Fact]
    public async Task Moving_an_agent_disconnects_it_and_it_returns_with_the_new_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        using var client = factory.CreateApiClient();
        var (oldToken, _) = await client.LoginOkAsync(account.Email, account.Password);

        await TestAccounts.MoveToAgencyAsync(factory, account.Id, "DKR");

        // L'ancien jeton portait l'ancien périmètre : refusé tout de suite (SecurityVersion a changé).
        using var withOldToken = await client.MeAsync(oldToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);

        var (newToken, _) = await client.LoginOkAsync(account.Email, account.Password);
        using var withNewToken = await client.MeAsync(newToken);
        var agency = (await withNewToken.ReadJsonAsync()).GetProperty("agency");
        Assert.Equal("DKR", agency.GetProperty("code").GetString());
    }
}