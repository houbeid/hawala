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
/// Modification d'argent par le Superviseur (validée le 21 octobre 2026) : l'écriture en vigueur est contre-passée
/// et remplacée au jour de la modification ; plafond appliqué à la nouvelle part sans l'ancienne ; motif obligatoire.
/// Transfert de référence : Nouakchott → Dakar, 50 000 FCFA à 3 650 = 36 500 MRO ; taux fixé sur une date tirée au hasard.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EditTransferAmountsTests(ApiFixture fixture)
{
    [Fact]
    public async Task Supervisor_changes_amount_rate_and_split_the_entry_is_reversed_and_replaced()
    {
        var ctx = await ArrangeAsync();
        var wallet = await ctx.CreateCashAsync("wallet");
        var id = await ctx.CreateAsync(paid: 36_500m);
        var originalEntry = await ctx.EntryInForceAsync(id);

        // 40 000 FCFA à 3 640 = 29 120 MRO : 20 000 en caisse, 9 120 sur le wallet.
        using var response = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, id, new
        {
            version = 1,
            amountXof = 40_000m,
            saleRate = 3640m,
            payments = new[]
            {
                new { treasuryAccountId = ctx.NkcCash, amount = 20_000m },
                new { treasuryAccountId = wallet, amount = 9_120m },
            },
            reason = "Montant mal saisi",
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var view = await ctx.GetAsync(ctx.SupervisorToken, id);
        Assert.Equal(40_000m, view.GetProperty("amountXof").GetDecimal());
        Assert.Equal(29_120m, view.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3640m, view.GetProperty("saleRate").GetDecimal());
        Assert.Equal(3600m, view.GetProperty("purchaseRate").GetDecimal());
        Assert.Equal(2, view.GetProperty("version").GetInt32());
        Assert.Equal(20_000m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
        Assert.Equal(9_120m, await ctx.TreasuryBalanceAsync(wallet));

        // L'ancienne écriture est contre-passée ; la nouvelle est en vigueur, au jour de la modification.
        var newEntry = await ctx.EntryInForceAsync(id);
        Assert.NotEqual(originalEntry, newEntry);
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var reversal = await ledger.JournalEntries.SingleAsync(e => e.ReversesEntryId == originalEntry);
        Assert.Equal("Montant mal saisi", reversal.Reason);
        var replacement = await ledger.JournalEntries.SingleAsync(e => e.Id == newEntry);
        Assert.Equal("Transfers.Edited", replacement.EntryType);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), replacement.OperationDate);
    }

    [Fact]
    public async Task History_shows_the_split_change_to_the_supervisor_but_not_to_the_destination_agency()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateAsync(paid: 36_500m);
        using var edit = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, id, new
        {
            version = 1,
            amountXof = 40_000m,
            payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 29_200m } },
            reason = "Montant mal saisi",
        });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);

        var forSupervisor = await ctx.LastChangesAsync(ctx.SupervisorToken, id);
        var forDakar = await ctx.LastChangesAsync(ctx.DkrAgentToken, id);

        Assert.Contains("Payments", forSupervisor);
        Assert.Contains("AmountMro", forSupervisor);
        Assert.DoesNotContain("Payments", forDakar);
        Assert.Contains("AmountMro", forDakar);
    }

    [Fact]
    public async Task Credit_limit_counts_the_new_part_without_the_old_one()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync(limit: 20_000m);
        var id = await ctx.CreateAsync(paid: 20_000m, creditPartyId: party);
        Assert.Equal(16_500m, await ctx.PartyBalanceAsync(party));

        // Nouvelle part 18 000 : 16 500 + 18 000 dépasserait 20 000, mais l'ancienne part est contre-passée d'abord.
        using var accepted = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, id, new
        {
            version = 1,
            payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 18_500m } },
            creditPartyId = party,
            reason = "Le client a moins payé",
        });
        using var refused = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, id, new
        {
            version = 2,
            payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 10_000m } },
            creditPartyId = party,
            reason = "Encore moins",
        });

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Transfers.Credit.LimitExceeded", await refused.ReadErrorCodeAsync());

        // Le refus n'a rien laissé : la part à crédit reste 18 000, la caisse 18 500, la version 2.
        Assert.Equal(18_000m, await ctx.PartyBalanceAsync(party));
        Assert.Equal(18_500m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
        Assert.Equal(2, (await ctx.GetAsync(ctx.SupervisorToken, id)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Cancelling_after_an_edit_reverses_the_entry_in_force()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateAsync(paid: 36_500m);
        using var edit = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, id, new
        {
            version = 1,
            amountXof = 40_000m,
            payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 29_200m } },
            reason = "Montant mal saisi",
        });

        using var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, id, "Client a renoncé");

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
    }

    [Fact]
    public async Task Only_the_supervisor_edits_amounts_with_a_reason_and_the_current_version()
    {
        var ctx = await ArrangeAsync();
        var id = await ctx.CreateAsync(paid: 36_500m);
        var payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 29_200m } };

        using var byAgent = await ctx.Client.EditTransferAmountsAsync(
            ctx.NkcAgentToken, id, new { version = 1, amountXof = 40_000m, payments, reason = "Motif" });
        using var noReason = await ctx.Client.EditTransferAmountsAsync(
            ctx.SupervisorToken, id, new { version = 1, amountXof = 40_000m, payments });
        using var noVersion = await ctx.Client.EditTransferAmountsAsync(
            ctx.SupervisorToken, id, new { amountXof = 40_000m, payments, reason = "Motif" });
        using var stale = await ctx.Client.EditTransferAmountsAsync(
            ctx.SupervisorToken, id, new { version = 9, amountXof = 40_000m, payments, reason = "Motif" });
        using var badRate = await ctx.Client.EditTransferAmountsAsync(
            ctx.SupervisorToken, id, new { version = 1, saleRate = 3650.123m, payments, reason = "Motif" });

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Transfers.Transfer.ReasonRequired", await noReason.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.VersionRequired", await noVersion.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.VersionConflict", await stale.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, badRate.StatusCode);
        Assert.Equal(36_500m, await ctx.TreasuryBalanceAsync(ctx.NkcCash));
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

        var ctx = new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), Guid.Empty);
        return ctx with { NkcCash = await ctx.CreateCashAsync("cash") };
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Nkc,
        Guid Dkr,
        DateTimeOffset OperationAt,
        Guid NkcCash)
    {
        public async Task<Guid> CreateCashAsync(string type)
        {
            using var response = await Client.CreateTreasuryAccountAsync(
                SupervisorToken, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type, agencyId = Nkc });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<Guid> CreatePartyAsync(decimal limit)
        {
            using var response = await Client.CreatePartyAsync(
                SupervisorToken, new { name = $"Débiteur {Guid.NewGuid():N}"[..30], type = "Client" });
            var party = (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
            using var set = await Client.SetCreditLimitAsync(SupervisorToken, party, Nkc, limit);
            Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
            return party;
        }

        /// <summary>Nouakchott → Dakar, 50 000 FCFA = 36 500 MRO ; le reste à crédit si un tiers est donné.</summary>
        public async Task<Guid> CreateAsync(decimal paid, Guid? creditPartyId = null)
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
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = paid } },
                    creditPartyId,
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> GetAsync(string token, Guid id)
        {
            using var response = await Client.GetTransferAsync(token, id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<IReadOnlyList<string>> LastChangesAsync(string token, Guid id)
        {
            using var response = await Client.SendGetAsync(token, $"/api/v1/transfers/{id}/history");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var last = (await response.ReadJsonAsync()).EnumerateArray().Last();
            return [.. last.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString()!)];
        }

        public async Task<Guid> EntryInForceAsync(Guid id)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
                .Transfers.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == id)).CreationEntryId;
        }

        public async Task<decimal> TreasuryBalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }

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
