using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Audit;

/// <summary>
/// Taux du jour, capital initial et couleur d'agence tracés dans l'audit (phase 6, tour 7d), dans la même transaction :
/// leurs modules travaillent maintenant sur la connexion partagée.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsAuditTests(ApiFixture fixture)
{
    [Fact]
    public async Task Setting_a_daily_rate_is_traced_once_per_change()
    {
        var ctx = await ArrangeAsync();
        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        var dayText = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        using var first = await ctx.Client.SetDailyRateAsync(ctx.Token, day, 3600m, 3650m);
        using var same = await ctx.Client.SetDailyRateAsync(ctx.Token, day, 3600m, 3650m);
        using var changed = await ctx.Client.SetDailyRateAsync(ctx.Token, day, 3600m, 3655.5m);

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        var lines = (await ReadAsync(ctx, $"action=Rates.DailyRate.Set&actorId={ctx.SupervisorId}"))
            .Where(l => Change(l, "date").After == dayText)
            .ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(("3650", "3655.5"), Change(lines[0], "saleRate"));
        Assert.DoesNotContain(lines[0].GetProperty("changes").EnumerateArray(), c => c.GetProperty("field").GetString() == "purchaseRate");
        Assert.Equal(((string?)null, "3600"), Change(lines[1], "purchaseRate"));
        Assert.Equal(JsonValueKind.Null, lines[0].GetProperty("agencyId").ValueKind);
    }

    [Fact]
    public async Task Setting_the_initial_capital_is_traced_with_the_previous_amount()
    {
        var ctx = await ArrangeAsync();

        using var first = await ctx.Client.PutJsonAsync(
            ctx.Token, "/api/v1/balance/capital", new { agencyId = ctx.Dkr, amount = 1_000_000m, reason = "Capital de départ" });
        using var second = await ctx.Client.PutJsonAsync(
            ctx.Token, "/api/v1/balance/capital", new { agencyId = ctx.Dkr, amount = 1_200_000m, reason = "Apport compté à part" });

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var line = (await ReadAsync(ctx, $"action=Balance.Capital.Set&actorId={ctx.SupervisorId}")).First();
        Assert.Equal(("1000000", "1200000"), Change(line, "initialCapital"));
        Assert.Equal("Apport compté à part", line.GetProperty("reason").GetString());
        Assert.Equal("Dakar", line.GetProperty("entityName").GetString());
        Assert.Equal(ctx.Dkr, line.GetProperty("agencyId").GetGuid());
    }

    [Fact]
    public async Task Changing_an_agency_color_is_traced()
    {
        var ctx = await ArrangeAsync();
        try
        {
            using var change = await ctx.Client.ChangeAgencyColorAsync(ctx.Token, ctx.Nkc, "#123456");

            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
            var line = (await ReadAsync(ctx, $"action=Organization.Agency.ColorChanged&actorId={ctx.SupervisorId}")).Single();
            Assert.Equal(("#1E8E3E", "#123456"), Change(line, "color"));
            Assert.Equal("Nouakchott", line.GetProperty("entityName").GetString());
        }
        finally
        {
            using var restore = await ctx.Client.ChangeAgencyColorAsync(ctx.Token, ctx.Nkc, "#1E8E3E");
            Assert.Equal(HttpStatusCode.NoContent, restore.StatusCode);
        }
    }

    // --- Outils ------------------------------------------------------------

    private static (string? Before, string? After) Change(JsonElement line, string field)
    {
        var change = line.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == field);
        return (change.GetProperty("before").GetString(), change.GetProperty("after").GetString());
    }

    private static async Task<List<JsonElement>> ReadAsync(Context ctx, string query)
    {
        using var response = await ctx.Client.SendGetAsync(ctx.Token, $"/api/v1/audit?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()];
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
        return new Context(client, supervisor.Id, token, nkc, dkr);
    }

    private sealed record Context(HttpClient Client, Guid SupervisorId, string Token, Guid Nkc, Guid Dkr);
}
