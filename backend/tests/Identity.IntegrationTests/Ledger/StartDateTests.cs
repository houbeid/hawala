using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Ledger;

/// <summary>
/// Date de démarrage (phase 8, décision du 24 octobre 2026). Ce test a SA PROPRE base (IClassFixture, hors de la collection
/// partagée) : la date ne se fixe qu'une fois et fermerait la période 1975-1995 où les autres tests écrivent. Un seul
/// scénario, dans l'ordre : jamais future ; refusée s'il y a des écritures avant ; fixée ; pas deux fois ; ensuite, toute
/// opération datée avant est refusée (caisse, transfert, solde initial), le jour même est accepté. Données fictives.
/// </summary>
public sealed class StartDateTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task The_start_date_is_set_once_and_closes_the_period_before_it()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = today.AddDays(-3);
        using var cash = await client.CreateTreasuryAccountAsync(token, new { name = "Caisse démarrage", type = "cash", agencyId = nkc });
        var cashId = (await cash.ReadJsonAsync()).GetProperty("id").GetGuid();

        // Pas encore fixée.
        Assert.Equal(JsonValueKind.Null, (await GetAsync(client, token)).GetProperty("startDate").ValueKind);

        // Jamais future.
        using var future = await SetAsync(client, token, today.AddDays(1));
        Assert.Equal("Ledger.StartDate.Future", await future.ReadErrorCodeAsync());

        // Une écriture datée avant la date voulue : refus, avec le nombre d'écritures.
        await MovementAsync(client, token, nkc, cashId, start.AddDays(-1), HttpStatusCode.Created);
        using var tooLate = await SetAsync(client, token, start);
        Assert.Equal(HttpStatusCode.Conflict, tooLate.StatusCode);
        var body = await tooLate.ReadJsonAsync();
        Assert.Equal("Ledger.StartDate.EntriesBefore", body.GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("count").GetInt32());

        // Le jour de cette écriture convient (rien n'est daté AVANT), et un agent ne peut pas la fixer.
        using var byAgent = await SetAsync(client, agentToken, start.AddDays(-1));
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        using var set = await SetAsync(client, token, start.AddDays(-1));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(Day(start.AddDays(-1)), (await GetAsync(client, agentToken)).GetProperty("startDate").GetString());

        // Une seule fois.
        using var again = await SetAsync(client, token, start);
        Assert.Equal("Ledger.StartDate.AlreadySet", await again.ReadErrorCodeAsync());

        // Ensuite : la période d'avant est fermée, partout ; le jour de démarrage est accepté.
        var before = start.AddDays(-2);
        using var lateMovement = await MovementAsync(client, token, nkc, cashId, before, HttpStatusCode.BadRequest);
        var refused = await lateMovement.ReadJsonAsync();
        Assert.Equal("Ledger.Entry.BeforeStartDate", refused.GetProperty("code").GetString());
        Assert.Equal(Day(start.AddDays(-1)), refused.GetProperty("startDate").GetString());
        using var onStart = await MovementAsync(client, token, nkc, cashId, start.AddDays(-1), HttpStatusCode.Created);

        using var rate = await client.SetDailyRateAsync(token, before, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);
        using var transfer = await client.CreateTransferAsync(
            agentToken,
            new
            {
                destinationAgencyId = dkr,
                amountXof = 50_000m,
                senderInfo = "Expéditeur fictif",
                beneficiaryInfo = "Bénéficiaire fictif",
                operationAt = new DateTimeOffset(before.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                payments = new[] { new { treasuryAccountId = cashId, amount = 36_500m } },
            },
            Guid.NewGuid().ToString("N"));
        Assert.Equal("Ledger.Entry.BeforeStartDate", await transfer.ReadErrorCodeAsync());

        using var party = await client.CreatePartyAsync(token, new { name = "Tiers démarrage", type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();
        using var openingBefore = await client.SetOpeningBalanceAsync(token, partyId, nkc, new { amount = 1_000m, operationDate = Day(before) });
        using var openingOnStart = await client.SetOpeningBalanceAsync(token, partyId, nkc, new { amount = 1_000m, operationDate = Day(start.AddDays(-1)) });
        Assert.Equal("Ledger.Entry.BeforeStartDate", await openingBefore.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, openingOnStart.StatusCode);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<JsonElement> GetAsync(HttpClient client, string token)
    {
        using var response = await client.SendGetAsync(token, "/api/v1/setup/start-date");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string token, DateOnly date) =>
        client.SetStartDateAsync(token, Day(date));

    private static async Task<HttpResponseMessage> MovementAsync(
        HttpClient client, string token, Guid agencyId, Guid cashId, DateOnly date, HttpStatusCode expected)
    {
        var response = await client.CreateCashMovementAsync(
            token,
            new
            {
                agencyId,
                type = "OwnerContribution",
                treasuryAccountId = cashId,
                amount = 100_000m,
                operationAt = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero),
            },
            Guid.NewGuid().ToString("N"));
        Assert.True(response.StatusCode == expected, await response.Content.ReadAsStringAsync());
        return response;
    }
}
