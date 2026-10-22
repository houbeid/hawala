using System.Net;
using System.Text.Json;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Audit;

/// <summary>
/// Lecture du journal d'audit (phase 6, règles validées le 22 octobre 2026) : permission audit.view (Superviseur),
/// seulement les actions, filtres, 50 par page, noms de l'auteur et du compte concerné ; périmètre par agence pour
/// un compte qui aurait reçu audit.view sans être Superviseur.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditReadTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_supervisor_reads_an_action_with_its_author_target_and_changes()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        using var limits = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/limits", new { perDay = 5_000m });
        Assert.Equal(HttpStatusCode.OK, limits.StatusCode);

        var line = Assert.Single(await ReadAsync(ctx, ctx.SupervisorToken, $"entityType=UserAccount&entityId={agent.Id}"));

        Assert.Equal("Identity.Account.LimitsChanged", line.GetProperty("action").GetString());
        Assert.Equal(agent.Id, line.GetProperty("entityId").GetGuid());
        Assert.Equal("Agent Test", line.GetProperty("entityName").GetString());
        Assert.Equal(ctx.Supervisor.Id, line.GetProperty("actor").GetProperty("id").GetGuid());
        Assert.Equal("Agent Test", line.GetProperty("actor").GetProperty("name").GetString());
        Assert.Equal(agent.AgencyId, line.GetProperty("agencyId").GetGuid());
        var change = Assert.Single(line.GetProperty("changes").EnumerateArray().ToList());
        Assert.Equal("perDay", change.GetProperty("field").GetString());
        Assert.Equal("5000", change.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Filters_on_author_and_action()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        using var limits = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/limits", new { perDay = 5_000m });
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.ProfitView];
        using var grant = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/permissions", new { permissions });

        var byAuthor = await ReadAsync(ctx, ctx.SupervisorToken, $"actorId={ctx.Supervisor.Id}");
        var permissionsOnly = await ReadAsync(ctx, ctx.SupervisorToken, $"actorId={ctx.Supervisor.Id}&action=Identity.Account.Permissions");

        Assert.Equal(2, byAuthor.Count);
        var line = Assert.Single(permissionsOnly);
        Assert.Equal("Identity.Account.PermissionsChanged", line.GetProperty("action").GetString());
    }

    [Fact]
    public async Task The_newest_line_comes_first_and_a_past_period_is_empty()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        using var first = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/limits", new { perDay = 5_000m });
        using var second = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{agent.Id}/limits", new { perDay = 6_000m });

        var today = await ReadAsync(ctx, ctx.SupervisorToken, $"entityId={agent.Id}");
        var past = await ReadAsync(ctx, ctx.SupervisorToken, $"entityId={agent.Id}&from=2000-01-01&to=2000-01-31");

        Assert.Equal(["6000", "5000"], today.Select(l => l.GetProperty("changes")[0].GetProperty("after").GetString()));
        Assert.Empty(past);
    }

    [Fact]
    public async Task Without_audit_view_the_journal_is_closed()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        var (token, _) = await ctx.Client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await ctx.Client.SendGetAsync(token, "/api/v1/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_reader_who_is_not_supervisor_sees_his_agency_only()
    {
        var ctx = await ArrangeAsync();
        var reader = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        var nouakchott = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        var dakar = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "DKR");
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.AuditView];
        using var grant = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{reader.Id}/permissions", new { permissions });
        using var nkc = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{nouakchott.Id}/limits", new { perDay = 5_000m });
        using var dkr = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{dakar.Id}/limits", new { perDay = 5_000m });
        var (token, _) = await ctx.Client.LoginOkAsync(reader.Email, reader.Password);

        var seen = await ReadAsync(ctx, token, $"actorId={ctx.Supervisor.Id}");
        var otherAgency = await ReadAsync(ctx, token, $"agencyId={dakar.AgencyId}");

        Assert.Contains(seen, l => l.GetProperty("entityId").GetGuid() == nouakchott.Id);
        Assert.DoesNotContain(seen, l => l.GetProperty("entityId").GetGuid() == dakar.Id);
        Assert.All(seen, l => Assert.Equal(reader.AgencyId, l.GetProperty("agencyId").GetGuid()));
        Assert.Empty(otherAgency);
    }

    [Fact]
    public async Task Invalid_filters_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var period = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/audit?from=2026-10-22&to=2026-10-01");
        using var page = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/audit?page=0");

        Assert.Equal("Audit.List.PeriodInvalid", await period.ReadErrorCodeAsync());
        Assert.Equal("Audit.List.PageInvalid", await page.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<List<JsonElement>> ReadAsync(Context ctx, string token, string query)
    {
        using var response = await ctx.Client.SendGetAsync(token, $"/api/v1/audit?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()];
    }

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return new Context(factory, client, supervisor, token);
    }

    private sealed record Context(HawalaApiFactory Factory, HttpClient Client, TestAccount Supervisor, string SupervisorToken);
}
