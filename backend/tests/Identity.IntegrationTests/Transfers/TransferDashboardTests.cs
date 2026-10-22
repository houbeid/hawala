using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Tableau de bord du Superviseur (décisions du 21 octobre 2026) : par caisse, dans sa devise, sur les transferts qu'elle a envoyés
/// (annulés exclus) : envoyé = livré + non livré, taux de livraison en nombre. Jour tiré au hasard (vente 3 650, achat 3 600) ;
/// la base est partagée : on mesure l'écart avant / après les créations.
///   NKC → DKR 50 000 FCFA = 36 500 MRO, livré ; NKC → DKR 10 000 FCFA = 7 300 MRO, non livré ;
///   NKC → DKR 20 000 FCFA = 14 600 MRO, annulé ; DKR → NKC 5 000 FCFA, livré.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferDashboardTests(ApiFixture fixture)
{
    [Fact]
    public async Task Each_caisse_shows_what_it_sent_in_its_currency_split_into_delivered_and_not_delivered()
    {
        var ctx = await ArrangeAsync();
        var before = await ctx.DashboardOkAsync();

        var a = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.Dkr, ctx.NkcCash, 50_000m, 36_500m);
        await ctx.CreateAsync(ctx.NkcAgentToken, ctx.Dkr, ctx.NkcCash, 10_000m, 7_300m);
        var cancelled = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.Dkr, ctx.NkcCash, 20_000m, 14_600m);
        var d = await ctx.CreateAsync(ctx.DkrAgentToken, ctx.Nkc, ctx.DkrCash, 5_000m, 5_000m);
        await ctx.DeliverAsync(ctx.DkrAgentToken, a, ctx.DkrCash, 50_000m);
        await ctx.DeliverAsync(ctx.NkcAgentToken, d, ctx.NkcCash, 3_650m);
        await ctx.CancelAsync(cancelled);

        var after = await ctx.DashboardOkAsync();

        var nkc = Caisse(after, ctx.Nkc);
        Assert.Equal("MRO", nkc.GetProperty("currency").GetString());
        AssertDelta(before, after, ctx.Nkc, "sent", 43_800m, 2);
        AssertDelta(before, after, ctx.Nkc, "delivered", 36_500m, 1);
        AssertDelta(before, after, ctx.Nkc, "notDelivered", 7_300m, 1);

        var dkr = Caisse(after, ctx.Dkr);
        Assert.Equal("XOF", dkr.GetProperty("currency").GetString());
        AssertDelta(before, after, ctx.Dkr, "sent", 5_000m, 1);
        AssertDelta(before, after, ctx.Dkr, "delivered", 5_000m, 1);
        AssertDelta(before, after, ctx.Dkr, "notDelivered", 0m, 0);

        foreach (var caisse in new[] { nkc, dkr })
        {
            var sent = caisse.GetProperty("sent");
            var delivered = caisse.GetProperty("delivered");
            var notDelivered = caisse.GetProperty("notDelivered");
            Assert.Equal(sent.GetProperty("amount").GetDecimal(),
                delivered.GetProperty("amount").GetDecimal() + notDelivered.GetProperty("amount").GetDecimal());
            Assert.Equal(
                (decimal)delivered.GetProperty("count").GetInt32() / sent.GetProperty("count").GetInt32() * 100m,
                caisse.GetProperty("deliveryRatePercent").GetDecimal());
        }
    }

    [Fact]
    public async Task A_day_without_transfers_gives_zero_cards_and_no_delivery_rate()
    {
        var ctx = await ArrangeAsync();

        // 1970 : aucun test ne crée de transfert avant 1975.
        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/transfers/dashboard?from=1970-01-01&to=1970-01-01");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.ReadJsonAsync();

        Assert.Equal(2, dashboard.GetProperty("caisses").GetArrayLength());
        var nkc = Caisse(dashboard, ctx.Nkc);
        Assert.Equal(0m, nkc.GetProperty("sent").GetProperty("amount").GetDecimal());
        Assert.Equal(0, nkc.GetProperty("sent").GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, nkc.GetProperty("deliveryRatePercent").ValueKind);
    }

    [Fact]
    public async Task Today_is_the_default_period_and_bad_inputs_or_other_roles_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var byDefault = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/transfers/dashboard");
        using var badPeriod = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, "/api/v1/transfers/dashboard?from=2026-10-10&to=2026-10-01");
        using var agent = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, "/api/v1/transfers/dashboard");
        using var anonymous = await ctx.Client.SendGetAsync(accessToken: null, "/api/v1/transfers/dashboard");

        var dashboard = await byDefault.ReadJsonAsync();
        Assert.Equal(dashboard.GetProperty("from").GetString(), dashboard.GetProperty("to").GetString());
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dashboard.GetProperty("to").GetString());
        Assert.Equal("Transfers.Filter.PeriodInvalid", await badPeriod.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, agent.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static JsonElement Caisse(JsonElement dashboard, Guid agencyId) =>
        dashboard.GetProperty("caisses").EnumerateArray().Single(c => c.GetProperty("agencyId").GetGuid() == agencyId);

    private static void AssertDelta(JsonElement before, JsonElement after, Guid agencyId, string card, decimal amount, int count)
    {
        var b = Caisse(before, agencyId).GetProperty(card);
        var a = Caisse(after, agencyId).GetProperty(card);
        Assert.Equal(amount, a.GetProperty("amount").GetDecimal() - b.GetProperty("amount").GetDecimal());
        Assert.Equal(count, a.GetProperty("count").GetInt32() - b.GetProperty("count").GetInt32());
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

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(token, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        return new Context(
            client, token, nkcToken, dkrToken, nkc, dkr, day,
            await CreateCashAsync(client, token, nkc), await CreateCashAsync(client, token, dkr));
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Nkc,
        Guid Dkr,
        DateOnly Day,
        Guid NkcCash,
        Guid DkrCash)
    {
        private string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private DateTimeOffset OperationAt => new(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        private readonly Dictionary<Guid, string> _codes = [];

        public async Task<Guid> CreateAsync(string token, Guid destination, Guid cash, decimal amountXof, decimal paid)
        {
            using var response = await Client.CreateTransferAsync(
                token,
                new
                {
                    destinationAgencyId = destination,
                    amountXof,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = cash, amount = paid } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            var id = body.GetProperty("id").GetGuid();
            _codes[id] = body.GetProperty("withdrawalCode").GetString()!;
            return id;
        }

        public async Task DeliverAsync(string token, Guid id, Guid cash, decimal amount)
        {
            using var response = await Client.DeliverTransferAsync(token, id, new
            {
                code = _codes[id],
                operationAt = OperationAt.AddHours(1),
                payments = new[] { new { treasuryAccountId = cash, amount } },
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        public async Task CancelAsync(Guid id)
        {
            using var response = await Client.CancelTransferAsync(SupervisorToken, id, "Erreur de saisie (fictif)", OperationAt.AddHours(1));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task<JsonElement> DashboardOkAsync()
        {
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/transfers/dashboard?from={DayText}&to={DayText}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
    }
}
