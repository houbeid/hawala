using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Cartes du panneau des transferts (lues sur l'app actuelle le 21 octobre 2026) : pour l'agence choisie A,
/// entrées A et livré · B (transferts A → B), envoyé · B et sorties A (transferts B → A), non livrés, bénéfice, taux.
/// Jeu de données d'un jour tiré au hasard (vente 3 650, achat 3 600), isolé par une étiquette dans l'expéditeur :
///   A : NKC → DKR 50 000 FCFA = 36 500 MRO, livré ;   B : NKC → DKR 10 000 FCFA = 7 300 MRO, non livré ;
///   C : DKR → NKC 20 000 FCFA = 14 600 MRO, livré ;   D : DKR → NKC  5 000 FCFA = 3 650 MRO, non livré.
/// Bénéfice : A 500 + B 100 + C (−200) + D (−50) = 350 MRO.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferSummaryTests(ApiFixture fixture)
{
    [Fact]
    public async Task Cards_of_nouakchott_show_both_groups_in_both_currencies()
    {
        var ctx = await ArrangeAsync();

        var cards = await ctx.SummaryOkAsync(ctx.Nkc);

        Assert.Equal("MRO", cards.GetProperty("currency").GetString());
        Assert.Equal("XOF", cards.GetProperty("otherCurrency").GetString());
        AssertAmount(cards, "incoming", 43_800m, 2);
        AssertAmount(cards, "deliveredByOther", 50_000m, 1);
        AssertAmount(cards, "sentByOther", 25_000m, 2);
        AssertAmount(cards, "outgoing", 14_600m, 1);
        AssertAmount(cards, "notDelivered", 3_650m, 1);
        Assert.Equal(350m, cards.GetProperty("profitMro").GetDecimal());
        Assert.Equal(3650m, cards.GetProperty("saleRate").GetDecimal());
        Assert.Equal(3600m, cards.GetProperty("purchaseRate").GetDecimal());
    }

    [Fact]
    public async Task Cards_of_dakar_are_the_mirror_image()
    {
        var ctx = await ArrangeAsync();

        var cards = await ctx.SummaryOkAsync(ctx.Dkr);

        Assert.Equal("XOF", cards.GetProperty("currency").GetString());
        AssertAmount(cards, "incoming", 25_000m, 2);
        AssertAmount(cards, "outgoing", 50_000m, 1);
        AssertAmount(cards, "sentByOther", 43_800m, 2);
        AssertAmount(cards, "deliveredByOther", 14_600m, 1);
        AssertAmount(cards, "notDelivered", 10_000m, 1);

        // Le bénéfice ne dépend pas de l'agence choisie.
        Assert.Equal(350m, cards.GetProperty("profitMro").GetDecimal());
    }

    [Fact]
    public async Task Another_day_shows_empty_cards_and_no_rate()
    {
        var ctx = await ArrangeAsync();

        // 1970 : aucun test ne fixe de taux avant 1975.
        using var response = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, $"/api/v1/transfers/summary?agencyId={ctx.Nkc}&from=1970-01-01&to=1970-01-01&search={ctx.Tag}");
        var cards = await response.ReadJsonAsync();

        AssertAmount(cards, "incoming", 0m, 0);
        Assert.Equal(0m, cards.GetProperty("profitMro").GetDecimal());
        Assert.Equal(JsonValueKind.Null, cards.GetProperty("saleRate").ValueKind);
    }

    [Fact]
    public async Task Cards_are_for_the_supervisor_only_and_need_an_agency()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, $"/api/v1/transfers/summary?agencyId={ctx.Nkc}");
        using var withoutAgency = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/transfers/summary");
        using var unknownAgency = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/transfers/summary?agencyId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Transfers.Filter.AgencyRequired", await withoutAgency.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Filter.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task List_gives_the_count_of_all_visible_transfers_next_to_the_filtered_count()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.ListTransfersAsync(ctx.NkcAgentToken, $"from={ctx.DayText}&to={ctx.DayText}&search={ctx.Tag}");
        var list = await response.ReadJsonAsync();

        Assert.Equal(4, list.GetProperty("totalCount").GetInt32());
        Assert.True(list.GetProperty("allCount").GetInt32() >= 4);
    }

    // --- Outils ------------------------------------------------------------

    private static void AssertAmount(JsonElement cards, string name, decimal amount, int count)
    {
        var card = cards.GetProperty(name);
        Assert.Equal(amount, card.GetProperty("amount").GetDecimal());
        Assert.Equal(count, card.GetProperty("count").GetInt32());
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

        var ctx = new Context(
            client, token, nkcToken, dkrToken, nkc, dkr, day, $"s{Guid.NewGuid():N}"[..12],
            await CreateCashAsync(client, token, nkc), await CreateCashAsync(client, token, dkr));

        var a = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.Dkr, ctx.NkcCash, 50_000m, 36_500m);
        await ctx.CreateAsync(ctx.NkcAgentToken, ctx.Dkr, ctx.NkcCash, 10_000m, 7_300m);
        var c = await ctx.CreateAsync(ctx.DkrAgentToken, ctx.Nkc, ctx.DkrCash, 20_000m, 20_000m);
        await ctx.CreateAsync(ctx.DkrAgentToken, ctx.Nkc, ctx.DkrCash, 5_000m, 5_000m);

        await ctx.DeliverAsync(ctx.DkrAgentToken, a, ctx.DkrCash, 50_000m);
        await ctx.DeliverAsync(ctx.NkcAgentToken, c, ctx.NkcCash, 14_600m);
        return ctx;
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
        string Tag,
        Guid NkcCash,
        Guid DkrCash)
    {
        public string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
                    senderInfo = $"Expéditeur {Tag}",
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

        public async Task<JsonElement> SummaryOkAsync(Guid agencyId)
        {
            using var response = await Client.SendGetAsync(
                SupervisorToken, $"/api/v1/transfers/summary?agencyId={agencyId}&from={DayText}&to={DayText}&search={Tag}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
    }
}
