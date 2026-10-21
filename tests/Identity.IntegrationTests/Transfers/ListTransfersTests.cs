using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Liste et fiche des transferts (règles validées le 20 octobre 2026) : envoyés et reçus par l'agence ;
/// répartition et crédit pour l'agence d'envoi et le Superviseur ; prix d'achat et bénéfice avec profit.view ;
/// période sur la date d'opération (aujourd'hui par défaut), 50 par page.
/// La base est partagée : chaque test marque ses transferts d'une étiquette unique dans l'expéditeur
/// et filtre la liste avec cette étiquette ; le taux est fixé sur une date tirée au hasard entre 1975 et 1995.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListTransfersTests(ApiFixture fixture)
{
    // --- Ce que chacun voit ------------------------------------------------

    [Fact]
    public async Task Sending_agency_sees_the_split_and_the_credit_but_not_the_purchase_rate()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateWithCreditAsync();

        var view = await ctx.GetOkAsync(ctx.NkcAgentToken, id);

        Assert.Equal("Sent", view.GetProperty("direction").GetString());
        Assert.Equal("NotDelivered", view.GetProperty("status").GetString());
        Assert.Equal(50_000m, view.GetProperty("amountXof").GetDecimal());
        Assert.Equal(36_500m, view.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3650m, view.GetProperty("saleRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("purchaseRate").ValueKind);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("profitMro").ValueKind);

        var payment = Assert.Single(view.GetProperty("payments").EnumerateArray());
        Assert.Equal(ctx.CashName, payment.GetProperty("treasuryAccountName").GetString());
        Assert.Equal(20_000m, payment.GetProperty("amount").GetDecimal());

        var credit = view.GetProperty("credit");
        Assert.Equal(ctx.PartyName, credit.GetProperty("partyName").GetString());
        Assert.Equal(16_500m, credit.GetProperty("amount").GetDecimal());
        Assert.Equal("MRO", credit.GetProperty("currency").GetString());

        // Le code de retrait n'est jamais renvoyé.
        Assert.False(view.TryGetProperty("withdrawalCode", out _));
        Assert.False(view.TryGetProperty("withdrawalCodeHash", out _));
    }

    [Fact]
    public async Task Destination_agency_sees_the_amounts_but_neither_the_split_nor_the_credit()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateWithCreditAsync();

        var view = await ctx.GetOkAsync(ctx.DkrAgentToken, id);

        Assert.Equal("Received", view.GetProperty("direction").GetString());
        Assert.Equal(50_000m, view.GetProperty("amountXof").GetDecimal());
        Assert.Equal(36_500m, view.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3650m, view.GetProperty("saleRate").GetDecimal());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("payments").ValueKind);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("credit").ValueKind);
        Assert.Equal(JsonValueKind.Null, view.GetProperty("purchaseRate").ValueKind);
    }

    [Fact]
    public async Task Supervisor_sees_the_purchase_rate_and_the_profit()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateWithCreditAsync();

        var view = await ctx.GetOkAsync(ctx.SupervisorToken, id);

        // 50 000 FCFA vendus 3 650, achetés 3 600 : 36 500 − 36 000 = 500 MRO.
        Assert.Equal(3600m, view.GetProperty("purchaseRate").GetDecimal());
        Assert.Equal(500m, view.GetProperty("profitMro").GetDecimal());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("direction").ValueKind);
        Assert.Equal(JsonValueKind.Array, view.GetProperty("payments").ValueKind);
        Assert.Equal(JsonValueKind.Object, view.GetProperty("credit").ValueKind);
    }

    [Fact]
    public async Task Unknown_transfer_is_not_found()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.GetTransferAsync(ctx.NkcAgentToken, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Transfers.Transfer.NotFound", await response.ReadErrorCodeAsync());
    }

    // --- Filtres -----------------------------------------------------------

    [Fact]
    public async Task Direction_separates_sent_and_received_for_each_agency()
    {
        var ctx = await ArrangeAsync();
        var fromNkc = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.NkcCash, 36_500m);
        var fromDkr = await ctx.CreateAsync(ctx.DkrAgentToken, ctx.DkrCash, 50_000m);

        Assert.Equal([fromNkc], await ctx.ListIdsAsync(ctx.NkcAgentToken, "direction=Sent"));
        Assert.Equal([fromDkr], await ctx.ListIdsAsync(ctx.NkcAgentToken, "direction=received"));
        Assert.Equal([fromDkr], await ctx.ListIdsAsync(ctx.DkrAgentToken, "direction=Sent"));

        // Sans sens : envoyés et reçus, du plus récent au plus ancien.
        Assert.Equal([fromDkr, fromNkc], await ctx.ListIdsAsync(ctx.NkcAgentToken));

        // Le Superviseur choisit l'agence pour lire le sens.
        Assert.Equal([fromNkc], await ctx.ListIdsAsync(ctx.SupervisorToken, $"agencyId={ctx.Nkc}&direction=Sent"));
    }

    [Fact]
    public async Task Status_period_and_search_filter_the_list()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.NkcCash, 36_500m);
        var nextDay = ctx.Day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal([id], await ctx.ListIdsAsync(ctx.NkcAgentToken, "status=NotDelivered"));
        Assert.Empty(await ctx.ListIdsAsync(ctx.NkcAgentToken, "status=Delivered"));
        Assert.Empty(await ctx.ListIdsAsync(ctx.NkcAgentToken, $"from={nextDay}&to={nextDay}", withPeriod: false));

        // Recherche sans tenir compte de la casse ; « % » est cherché tel quel, pas comme joker.
        Assert.Equal([id], await ctx.ListIdsAsync(ctx.NkcAgentToken, search: ctx.Tag.ToUpperInvariant()));
        Assert.Empty(await ctx.ListIdsAsync(ctx.NkcAgentToken, search: ctx.Tag + "%"));
    }

    [Fact]
    public async Task Period_defaults_to_today()
    {
        var ctx = await ArrangeAsync();
        using var rate = await ctx.Client.SetDailyRateAsync(
            ctx.SupervisorToken, DateOnly.FromDateTime(DateTime.UtcNow), 3600m, 3650m);
        var id = await ctx.CreateAsync(ctx.NkcAgentToken, ctx.NkcCash, 36_500m, operationAt: null);

        Assert.Equal([id], await ctx.ListIdsAsync(ctx.NkcAgentToken, withPeriod: false));
    }

    [Fact]
    public async Task List_is_paged_by_fifty_with_the_total_count()
    {
        var ctx = await ArrangeAsync();
        for (var i = 0; i < 51; i++)
        {
            await ctx.CreateAsync(ctx.NkcAgentToken, ctx.NkcCash, 3650m, amountXof: 5000m);
        }

        var first = await ctx.ListAsync(ctx.NkcAgentToken, "page=1");
        var second = await ctx.ListAsync(ctx.NkcAgentToken, "page=2");

        Assert.Equal(51, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(50, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(50, first.GetProperty("items").GetArrayLength());
        Assert.Equal(1, second.GetProperty("items").GetArrayLength());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task Invalid_filters_are_refused_with_their_code()
    {
        var ctx = await ArrangeAsync();

        Assert.Equal("Transfers.Filter.PageInvalid", await ctx.ListErrorAsync(ctx.NkcAgentToken, "page=0"));
        Assert.Equal("Transfers.Filter.PeriodInvalid", await ctx.ListErrorAsync(ctx.NkcAgentToken, "from=1990-01-02&to=1990-01-01"));
        Assert.Equal("Transfers.Filter.StatusInvalid", await ctx.ListErrorAsync(ctx.NkcAgentToken, "status=Paid"));
        Assert.Equal("Transfers.Filter.DirectionInvalid", await ctx.ListErrorAsync(ctx.NkcAgentToken, "direction=1"));
        Assert.Equal("Transfers.Filter.AgencyNotAllowed", await ctx.ListErrorAsync(ctx.NkcAgentToken, $"agencyId={ctx.Dkr}"));
        Assert.Equal("Transfers.Filter.DirectionNeedsAgency", await ctx.ListErrorAsync(ctx.SupervisorToken, "direction=Sent"));
    }

    [Fact]
    public async Task Anonymous_cannot_read_transfers()
    {
        var ctx = await ArrangeAsync();

        using var list = await ctx.Client.ListTransfersAsync(accessToken: null);
        using var one = await ctx.Client.GetTransferAsync(accessToken: null, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, one.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

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

        var tag = $"t{Guid.NewGuid():N}"[..12];
        var cashName = $"Caisse {tag}";
        var nkcCash = await CreateCashAsync(client, token, nkc, cashName);
        var dkrCash = await CreateCashAsync(client, token, dkr, $"Caisse D {tag}");

        using var party = await client.CreatePartyAsync(token, new { name = $"Débiteur {tag}", type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        return new Context(client, token, nkcToken, dkrToken, nkc, dkr, day, tag, nkcCash, dkrCash, cashName, partyId, $"Débiteur {tag}");
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId, string name)
    {
        using var response = await client.CreateTreasuryAccountAsync(token, new { name, type = "cash", agencyId });
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
        Guid DkrCash,
        string CashName,
        Guid PartyId,
        string PartyName)
    {
        private DateTimeOffset OperationAt => new(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        private string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Nouakchott → Dakar, 50 000 FCFA = 36 500 MRO : 20 000 payés, 16 500 à crédit.</summary>
        public async Task<Guid> CreateWithCreditAsync()
        {
            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = $"Expéditeur {Tag}",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 20_000m } },
                    creditPartyId = PartyId,
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        /// <summary>Transfert comptant depuis l'agence de l'agent, vers l'autre agence.</summary>
        public Task<Guid> CreateAsync(string agentToken, Guid cash, decimal paid, decimal amountXof = 50_000m) =>
            CreateAsync(agentToken, cash, paid, OperationAt, amountXof);

        public async Task<Guid> CreateAsync(
            string agentToken, Guid cash, decimal paid, DateTimeOffset? operationAt, decimal amountXof = 50_000m)
        {
            using var response = await Client.CreateTransferAsync(
                agentToken,
                new
                {
                    destinationAgencyId = cash == NkcCash ? Dkr : Nkc,
                    amountXof,
                    senderInfo = $"Expéditeur {Tag}",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt,
                    payments = new[] { new { treasuryAccountId = cash, amount = paid } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> GetOkAsync(string token, Guid id)
        {
            using var response = await Client.GetTransferAsync(token, id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        /// <summary>Liste filtrée sur l'étiquette du test (et le jour du test, sauf <paramref name="withPeriod"/> faux).</summary>
        public async Task<JsonElement> ListAsync(string token, string? extra = null, bool withPeriod = true, string? search = null)
        {
            var parts = new List<string> { $"search={Uri.EscapeDataString(search ?? Tag)}" };
            if (withPeriod)
            {
                parts.Add($"from={DayText}&to={DayText}");
            }

            if (!string.IsNullOrEmpty(extra))
            {
                parts.Add(extra);
            }

            using var response = await Client.ListTransfersAsync(token, string.Join('&', parts));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<IReadOnlyList<Guid>> ListIdsAsync(string token, string? extra = null, bool withPeriod = true, string? search = null)
        {
            var list = await ListAsync(token, extra, withPeriod, search);
            return [.. list.GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];
        }

        public async Task<string?> ListErrorAsync(string token, string query)
        {
            using var response = await Client.ListTransfersAsync(token, query);
            return await response.ReadErrorCodeAsync();
        }
    }
}
