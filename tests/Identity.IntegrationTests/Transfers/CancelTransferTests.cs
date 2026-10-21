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
using Transfers.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Annulation (règles validées) : Superviseur seulement, non livré seulement, motif obligatoire,
/// contre-passation de l'écriture de création (remboursement en caisse, dette du crédit effacée).
/// Chaque test fixe le taux d'une date tirée au hasard entre 1975 et 1995 (vente 3 650, achat 3 600)
/// et marque ses transferts d'une étiquette unique dans l'expéditeur.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CancelTransferTests(ApiFixture fixture)
{
    [Fact]
    public async Task Supervisor_cancels_a_transfer_the_cash_is_refunded_and_the_creation_entry_reversed()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync(paid: 36_500m);
        Assert.Equal(36_500m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));

        using var response = await ctx.Client.CancelTransferAsync(
            ctx.SupervisorToken, id, "Client a renoncé", ctx.OperationAt.AddHours(2));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));

        var view = await ctx.GetAsync(ctx.DkrAgentToken, id);
        Assert.Equal("Cancelled", view.GetProperty("status").GetString());
        Assert.Equal("Client a renoncé", view.GetProperty("cancellationReason").GetString());
        Assert.Equal(ctx.OperationAt.AddHours(2), view.GetProperty("cancellationOperationAt").GetDateTimeOffset());

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var transfer = await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        var reversal = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.SingleAsync(e => e.Id == transfer.CancellationEntryId);
        Assert.Equal(transfer.CreationEntryId, reversal.ReversesEntryId);
        Assert.Equal("Client a renoncé", reversal.Reason);
        Assert.Equal(DateOnly.FromDateTime(ctx.OperationAt.UtcDateTime), reversal.OperationDate);

        // Annulé = verrouillé : on ne le livre plus.
        using var deliver = await ctx.Client.DeliverTransferAsync(ctx.DkrAgentToken, id, new
        {
            code,
            operationAt = ctx.OperationAt.AddHours(3),
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } },
        });
        Assert.Equal(HttpStatusCode.Conflict, deliver.StatusCode);
        Assert.Equal("Transfers.Delivery.Cancelled", await deliver.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Cancelling_a_credit_transfer_erases_the_debt_of_the_party()
    {
        var ctx = await ArrangeAsync();
        using var party = await ctx.Client.CreatePartyAsync(
            ctx.SupervisorToken, new { name = $"Débiteur {ctx.Tag}", type = "Client" });
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();
        var (id, _) = await ctx.CreateAsync(paid: 20_000m, creditPartyId: partyId);
        Assert.Equal(16_500m, await ctx.PartyBalanceAsync(partyId));

        using var response = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, id, "Erreur de saisie");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0m, await ctx.PartyBalanceAsync(partyId));
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
    }

    [Fact]
    public async Task Only_the_supervisor_cancels_and_a_reason_is_required()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync(paid: 36_500m);

        using var byAgent = await ctx.Client.CancelTransferAsync(ctx.NkcAgentToken, id, "Motif");
        using var withoutReason = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, id, "  ");
        using var tooLong = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, id, new string('m', 501));
        using var beforeTransfer = await ctx.Client.CancelTransferAsync(
            ctx.SupervisorToken, id, "Motif", ctx.OperationAt.AddMinutes(-1));
        using var unknown = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, Guid.NewGuid(), "Motif");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Transfers.Cancellation.ReasonRequired", await withoutReason.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Cancellation.ReasonTooLong", await tooLong.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Cancellation.DateBeforeTransfer", await beforeTransfer.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("NotDelivered", (await ctx.GetAsync(ctx.NkcAgentToken, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_delivered_or_already_cancelled_transfer_cannot_be_cancelled()
    {
        var ctx = await ArrangeAsync();
        var (delivered, code) = await ctx.CreateAsync(paid: 36_500m);
        using var deliver = await ctx.Client.DeliverTransferAsync(ctx.DkrAgentToken, delivered, new
        {
            code,
            operationAt = ctx.OperationAt.AddHours(1),
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } },
        });
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        var (cancelled, _) = await ctx.CreateAsync(paid: 36_500m);
        using var first = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Motif");

        using var onDelivered = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, delivered, "Motif");
        using var again = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Motif");

        Assert.Equal(HttpStatusCode.Conflict, onDelivered.StatusCode);
        Assert.Equal("Transfers.Cancellation.Delivered", await onDelivered.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Cancellation.AlreadyCancelled", await again.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Cancelled_transfers_are_left_out_of_the_cards_but_found_by_the_status_filter()
    {
        var ctx = await ArrangeAsync();
        var (kept, _) = await ctx.CreateAsync(paid: 36_500m);
        var (cancelled, _) = await ctx.CreateAsync(paid: 36_500m);
        using var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Motif");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        using var summary = await ctx.Client.SendGetAsync(
            ctx.SupervisorToken, $"/api/v1/transfers/summary?agencyId={ctx.Nkc}&from={ctx.DayText}&to={ctx.DayText}&search={ctx.Tag}");
        var cards = await summary.ReadJsonAsync();
        Assert.Equal(36_500m, cards.GetProperty("incoming").GetProperty("amount").GetDecimal());
        Assert.Equal(1, cards.GetProperty("incoming").GetProperty("count").GetInt32());
        Assert.Equal(500m, cards.GetProperty("profitMro").GetDecimal());

        using var list = await ctx.Client.ListTransfersAsync(
            ctx.SupervisorToken, $"from={ctx.DayText}&to={ctx.DayText}&search={ctx.Tag}&status=Cancelled");
        var ids = (await list.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(t => t.GetProperty("id").GetGuid());
        Assert.Equal([cancelled], ids);
        Assert.NotEqual(kept, cancelled);
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

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr, day, $"c{Guid.NewGuid():N}"[..12],
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
        HawalaApiFactory Factory,
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
        public DateTimeOffset OperationAt => new(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        public string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Nouakchott → Dakar, 50 000 FCFA = 36 500 MRO ; le reste à crédit si un tiers est donné.</summary>
        public async Task<(Guid Id, string Code)> CreateAsync(decimal paid, Guid? creditPartyId = null)
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
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = paid } },
                    creditPartyId,
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("withdrawalCode").GetString()!);
        }

        public async Task<JsonElement> GetAsync(string token, Guid id)
        {
            using var response = await Client.GetTransferAsync(token, id);
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
        public async Task<decimal> PartyBalanceAsync(Guid partyId)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
                .Parties.AsNoTracking().SingleAsync(p => p.Id == partyId);
            var ledgerAccount = party.AgencyAccounts.Single(a => a.AgencyId == Nkc).LedgerAccountId;

            var balances = await scope.ServiceProvider.GetRequiredService<ILedger>()
                .GetBalancesAsync([ledgerAccount], asOf: null, CancellationToken.None);
            return balances.Value.Single().Balance.Amount;
        }
    }
}
