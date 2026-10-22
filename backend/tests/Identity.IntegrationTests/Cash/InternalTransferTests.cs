using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Cash;

/// <summary>
/// Virement interne (règle validée : deux lignes) : d'un compte de trésorerie vers un autre de la même agence, sans tiers ;
/// ni entrée ni sortie pour l'agence. Chaque test crée ses comptes, sur une date tirée au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class InternalTransferTests(ApiFixture fixture)
{
    [Fact]
    public async Task Moving_money_from_the_cash_to_a_wallet_writes_two_lines()
    {
        var ctx = await ArrangeAsync();
        using var fund = await ctx.CreateAsync(new { type = "OwnerContribution", treasuryAccountId = ctx.Cash, amount = 80_000m });

        using var response = await ctx.CreateAsync(new
        {
            type = "InternalTransfer", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.Wallet, amount = 50_000m,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(30_000m, await ctx.BalanceAsync(ctx.Cash));
        Assert.Equal(50_000m, await ctx.BalanceAsync(ctx.Wallet));

        var id = (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.SourceId == id);
        Assert.Equal("Cash.InternalTransfer", entry.EntryType);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Single(entry.Lines, l => l.Side == EntrySide.Debit && l.Amount == 50_000m);
        Assert.Single(entry.Lines, l => l.Side == EntrySide.Credit && l.Amount == 50_000m);
    }

    [Fact]
    public async Task A_transfer_is_neither_incoming_nor_outgoing_in_the_list()
    {
        var ctx = await ArrangeAsync();
        using var fund = await ctx.CreateAsync(new { type = "OwnerContribution", treasuryAccountId = ctx.Cash, amount = 80_000m });
        using var move = await ctx.CreateAsync(new
        {
            type = "InternalTransfer", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.Wallet, amount = 50_000m,
        });

        using var response = await ctx.Client.SendGetAsync(
            ctx.AgentToken, $"/api/v1/cash-movements?from={ctx.DayText}&to={ctx.DayText}&search={ctx.Tag}");
        var list = await response.ReadJsonAsync();

        Assert.Equal(80_000m, list.GetProperty("incoming").GetProperty("amount").GetDecimal());
        Assert.Equal(0m, list.GetProperty("outgoing").GetProperty("amount").GetDecimal());
        Assert.Equal(80_000m, list.GetProperty("net").GetDecimal());
        Assert.Equal(50_000m, list.GetProperty("internalTransfers").GetProperty("amount").GetDecimal());
        Assert.Equal(1, list.GetProperty("internalTransfers").GetProperty("count").GetInt32());

        var line = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("isInternal").GetBoolean());
        Assert.False(line.GetProperty("incoming").GetBoolean());
        Assert.Equal(ctx.WalletName, line.GetProperty("destinationTreasuryAccountName").GetString());

        // Le filtre par compte trouve le virement par sa destination.
        using var byWallet = await ctx.Client.SendGetAsync(
            ctx.AgentToken, $"/api/v1/cash-movements?from={ctx.DayText}&to={ctx.DayText}&treasuryAccountId={ctx.Wallet}");
        Assert.Equal(1, (await byWallet.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Bad_transfers_are_refused()
    {
        var ctx = await ArrangeAsync();

        using var noDestination = await ctx.CreateAsync(new { type = "InternalTransfer", treasuryAccountId = ctx.Cash, amount = 1_000m });
        using var same = await ctx.CreateAsync(new
        {
            type = "InternalTransfer", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.Cash, amount = 1_000m,
        });
        using var otherAgency = await ctx.CreateAsync(new
        {
            type = "InternalTransfer", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.DakarCash, amount = 1_000m,
        });
        using var withParty = await ctx.CreateAsync(new
        {
            type = "InternalTransfer", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.Wallet,
            partyId = Guid.NewGuid(), amount = 1_000m,
        });
        using var destinationOnReceipt = await ctx.CreateAsync(new
        {
            type = "OwnerContribution", treasuryAccountId = ctx.Cash, destinationTreasuryAccountId = ctx.Wallet, amount = 1_000m,
        });

        Assert.Equal("Cash.Movement.DestinationRequired", await noDestination.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.SameAccount", await same.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.AccountNotAllowed", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.PartyNotAllowed", await withParty.ReadErrorCodeAsync());
        Assert.Equal("Cash.Movement.DestinationNotAllowed", await destinationOnReceipt.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.BalanceAsync(ctx.Cash));
        Assert.Equal(0m, await ctx.BalanceAsync(ctx.Wallet));
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

        var tag = $"v{Guid.NewGuid():N}"[..12];
        var walletName = $"Bankily {tag}";
        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        return new Context(
            factory, client, token, agentToken, tag,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            await CreateAccountAsync(client, token, nkc, $"Caisse {tag}", "cash"),
            await CreateAccountAsync(client, token, nkc, walletName, "wallet"),
            walletName,
            await CreateAccountAsync(client, token, dkr, $"Caisse D {tag}", "cash"));
    }

    private static async Task<Guid> CreateAccountAsync(HttpClient client, string token, Guid agencyId, string name, string type)
    {
        using var response = await client.CreateTreasuryAccountAsync(token, new { name, type, agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string AgentToken,
        string Tag,
        DateTimeOffset OperationAt,
        Guid Cash,
        Guid Wallet,
        string WalletName,
        Guid DakarCash)
    {
        public string DayText => OperationAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Ajoute la note marquée et la date du test à un corps de mouvement.</summary>
        public Task<HttpResponseMessage> CreateAsync(object body)
        {
            var fields = JsonSerializer.SerializeToElement(body).EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value);
            fields["note"] = $"Mouvement {Tag}";
            fields["operationAt"] = OperationAt;
            return Client.CreateCashMovementAsync(AgentToken, fields, Guid.NewGuid().ToString("N"));
        }

        public async Task<decimal> BalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }
    }
}
