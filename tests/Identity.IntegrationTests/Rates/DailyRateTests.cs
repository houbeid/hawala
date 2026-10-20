using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Rates;

/// <summary>
/// Taux du jour : fixés et changés par le Superviseur seul ; prix de vente visible de tous, prix d'achat avec profit.view.
/// La base est partagée et un taux est unique par date : chaque test utilise ses propres dates, tirées au hasard dans le passé.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DailyRateTests(ApiFixture fixture)
{
    [Fact]
    public async Task Supervisor_sets_the_rates_and_an_agent_sees_only_the_sale_rate()
    {
        var (factory, client, token) = await ArrangeAsync();
        var date = RandomPastDate();
        var agent = await TestAccounts.CreateAsync(factory);
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var set = await client.SetDailyRateAsync(token, date, 3600m, 3650.50m);

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var forSupervisor = await GetOkAsync(client, token, Format(date));
        Assert.Equal(Format(date), forSupervisor.GetProperty("date").GetString());
        Assert.Equal(3650.50m, forSupervisor.GetProperty("saleRate").GetDecimal());
        Assert.Equal(3600m, forSupervisor.GetProperty("purchaseRate").GetDecimal());

        // Règle validée : le prix d'achat est réservé au Superviseur.
        var forAgent = await GetOkAsync(client, agentToken, Format(date));
        Assert.Equal(3650.50m, forAgent.GetProperty("saleRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, forAgent.GetProperty("purchaseRate").ValueKind);
    }

    [Fact]
    public async Task Rates_of_a_day_can_be_changed()
    {
        var (_, client, token) = await ArrangeAsync();
        var date = RandomPastDate();
        using var first = await client.SetDailyRateAsync(token, date, 3600m, 3650m);

        using var second = await client.SetDailyRateAsync(token, date, 3610m, 3660m);

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var rate = await GetOkAsync(client, token, Format(date));
        Assert.Equal(3610m, rate.GetProperty("purchaseRate").GetDecimal());
        Assert.Equal(3660m, rate.GetProperty("saleRate").GetDecimal());
    }

    [Fact]
    public async Task Today_returns_the_rate_of_the_current_day()
    {
        var (_, client, token) = await ArrangeAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var set = await client.SetDailyRateAsync(token, today, 3600m, 3650m);

        var rate = await GetOkAsync(client, token, "today");

        Assert.Equal(Format(today), rate.GetProperty("date").GetString());
    }

    [Fact]
    public async Task History_lists_the_rates_of_a_period_most_recent_first()
    {
        var (_, client, token) = await ArrangeAsync();
        var older = RandomPastDate();
        var newer = older.AddDays(2);
        using var a = await client.SetDailyRateAsync(token, older, 3500m, 3550m);
        using var b = await client.SetDailyRateAsync(token, newer, 3520m, 3570m);

        using var response = await client.GetRatesAsync(token, string.Empty, $"from={Format(older)}&to={Format(newer)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dates = (await response.ReadJsonAsync()).EnumerateArray().Select(r => r.GetProperty("date").GetString()).ToList();
        Assert.Equal([Format(newer), Format(older)], dates);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Day_without_rate_is_not_found()
    {
        var (_, client, token) = await ArrangeAsync();

        using var response = await client.GetRatesAsync(token, "1999-01-01");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Rates.Rate.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_rates_are_refused_not_rounded()
    {
        var (_, client, token) = await ArrangeAsync();
        var date = RandomPastDate();

        using var tooPrecise = await client.SetDailyRateAsync(token, date, 3600.123m, 3650m);
        using var zeroSale = await client.SetDailyRateAsync(token, date, 3600m, 0m);
        using var missing = await client.SetDailyRateAsync(token, date, 3600m, null);

        Assert.Equal("Rates.Rate.PurchaseInvalid", await tooPrecise.ReadErrorCodeAsync());
        Assert.Equal("Rates.Rate.SaleInvalid", await zeroSale.ReadErrorCodeAsync());
        Assert.Equal("Rates.Rate.SaleInvalid", await missing.ReadErrorCodeAsync());

        using var read = await client.GetRatesAsync(token, Format(date));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
    }

    [Fact]
    public async Task Agent_cannot_set_a_rate_and_anonymous_cannot_read_one()
    {
        var (factory, client, _) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var byAgent = await client.SetDailyRateAsync(agentToken, RandomPastDate(), 3600m, 3650m);
        using var anonymous = await client.GetRatesAsync(accessToken: null, "today");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return (factory, client, token);
    }

    /// <summary>Une date passée tirée au hasard (2000 – 2024), pour ne pas croiser les autres tests.</summary>
    private static DateOnly RandomPastDate() => new DateOnly(2000, 1, 1).AddDays(Random.Shared.Next(0, 9_000));

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> GetOkAsync(HttpClient client, string token, string path)
    {
        using var response = await client.GetRatesAsync(token, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
}
