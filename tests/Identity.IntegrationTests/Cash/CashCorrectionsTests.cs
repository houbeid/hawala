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
/// Corrections du Superviseur (règles validées) : annuler un mouvement (contre-passation, motif obligatoire) ;
/// ajuster le solde d'un compte de trésorerie ou d'un tiers (différence signée, motif, contre « Écarts et ajustements »).
/// Chaque test crée ses comptes et son tiers, sur une date tirée au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CashCorrectionsTests(ApiFixture fixture)
{
    // --- Annuler un mouvement ----------------------------------------------

    [Fact]
    public async Task Cancelling_a_receipt_reverses_its_entry_and_leaves_the_totals()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.MovementAsync("Receipt", 12_000m, ctx.Party);
        var kept = await ctx.MovementAsync("OwnerContribution", 5_000m, party: null);
        Assert.Equal(17_000m, await ctx.TreasuryBalanceAsync());
        Assert.Equal(-12_000m, await ctx.PartyBalanceAsync());

        using var response = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, id, "Saisie en double");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(5_000m, await ctx.TreasuryBalanceAsync());
        Assert.Equal(0m, await ctx.PartyBalanceAsync());

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var original = await ledger.JournalEntries.SingleAsync(e => e.SourceId == id && e.ReversesEntryId == null);
        var reversal = await ledger.JournalEntries.SingleAsync(e => e.ReversesEntryId == original.Id);
        Assert.Equal("Saisie en double", reversal.Reason);

        var list = await ctx.ListAsync();
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(5_000m, list.GetProperty("incoming").GetProperty("amount").GetDecimal());
        var cancelled = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == id);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal("Saisie en double", cancelled.GetProperty("cancellationReason").GetString());
        Assert.NotEqual(kept, id);
    }

    [Fact]
    public async Task Only_the_supervisor_cancels_once_with_a_reason()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.MovementAsync("OwnerContribution", 5_000m, party: null);

        using var byAgent = await ctx.Client.CancelCashMovementAsync(ctx.AgentToken, id, "Motif");
        using var noReason = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, id, " ");
        using var first = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, id, "Motif");
        using var again = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, id, "Motif");
        using var unknown = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, Guid.NewGuid(), "Motif");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Cash.Movement.ReasonRequired", await noReason.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Cash.Movement.AlreadyCancelled", await again.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync());
    }

    // --- Ajuster un solde -------------------------------------------------

    [Fact]
    public async Task Adjusting_a_cash_account_up_and_down_moves_only_the_difference()
    {
        var ctx = await ArrangeAsync();

        using var up = await ctx.AdjustAsync("TreasuryAccount", ctx.Cash, 1_500.50m, "Billets retrouvés");
        using var down = await ctx.AdjustAsync("TreasuryAccount", ctx.Cash, -500m, "Écart constaté");

        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        Assert.Equal(HttpStatusCode.Created, down.StatusCode);
        Assert.Equal(1_000.50m, await ctx.TreasuryBalanceAsync());

        var id = (await up.ReadJsonAsync()).GetProperty("id").GetGuid();
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == id);
        Assert.Equal("Cash.Adjustment", entry.EntryType);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public async Task Adjusting_a_party_changes_its_balance_in_the_agency()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.AdjustAsync("Party", ctx.Party, 2_000m, "Dette oubliée");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2_000m, await ctx.PartyBalanceAsync());

        using var list = await ctx.Client.SendGetAsync(
            ctx.AgentToken, $"/api/v1/balance-adjustments?from={ctx.DayText}&to={ctx.DayText}");
        var line = (await list.ReadJsonAsync()).EnumerateArray().Single(a => a.GetProperty("targetId").GetGuid() == ctx.Party);
        Assert.Equal("Party", line.GetProperty("target").GetString());
        Assert.Equal(ctx.PartyName, line.GetProperty("targetName").GetString());
        Assert.Equal(2_000m, line.GetProperty("amount").GetDecimal());
        Assert.Equal("Dette oubliée", line.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Bad_adjustments_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.CreateBalanceAdjustmentAsync(ctx.AgentToken, new
        {
            agencyId = ctx.Nkc, target = "TreasuryAccount", targetId = ctx.Cash, amount = 100m, reason = "Motif",
        });
        using var noReason = await ctx.AdjustAsync("TreasuryAccount", ctx.Cash, 100m, reason: null);
        using var zero = await ctx.AdjustAsync("TreasuryAccount", ctx.Cash, 0m, "Motif");
        using var otherAgency = await ctx.AdjustAsync("TreasuryAccount", ctx.DakarCash, 100m, "Motif");
        using var badTarget = await ctx.AdjustAsync("Bank", ctx.Cash, 100m, "Motif");
        using var unknownParty = await ctx.AdjustAsync("Party", Guid.NewGuid(), 100m, "Motif");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Cash.Adjustment.ReasonRequired", await noReason.ReadErrorCodeAsync());
        Assert.Equal("Cash.Adjustment.AmountInvalid", await zero.ReadErrorCodeAsync());
        Assert.Equal("Cash.Adjustment.TargetNotFound", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Cash.Adjustment.TargetInvalid", await badTarget.ReadErrorCodeAsync());
        Assert.Equal("Cash.Adjustment.TargetNotFound", await unknownParty.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync());
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

        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        var tag = $"r{Guid.NewGuid():N}"[..12];
        var partyName = $"Tiers {tag}";
        using var party = await client.CreatePartyAsync(token, new { name = partyName, type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        return new Context(
            factory, client, token, agentToken, nkc, tag,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            await CreateCashAsync(client, token, nkc), await CreateCashAsync(client, token, dkr),
            partyId, partyName);
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string AgentToken,
        Guid Nkc,
        string Tag,
        DateTimeOffset OperationAt,
        Guid Cash,
        Guid DakarCash,
        Guid Party,
        string PartyName)
    {
        public string DayText => OperationAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public async Task<Guid> MovementAsync(string type, decimal amount, Guid? party)
        {
            using var response = await Client.CreateCashMovementAsync(
                AgentToken,
                new { type, treasuryAccountId = Cash, partyId = party, amount, note = $"Mouvement {Tag}", operationAt = OperationAt },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public Task<HttpResponseMessage> AdjustAsync(string target, Guid targetId, decimal amount, string? reason) =>
            Client.CreateBalanceAdjustmentAsync(SupervisorToken, new
            {
                agencyId = Nkc, target, targetId, amount, reason, operationAt = OperationAt,
            });

        public async Task<JsonElement> ListAsync()
        {
            using var response = await Client.SendGetAsync(
                AgentToken, $"/api/v1/cash-movements?from={DayText}&to={DayText}&search={Tag}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<decimal> TreasuryBalanceAsync()
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, Cash);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }

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
