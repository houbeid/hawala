using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Cash;

/// <summary>
/// Contrôle de cohérence (alertes validées le 21 octobre 2026, sans blocage) : taux inhabituel (plus de 5 %), ajustement,
/// solde initial changé, mouvement et transfert annulés, livraison sans code, compte en négatif ; gardées avec la photo du budget.
/// La base est partagée : les tests ne regardent que les alertes de LEURS objets, sur une période qu'ils fixent.
/// Taux du jour du test : vente 3 650, achat 3 600.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CoherenceCheckTests(ApiFixture fixture)
{
    [Fact]
    public async Task Every_kind_of_alert_is_found_for_the_agency()
    {
        var ctx = await ArrangeAsync();
        var since = DateTimeOffset.UtcNow.AddSeconds(-2);

        // Taux : 3 900 est à 6,8 % de 3 650 (alerte) ; 3 800 à 4,1 % (rien).
        var unusual = await ctx.TransferAsync();
        await ctx.EditRateAsync(unusual, 3900m, paidMro: 39_000m);
        var usual = await ctx.TransferAsync();
        await ctx.EditRateAsync(usual, 3800m, paidMro: 38_000m);

        var cancelled = await ctx.TransferAsync();
        using (var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Client a renoncé"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        var withoutCode = await ctx.TransferAsync();
        using (var deliver = await ctx.Client.DeliverWithoutCodeAsync(ctx.SupervisorToken, withoutCode, new
        {
            idDocument = "CNI 0000000", reason = "Code perdu", operationAt = ctx.OperationAt.AddHours(1),
            payments = new[] { new { treasuryAccountId = ctx.DakarCash, amount = 50_000m } },
        }))
        {
            Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        }

        using (var adjust = await ctx.Client.CreateBalanceAdjustmentAsync(ctx.SupervisorToken, new
        {
            agencyId = ctx.Nkc, target = "TreasuryAccount", targetId = ctx.NkcCash, amount = 1_000m, reason = "Billets retrouvés",
        }))
        {
            Assert.Equal(HttpStatusCode.Created, adjust.StatusCode);
        }

        using (var opening = await ctx.Client.SetOpeningBalanceAsync(ctx.SupervisorToken, ctx.Party, ctx.Nkc, new { amount = 7_000m }))
        {
            Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);
        }

        var movement = await ctx.MovementAsync("Payment", 500_000m);
        using (var cancelMovement = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, movement, "Saisie en double"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancelMovement.StatusCode);
        }

        // Le wallet passe en négatif.
        await ctx.MovementAsync("Payment", 2_000m, ctx.NkcWallet);

        var alerts = await ctx.AlertsAsync(since);

        AssertAlert(alerts, "UnusualRate", unusual);
        Assert.DoesNotContain(alerts, a => a.GetProperty("sourceId").GetGuid() == usual);
        Assert.Equal("Client a renoncé", AssertAlert(alerts, "TransferCancelled", cancelled).GetProperty("reason").GetString());
        Assert.Equal("Code perdu", AssertAlert(alerts, "DeliveredWithoutCode", withoutCode).GetProperty("reason").GetString());
        Assert.Contains(alerts, a => a.GetProperty("kind").GetString() == "BalanceAdjustment"
            && a.GetProperty("reason").GetString() == "Billets retrouvés");
        Assert.Equal(7_000m, AssertAlert(alerts, "OpeningBalanceChanged", ctx.Party).GetProperty("amount").GetDecimal());
        Assert.Equal("Saisie en double", AssertAlert(alerts, "CashMovementCancelled", movement).GetProperty("reason").GetString());
        Assert.Equal(-2_000m, AssertAlert(alerts, "NegativeBalance", ctx.NkcWallet).GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Nothing_recorded_before_the_period_is_reported()
    {
        var ctx = await ArrangeAsync();
        var cancelled = await ctx.TransferAsync();
        using var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Client a renoncé");

        var later = await ctx.AlertsAsync(DateTimeOffset.UtcNow.AddSeconds(1));
        var earlier = await ctx.AlertsAsync(DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.DoesNotContain(later, a => a.GetProperty("sourceId").GetGuid() == cancelled);
        Assert.Contains(earlier, a => a.GetProperty("sourceId").GetGuid() == cancelled);
    }

    [Fact]
    public async Task Saving_the_budget_keeps_its_alerts()
    {
        var ctx = await ArrangeAsync();
        var cancelled = await ctx.TransferAsync();
        using var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Client a renoncé");

        using var saved = await ctx.Client.PostJsonAsync(ctx.SupervisorToken, "/api/v1/cash-budget/snapshots", new { agencyId = ctx.Nkc });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var snapshot = await saved.ReadJsonAsync();
        Assert.Equal(JsonValueKind.String, snapshot.GetProperty("checkedSince").ValueKind);
        Assert.Contains(snapshot.GetProperty("alerts").EnumerateArray(),
            a => a.GetProperty("kind").GetString() == "TransferCancelled" && a.GetProperty("sourceId").GetGuid() == cancelled);

        using var history = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/cash-budget/snapshots?agencyId={ctx.Nkc}");
        var today = (await history.ReadJsonAsync()).EnumerateArray().Last();
        Assert.Contains(today.GetProperty("alerts").EnumerateArray(), a => a.GetProperty("sourceId").GetGuid() == cancelled);
    }

    [Fact]
    public async Task An_agent_checks_only_its_agency()
    {
        var ctx = await ArrangeAsync();

        using var own = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, "/api/v1/cash-budget/coherence");
        using var other = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, $"/api/v1/cash-budget/coherence?agencyId={ctx.Dkr}");

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(ctx.Nkc, (await own.ReadJsonAsync()).GetProperty("agencyId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static JsonElement AssertAlert(IReadOnlyList<JsonElement> alerts, string kind, Guid sourceId) =>
        Assert.Single(alerts, a => a.GetProperty("kind").GetString() == kind
            && a.GetProperty("sourceId").ValueKind == JsonValueKind.String
            && a.GetProperty("sourceId").GetGuid() == sourceId);

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(token, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        using var party = await client.CreatePartyAsync(token, new { name = $"Tiers {Guid.NewGuid():N}"[..20], type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();

        return new Context(
            client, token, agentToken, nkc, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            await AccountAsync(client, token, nkc, "cash"),
            await AccountAsync(client, token, nkc, "wallet"),
            await AccountAsync(client, token, dkr, "cash"),
            partyId);
    }

    private static async Task<Guid> AccountAsync(HttpClient client, string token, Guid agencyId, string type)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Compte {Guid.NewGuid():N}"[..30], type, agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        Guid Nkc,
        Guid Dkr,
        DateTimeOffset OperationAt,
        Guid NkcCash,
        Guid NkcWallet,
        Guid DakarCash,
        Guid Party)
    {
        /// <summary>Nouakchott → Dakar, 50 000 FCFA à 3 650 = 36 500 MRO payés en caisse.</summary>
        public async Task<Guid> TransferAsync()
        {
            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 36_500m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task EditRateAsync(Guid id, decimal saleRate, decimal paidMro)
        {
            using var response = await Client.EditTransferAmountsAsync(SupervisorToken, id, new
            {
                version = 1,
                saleRate,
                payments = new[] { new { treasuryAccountId = NkcCash, amount = paidMro } },
                reason = "Taux négocié",
            });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task<Guid> MovementAsync(string type, decimal amount, Guid? account = null)
        {
            using var response = await Client.CreateCashMovementAsync(
                NkcAgentToken,
                new { type, treasuryAccountId = account ?? NkcCash, partyId = Party, amount, operationAt = OperationAt },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<IReadOnlyList<JsonElement>> AlertsAsync(DateTimeOffset since)
        {
            var sinceText = Uri.EscapeDataString(since.ToString("O", CultureInfo.InvariantCulture));
            using var response = await Client.SendGetAsync(
                SupervisorToken, $"/api/v1/cash-budget/coherence?agencyId={Nkc}&since={sinceText}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("alerts").EnumerateArray()];
        }
    }
}
