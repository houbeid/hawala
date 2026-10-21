using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Cash;

/// <summary>
/// Budget de la caisse (règles validées) : espèces + banques + soldes débiteurs − soldes créditeurs − transferts non livrés,
/// tout lu dans le grand livre ; photo du jour (une par agence et par jour, remplacée le même jour) ; historique.
/// La base est partagée et le budget additionne tous les comptes de l'agence : les tests mesurent des ÉCARTS
/// (avant / après leurs propres opérations) et vérifient le détail sur leurs propres comptes et tiers.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CashBudgetTests(ApiFixture fixture)
{
    [Fact]
    public async Task Budget_adds_cash_banks_debtors_and_subtracts_creditors_and_undelivered_transfers()
    {
        var ctx = await ArrangeAsync();
        var before = await ctx.BudgetAsync(ctx.SupervisorToken, ctx.Dkr);

        var cash = await ctx.AccountAsync(ctx.Dkr, "cash");
        var wallet = await ctx.AccountAsync(ctx.Dkr, "wallet");
        var debtor = await ctx.PartyAsync($"Débiteur {ctx.Tag}"[..20], inBalanceSheet: true);
        var creditor = await ctx.PartyAsync($"Créditeur {ctx.Tag}"[..20], inBalanceSheet: true);
        var outside = await ctx.PartyAsync($"Hors bilan {ctx.Tag}"[..20], inBalanceSheet: false);

        await ctx.MovementAsync("OwnerContribution", cash, 100_000m, party: null);
        await ctx.MovementAsync("OwnerContribution", wallet, 40_000m, party: null);
        await ctx.MovementAsync("Payment", cash, 30_000m, debtor);      // il nous doit 30 000
        await ctx.MovementAsync("Receipt", wallet, 20_000m, creditor);  // on lui doit 20 000
        await ctx.MovementAsync("Payment", cash, 5_000m, outside);      // hors bilan : ignoré
        await ctx.UndeliveredTransferToDakarAsync(50_000m);

        var after = await ctx.BudgetAsync(ctx.SupervisorToken, ctx.Dkr);

        Assert.Equal("XOF", after.GetProperty("currency").GetString());
        Assert.Equal(65_000m, Line(after, "cash") - Line(before, "cash"));
        Assert.Equal(60_000m, Line(after, "banks") - Line(before, "banks"));
        Assert.Equal(30_000m, Line(after, "debtors") - Line(before, "debtors"));
        Assert.Equal(20_000m, Line(after, "creditors") - Line(before, "creditors"));
        Assert.Equal(50_000m, after.GetProperty("undelivered").GetDecimal() - before.GetProperty("undelivered").GetDecimal());
        Assert.Equal(85_000m, after.GetProperty("budget").GetDecimal() - before.GetProperty("budget").GetDecimal());

        // Chaque ligne s'ouvre sur son détail.
        Assert.Equal(65_000m, Item(after, "cash", cash));
        Assert.Equal(60_000m, Item(after, "banks", wallet));
        Assert.Equal(30_000m, Item(after, "debtors", debtor));
        Assert.Equal(20_000m, Item(after, "creditors", creditor));
        Assert.DoesNotContain(after.GetProperty("debtors").GetProperty("items").EnumerateArray(),
            i => i.GetProperty("id").GetGuid() == outside);
    }

    [Fact]
    public async Task The_cashier_saves_the_budget_of_the_day_and_saving_again_replaces_it()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.AccountAsync(ctx.Dkr, "cash");
        await ctx.MovementAsync("OwnerContribution", cash, 10_000m, party: null);
        var cashier = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Cashier, agencyCode: "DKR");
        var (cashierToken, _) = await ctx.Client.LoginOkAsync(cashier.Email, cashier.Password);

        var first = await ctx.SaveAsync(cashierToken, agency: null);
        var live = await ctx.BudgetAsync(cashierToken, agency: null);
        Assert.Equal(live.GetProperty("budget").GetDecimal(), first.GetProperty("budget").GetDecimal());
        Assert.Equal(cashier.Id, first.GetProperty("savedBy").GetGuid());
        Assert.Equal(ctx.Today, first.GetProperty("date").GetString());

        await ctx.MovementAsync("OwnerContribution", cash, 5_000m, party: null);
        var second = await ctx.SaveAsync(ctx.SupervisorToken, ctx.Dkr);

        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(5_000m, second.GetProperty("budget").GetDecimal() - first.GetProperty("budget").GetDecimal());

        using var history = await ctx.Client.SendGetAsync(cashierToken, "/api/v1/cash-budget/snapshots");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var today = (await history.ReadJsonAsync()).EnumerateArray().Where(s => s.GetProperty("date").GetString() == ctx.Today).ToList();
        var saved = Assert.Single(today);
        Assert.Equal(second.GetProperty("budget").GetDecimal(), saved.GetProperty("budget").GetDecimal());
    }

    [Fact]
    public async Task Rights_and_agency_scope_are_applied()
    {
        var ctx = await ArrangeAsync();

        using var otherAgency = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, $"/api/v1/cash-budget?agencyId={ctx.Dkr}");
        using var ownAgency = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, "/api/v1/cash-budget");
        using var agentSaves = await ctx.Client.PostJsonAsync(ctx.NkcAgentToken, "/api/v1/cash-budget/snapshots", new { });
        using var supervisorWithoutAgency = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/cash-budget");
        using var badPeriod = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, $"/api/v1/cash-budget/snapshots?agencyId={ctx.Dkr}&from=2026-02-02&to=2026-02-01");

        Assert.Equal(HttpStatusCode.Forbidden, otherAgency.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownAgency.StatusCode);
        Assert.Equal("MRO", (await ownAgency.ReadJsonAsync()).GetProperty("currency").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, agentSaves.StatusCode);
        Assert.Equal("Cash.Movement.AgencyRequired", await supervisorWithoutAgency.ReadErrorCodeAsync());
        Assert.Equal("Cash.Filter.PeriodInvalid", await badPeriod.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static decimal Line(JsonElement budget, string line) => budget.GetProperty(line).GetProperty("amount").GetDecimal();

    private static decimal Item(JsonElement budget, string line, Guid id) =>
        budget.GetProperty(line).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == id).GetProperty("balance").GetDecimal();

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr, $"b{Guid.NewGuid():N}"[..12],
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Nkc,
        Guid Dkr,
        string Tag,
        DateTimeOffset OperationAt,
        string Today)
    {
        public async Task<JsonElement> BudgetAsync(string token, Guid? agency)
        {
            using var response = await Client.SendGetAsync(
                token, agency is { } id ? $"/api/v1/cash-budget?agencyId={id}" : "/api/v1/cash-budget");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<JsonElement> SaveAsync(string token, Guid? agency)
        {
            using var response = await Client.PostJsonAsync(token, "/api/v1/cash-budget/snapshots", new { agencyId = agency });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<Guid> AccountAsync(Guid agencyId, string type)
        {
            using var response = await Client.CreateTreasuryAccountAsync(
                SupervisorToken, new { name = $"Compte {Guid.NewGuid():N}"[..30], type, agencyId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<Guid> PartyAsync(string name, bool inBalanceSheet)
        {
            using var created = await Client.CreatePartyAsync(SupervisorToken, new { name, type = "Client" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
            if (!inBalanceSheet)
            {
                using var update = await Client.UpdatePartyAsync(SupervisorToken, id, new { name, type = "Client", inBalanceSheet = false });
                Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
            }

            return id;
        }

        /// <summary>Mouvement de caisse saisi par l'agent de Dakar.</summary>
        public async Task MovementAsync(string type, Guid account, decimal amount, Guid? party)
        {
            using var response = await Client.CreateCashMovementAsync(
                DkrAgentToken,
                new { type, treasuryAccountId = account, partyId = party, amount, note = $"Mouvement {Tag}", operationAt = OperationAt },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        /// <summary>Transfert Nouakchott → Dakar non livré : Dakar doit encore payer ce montant en FCFA.</summary>
        public async Task UndeliveredTransferToDakarAsync(decimal amountXof)
        {
            var day = DateOnly.FromDateTime(OperationAt.UtcDateTime);
            using var rate = await Client.SetDailyRateAsync(SupervisorToken, day, 3600m, 3650m);
            var nkcCash = await AccountAsync(Nkc, "cash");
            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = nkcCash, amount = amountXof * 3650m / 5000m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
    }
}
