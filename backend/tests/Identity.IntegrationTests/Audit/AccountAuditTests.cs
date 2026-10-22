using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Audit;

/// <summary>
/// Actions sur les comptes tracées dans l'audit (phase 6, tour 7b) : création, profil, rôle, agence, blocage, déblocage,
/// réinitialisation du mot de passe ; une ligne par action réussie, seulement les champs changés, jamais de mot de passe.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountAuditTests(ApiFixture fixture)
{
    [Fact]
    public async Task Every_account_action_leaves_one_line_and_no_password()
    {
        var ctx = await ArrangeAsync();
        var email = $"audit-{Guid.NewGuid():N}@example.com";
        var passwords = new List<string>();

        using var create = await ctx.Client.CreateUserAsync(
            ctx.Token, new { email, fullName = "Compte Audité", role = "Agent", agencyId = ctx.Nkc });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        passwords.Add(created.GetProperty("temporaryPassword").GetString()!);

        using var profile = await ctx.Client.UpdateUserProfileAsync(ctx.Token, id, new { email, fullName = "Compte Renommé" });
        using var role = await ctx.Client.ChangeUserRoleAsync(ctx.Token, id, new { role = "Cashier", agencyId = ctx.Dkr });
        using var agency = await ctx.Client.ChangeUserAgencyAsync(ctx.Token, id, new { agencyId = ctx.Nkc });
        using var block = await ctx.Client.BlockUserAsync(ctx.Token, id);
        using var unblock = await ctx.Client.UnblockUserAsync(ctx.Token, id);
        passwords.Add((await unblock.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!);
        using var reset = await ctx.Client.ResetUserPasswordAsync(ctx.Token, id);
        passwords.Add((await reset.ReadJsonAsync()).GetProperty("temporaryPassword").GetString()!);

        using var response = await ctx.Client.SendGetAsync(ctx.Token, $"/api/v1/audit?entityType=UserAccount&entityId={id}");
        var text = await response.Content.ReadAsStringAsync();
        var lines = (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();

        Assert.Equal(
            [
                "Identity.Account.PasswordReset",
                "Identity.Account.Unblocked",
                "Identity.Account.Blocked",
                "Identity.Account.AgencyChanged",
                "Identity.Account.RoleChanged",
                "Identity.Account.ProfileUpdated",
                "Identity.Account.Created",
            ],
            lines.Select(l => l.GetProperty("action").GetString()));
        Assert.All(lines, l => Assert.Equal(ctx.SupervisorId, l.GetProperty("actor").GetProperty("id").GetGuid()));
        Assert.All(passwords, password => Assert.DoesNotContain(password, text, StringComparison.Ordinal));

        Assert.Equal(("Agent", "Cashier"), Change(lines[4], "role"));
        Assert.Equal((ctx.Nkc.ToString(), ctx.Dkr.ToString()), Change(lines[4], "agencyId"));
        Assert.Equal((ctx.Dkr.ToString(), ctx.Nkc.ToString()), Change(lines[3], "agencyId"));
        Assert.Equal(("Compte Audité", "Compte Renommé"), Change(lines[5], "fullName"));
        Assert.Single(lines[5].GetProperty("changes").EnumerateArray());
        Assert.Equal(("Active", "Blocked"), Change(lines[2], "status"));
        Assert.Empty(lines[0].GetProperty("changes").EnumerateArray());
    }

    [Fact]
    public async Task An_action_that_changes_nothing_is_not_traced()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");

        using var sameProfile = await ctx.Client.UpdateUserProfileAsync(ctx.Token, agent.Id, new { email = agent.Email, fullName = "Agent Test" });
        using var sameRole = await ctx.Client.ChangeUserRoleAsync(ctx.Token, agent.Id, new { role = "Agent", agencyId = agent.AgencyId });
        using var sameAgency = await ctx.Client.ChangeUserAgencyAsync(ctx.Token, agent.Id, new { agencyId = agent.AgencyId });

        Assert.Equal(HttpStatusCode.NoContent, sameProfile.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sameRole.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sameAgency.StatusCode);
        using var response = await ctx.Client.SendGetAsync(ctx.Token, $"/api/v1/audit?entityId={agent.Id}");
        Assert.Empty((await response.ReadJsonAsync()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_refused_action_leaves_no_line()
    {
        var ctx = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        using var first = await ctx.Client.BlockUserAsync(ctx.Token, agent.Id);

        using var again = await ctx.Client.BlockUserAsync(ctx.Token, agent.Id);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using var response = await ctx.Client.SendGetAsync(ctx.Token, $"/api/v1/audit?entityId={agent.Id}");
        Assert.Single((await response.ReadJsonAsync()).GetProperty("items").EnumerateArray());
    }

    // --- Outils ------------------------------------------------------------

    private static (string? Before, string? After) Change(JsonElement line, string field)
    {
        var change = line.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == field);
        return (change.GetProperty("before").GetString(), change.GetProperty("after").GetString());
    }

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return new Context(factory, client, supervisor.Id, token, nkc, dkr);
    }

    private sealed record Context(HawalaApiFactory Factory, HttpClient Client, Guid SupervisorId, string Token, Guid Nkc, Guid Dkr);
}
