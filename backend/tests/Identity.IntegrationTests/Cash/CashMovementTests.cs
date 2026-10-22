using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parties.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Cash;

/// <summary>
/// Mouvements manuels de caisse (validés le 21 octobre 2026, écran « Caisse - Ajout ») : encaissement et décaissement avec un tiers,
/// approvisionnement et retrait du patron en montant seul ; écriture au grand livre dans la même transaction.
/// La base est partagée : chaque test crée ses comptes et son tiers, sur une date tirée au hasard (1975-1995),
/// et marque ses notes d'une étiquette unique.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CashMovementTests(ApiFixture fixture)
{
    // --- Les quatre types -------------------------------------------------

    [Fact]
    public async Task Receipt_from_a_party_enters_the_cash_and_credits_the_party()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.CreateAsync(ctx.NkcAgentToken, "Receipt", 12_500.50m, ctx.Party);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/cash-movements/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Receipt", created.GetProperty("type").GetString());
        Assert.Equal(12_500.50m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));

        // Le tiers nous a payé : son solde baisse (vert = on lui doit).
        Assert.Equal(-12_500.50m, await ctx.PartyBalanceAsync());

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == id);
        Assert.Equal("Cash.Receipt", entry.EntryType);
        Assert.Equal(DateOnly.FromDateTime(ctx.OperationAt.UtcDateTime), entry.OperationDate);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public async Task Payment_to_a_party_leaves_the_cash_and_debits_the_party()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.CreateAsync(ctx.NkcAgentToken, "payment", 8_000m, ctx.Party);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(-8_000m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
        Assert.Equal(8_000m, await ctx.PartyBalanceAsync());
    }

    [Fact]
    public async Task Owner_contribution_and_withdrawal_move_only_the_cash()
    {
        var ctx = await ArrangeAsync();

        using var contribution = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 100_000m, party: null);
        using var withdrawal = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerWithdrawal", 30_000m, party: null);

        Assert.Equal(HttpStatusCode.Created, contribution.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withdrawal.StatusCode);
        Assert.Equal(70_000m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
    }

    [Fact]
    public async Task An_amount_in_mru_is_stored_in_mro()
    {
        var ctx = await ArrangeAsync();

        using var mru = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 1_250.5m, party: null, unit: "MRU");
        using var mruInDakar = await ctx.CreateAsync(ctx.DkrAgentToken, "OwnerContribution", 1_000m, party: null, unit: "MRU", cash: ctx.DkrCash);
        using var tooPrecise = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 1.2345m, party: null, unit: "MRU");

        Assert.Equal(HttpStatusCode.Created, mru.StatusCode);
        Assert.Equal(12_505m, (await mru.ReadJsonAsync()).GetProperty("amount").GetDecimal());
        Assert.Equal("Cash.Movement.UnitInvalid", await mruInDakar.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
    }

    // --- Idempotence -------------------------------------------------------

    [Fact]
    public async Task Same_idempotency_key_records_the_movement_only_once()
    {
        var ctx = await ArrangeAsync();
        var key = Guid.NewGuid().ToString("N");

        using var first = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 5_000m, party: null, key: key);
        using var again = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 5_000m, party: null, key: key);
        using var missing = await ctx.Client.CreateCashMovementAsync(
            ctx.NkcAgentToken, new { type = "OwnerContribution", treasuryAccountId = ctx.NkcCash, amount = 5_000m }, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.True((await again.ReadJsonAsync()).GetProperty("replayed").GetBoolean());
        Assert.Equal("Cash.Movement.IdempotencyKeyRequired", await missing.ReadErrorCodeAsync());
        Assert.Equal(5_000m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Party_rules_are_applied()
    {
        var ctx = await ArrangeAsync();
        using var archivedParty = await ctx.Client.CreatePartyAsync(
            ctx.SupervisorToken, new { name = $"Archivé {ctx.Tag}"[..20], type = "Client" });
        var archived = (await archivedParty.ReadJsonAsync()).GetProperty("id").GetGuid();
        using var archive = await ctx.Client.ArchivePartyAsync(ctx.SupervisorToken, archived);

        using var noParty = await ctx.CreateAsync(ctx.NkcAgentToken, "Receipt", 1_000m, party: null);
        using var partyForOwner = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerWithdrawal", 1_000m, ctx.Party);
        using var unknown = await ctx.CreateAsync(ctx.NkcAgentToken, "Payment", 1_000m, Guid.NewGuid());
        using var onArchived = await ctx.CreateAsync(ctx.NkcAgentToken, "Payment", 1_000m, archived);

        Assert.Equal("Cash.Movement.PartyRequired", await noParty.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.PartyNotAllowed", await partyForOwner.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.PartyNotFound", await unknown.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.PartyArchived", await onArchived.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
    }

    [Fact]
    public async Task An_agent_works_only_on_its_agency_and_read_only_cannot_record()
    {
        var ctx = await ArrangeAsync();
        var readOnly = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.ReadOnly, agencyCode: "NKC");
        var (readOnlyToken, _) = await ctx.Client.LoginOkAsync(readOnly.Email, readOnly.Password);

        using var otherAgency = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 1_000m, party: null, agency: ctx.Dkr);
        using var otherCash = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 1_000m, party: null, cash: ctx.DkrCash);
        using var supervisorWithoutAgency = await ctx.CreateAsync(ctx.SupervisorToken, "OwnerContribution", 1_000m, party: null);
        using var byReadOnly = await ctx.CreateAsync(readOnlyToken, "OwnerContribution", 1_000m, party: null);
        using var badType = await ctx.CreateAsync(ctx.NkcAgentToken, "Transfer", 1_000m, party: null);

        Assert.Equal(HttpStatusCode.Forbidden, otherAgency.StatusCode);
        Assert.Equal("Cash.Movement.AgencyNotAllowed", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.AccountNotAllowed", await otherCash.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.AgencyRequired", await supervisorWithoutAgency.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byReadOnly.StatusCode);
        Assert.Equal("Cash.Movement.TypeInvalid", await badType.ReadErrorCodeAsync());
    }

    // --- Liste -------------------------------------------------------------

    [Fact]
    public async Task List_gives_the_movements_of_the_period_with_incoming_outgoing_and_net()
    {
        var ctx = await ArrangeAsync();
        using var a = await ctx.CreateAsync(ctx.NkcAgentToken, "Receipt", 10_000m, ctx.Party);
        using var b = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 5_000m, party: null);
        using var c = await ctx.CreateAsync(ctx.NkcAgentToken, "Payment", 3_000.25m, ctx.Party);

        var list = await ctx.ListAsync(ctx.NkcAgentToken, $"search={ctx.Tag}");

        Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(15_000m, list.GetProperty("incoming").GetProperty("amount").GetDecimal());
        Assert.Equal(2, list.GetProperty("incoming").GetProperty("count").GetInt32());
        Assert.Equal(3_000.25m, list.GetProperty("outgoing").GetProperty("amount").GetDecimal());
        Assert.Equal(11_999.75m, list.GetProperty("net").GetDecimal());

        var first = list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("type").GetString() == "Receipt");
        Assert.True(first.GetProperty("incoming").GetBoolean());
        Assert.Equal(ctx.PartyName, first.GetProperty("partyName").GetString());
        Assert.Equal(ctx.CashName, first.GetProperty("treasuryAccountName").GetString());

        var payments = await ctx.ListAsync(ctx.NkcAgentToken, $"search={ctx.Tag}&type=Payment");
        Assert.Equal(1, payments.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Dakar_does_not_see_the_movements_of_nouakchott()
    {
        var ctx = await ArrangeAsync();
        using var created = await ctx.CreateAsync(ctx.NkcAgentToken, "OwnerContribution", 5_000m, party: null);
        var id = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        var fromDakar = await ctx.ListAsync(ctx.DkrAgentToken, $"search={ctx.Tag}");
        using var oneFromDakar = await ctx.Client.SendGetAsync(ctx.DkrAgentToken, $"/api/v1/cash-movements/{id}");
        using var askNkc = await ctx.Client.SendGetAsync(ctx.DkrAgentToken, $"/api/v1/cash-movements?agencyId={ctx.Nkc}");
        using var oneFromSupervisor = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/cash-movements/{id}");

        Assert.Equal(0, fromDakar.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, oneFromDakar.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, askNkc.StatusCode);
        Assert.Equal(HttpStatusCode.OK, oneFromSupervisor.StatusCode);
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

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        var tag = $"k{Guid.NewGuid():N}"[..12];
        var cashName = $"Caisse {tag}";
        var nkcCash = await CreateCashAsync(client, token, nkc, cashName);
        var dkrCash = await CreateCashAsync(client, token, dkr, $"Caisse D {tag}");

        var partyName = $"Tiers {tag}";
        using var party = await client.CreatePartyAsync(token, new { name = partyName, type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr, tag,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            nkcCash, cashName, dkrCash, partyId, partyName);
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId, string name)
    {
        using var response = await client.CreateTreasuryAccountAsync(token, new { name, type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
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
        Guid NkcCash,
        string CashName,
        Guid DkrCash,
        Guid Party,
        string PartyName)
    {
        private string DayText => OperationAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public Task<HttpResponseMessage> CreateAsync(
            string token,
            string type,
            decimal amount,
            Guid? party,
            string? unit = null,
            Guid? cash = null,
            Guid? agency = null,
            string? key = null) =>
            Client.CreateCashMovementAsync(
                token,
                new
                {
                    agencyId = agency,
                    type,
                    treasuryAccountId = cash ?? NkcCash,
                    partyId = party,
                    amount,
                    unit,
                    note = $"Mouvement {Tag}",
                    operationAt = OperationAt,
                },
                key ?? Guid.NewGuid().ToString("N"));

        public async Task<JsonElement> ListAsync(string token, string query)
        {
            using var response = await Client.SendGetAsync(token, $"/api/v1/cash-movements?from={DayText}&to={DayText}&{query}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<decimal> TreasuryBalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }

        /// <summary>Solde du tiers à Nouakchott, lu au grand livre (positif = rouge).</summary>
        public async Task<decimal> PartyBalanceAsync()
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
                .Parties.AsNoTracking().SingleAsync(p => p.Id == Party);
            var ledgerAccount = party.AgencyAccounts.Single(a => a.AgencyId == Nkc).LedgerAccountId;
            var balances = await scope.ServiceProvider.GetRequiredService<ILedger>()
                .GetBalancesAsync([ledgerAccount], asOf: null, CancellationToken.None);
            return balances.Value.Single().Balance.Amount;
        }
    }
}
