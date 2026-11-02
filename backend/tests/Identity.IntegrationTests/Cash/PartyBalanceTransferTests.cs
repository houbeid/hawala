using System.Net;
using Cash.Application.Features.PartyTransfers;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parties.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Cash;

/// <summary>
/// Transfert de solde entre tiers (décisions du patron, 2 novembre 2026) : même agence, Superviseur et Agent, sans motif,
/// plafond du tiers débité bloquant ; les caisses ne bougent pas. Chaque test crée ses tiers, sur une date tirée au hasard
/// (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PartyBalanceTransferTests(ApiFixture fixture)
{
    [Fact]
    public async Task Agent_moves_a_balance_from_one_party_to_another_and_a_replay_records_nothing()
    {
        var ctx = await ArrangeAsync();
        var key = Guid.NewGuid().ToString("N");

        using var response = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyB, 400_000m, key: key);
        using var replay = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyB, 400_000m, key: key);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        Assert.Equal(400_000m, created.GetProperty("amount").GetDecimal());
        Assert.Equal("MRO", created.GetProperty("currency").GetString());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.ReadJsonAsync()).GetProperty("replayed").GetBoolean());

        // A est débité (il nous doit davantage), B crédité (on lui doit davantage) ; une seule écriture.
        Assert.Equal(400_000m, await ctx.PartyBalanceAsync(ctx.PartyA, ctx.Nkc));
        Assert.Equal(-400_000m, await ctx.PartyBalanceAsync(ctx.PartyB, ctx.Nkc));

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var id = created.GetProperty("id").GetGuid();
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == id);
        Assert.Equal(CreatePartyBalanceTransferCommandHandler.EntryType, entry.EntryType);
        Assert.Equal(DateOnly.FromDateTime(ctx.OperationAt.UtcDateTime), entry.OperationDate);
        Assert.Equal($"Transfert de solde : A {ctx.Tag} → B {ctx.Tag}", entry.Label);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public async Task The_credit_limit_of_the_debited_party_blocks()
    {
        var ctx = await ArrangeAsync();
        using var limit = await ctx.Client.SetCreditLimitAsync(ctx.SupervisorToken, ctx.PartyA, ctx.Nkc, 100_000m);
        Assert.Equal(HttpStatusCode.NoContent, limit.StatusCode);

        using var tooMuch = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyB, 100_000.01m);
        using var withinLimit = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyB, 100_000m);

        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Equal("Cash.PartyTransfer.LimitExceeded", await tooMuch.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, withinLimit.StatusCode);
        Assert.Equal(100_000m, await ctx.PartyBalanceAsync(ctx.PartyA, ctx.Nkc));
    }

    [Fact]
    public async Task Supervisor_chooses_the_agency_and_may_enter_mru()
    {
        var ctx = await ArrangeAsync();

        using var withoutAgency = await ctx.TransferAsync(ctx.SupervisorToken, ctx.PartyA, ctx.PartyB, 1_000m);
        using var inDakar = await ctx.TransferAsync(ctx.SupervisorToken, ctx.PartyA, ctx.PartyB, 5_000m, agency: ctx.Dkr);
        using var inMru = await ctx.TransferAsync(ctx.SupervisorToken, ctx.PartyA, ctx.PartyB, 12.5m, agency: ctx.Nkc, unit: "MRU");

        Assert.Equal("Cash.Movement.AgencyRequired", await withoutAgency.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, inDakar.StatusCode);
        Assert.Equal(HttpStatusCode.Created, inMru.StatusCode);
        Assert.Equal(5_000m, await ctx.PartyBalanceAsync(ctx.PartyA, ctx.Dkr));
        Assert.Equal(125m, await ctx.PartyBalanceAsync(ctx.PartyA, ctx.Nkc));
    }

    [Fact]
    public async Task Refusals_leave_both_balances_untouched()
    {
        var ctx = await ArrangeAsync();
        var cashier = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Cashier, agencyCode: "NKC");
        var (cashierToken, _) = await ctx.Client.LoginOkAsync(cashier.Email, cashier.Password);
        using var archivedParty = await ctx.Client.CreatePartyAsync(ctx.SupervisorToken, new { name = $"C {ctx.Tag}", type = "Client" });
        var archived = (await archivedParty.ReadJsonAsync()).GetProperty("id").GetGuid();
        using var archive = await ctx.Client.ArchivePartyAsync(ctx.SupervisorToken, archived);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        using var byCashier = await ctx.TransferAsync(cashierToken, ctx.PartyA, ctx.PartyB, 1_000m);
        using var otherAgency = await ctx.TransferAsync(ctx.DkrAgentToken, ctx.PartyA, ctx.PartyB, 1_000m, agency: ctx.Nkc);
        using var sameParty = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyA, 1_000m);
        using var toArchived = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, archived, 1_000m);
        using var unknown = await ctx.TransferAsync(ctx.NkcAgentToken, Guid.NewGuid(), ctx.PartyB, 1_000m);
        using var tooPrecise = await ctx.TransferAsync(ctx.NkcAgentToken, ctx.PartyA, ctx.PartyB, 10.001m);
        using var withoutKey = await ctx.Client.CreatePartyTransferAsync(
            ctx.NkcAgentToken, new { fromPartyId = ctx.PartyA, toPartyId = ctx.PartyB, amount = 1_000m }, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.Forbidden, byCashier.StatusCode);
        Assert.Equal("Cash.Movement.AgencyNotAllowed", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Cash.PartyTransfer.SameParty", await sameParty.ReadErrorCodeAsync());
        Assert.Equal("Cash.PartyTransfer.PartyArchived", await toArchived.ReadErrorCodeAsync());
        Assert.Equal("Cash.PartyTransfer.PartyNotFound", await unknown.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, tooPrecise.StatusCode);
        Assert.Equal("Cash.Movement.IdempotencyKeyRequired", await withoutKey.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.PartyBalanceAsync(ctx.PartyA, ctx.Nkc));
        Assert.Equal(0m, await ctx.PartyBalanceAsync(ctx.PartyB, ctx.Nkc));
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

        // Lettres seulement : la recherche des tiers compare aussi les chiffres aux téléphones.
        var tag = new string([.. Enumerable.Range(0, 10).Select(_ => (char)('a' + Random.Shared.Next(26)))]);
        var partyA = await CreatePartyAsync(client, token, $"A {tag}");
        var partyB = await CreatePartyAsync(client, token, $"B {tag}");

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr, tag,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), partyA, partyB);
    }

    private static async Task<Guid> CreatePartyAsync(HttpClient client, string token, string name)
    {
        using var response = await client.CreatePartyAsync(token, new { name, type = "Client" });
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
        Guid PartyA,
        Guid PartyB)
    {
        public Task<HttpResponseMessage> TransferAsync(
            string token, Guid from, Guid to, decimal amount, Guid? agency = null, string? unit = null, string? key = null) =>
            Client.CreatePartyTransferAsync(
                token,
                new { agencyId = agency, fromPartyId = from, toPartyId = to, amount, unit, operationAt = OperationAt },
                key ?? Guid.NewGuid().ToString("N"));

        /// <summary>Solde du tiers dans une agence, lu au grand livre (positif = rouge).</summary>
        public async Task<decimal> PartyBalanceAsync(Guid partyId, Guid agencyId)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
                .Parties.AsNoTracking().SingleAsync(p => p.Id == partyId);
            var ledgerAccount = party.AgencyAccounts.Single(a => a.AgencyId == agencyId).LedgerAccountId;
            var balances = await scope.ServiceProvider.GetRequiredService<ILedger>()
                .GetBalancesAsync([ledgerAccount], asOf: null, CancellationToken.None);
            return balances.Value.Single().Balance.Amount;
        }
    }
}
