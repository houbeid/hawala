using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transfers.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Livraison (règles validées le 20 octobre 2026) : agence de destination seulement, code saisi par tous,
/// code faux compté par transfert et blocage au 5e, une seule livraison répartie, total = montant à livrer,
/// caisse en négatif acceptée avec avertissement, écriture « Transferts à livrer » / caisses.
/// Chaque test fixe le taux d'une date tirée au hasard entre 1975 et 1995 et crée ses propres caisses.
/// Transfert de référence : Nouakchott → Dakar, 50 000 FCFA = 36 500 MRO (vente 3 650).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliverTransferTests(ApiFixture fixture)
{
    // --- Livraison réussie -------------------------------------------------

    [Fact]
    public async Task Destination_agent_delivers_a_split_payment_with_the_code()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var wave = await ctx.CreateCashAsync("DKR", "wallet");
        await ctx.FundDakarAsync(dakarCash, 30_000m);
        var (id, code) = await ctx.CreateNkcToDkrAsync();

        using var response = await ctx.Client.DeliverTransferAsync(ctx.DkrAgentToken, id, new
        {
            code,
            operationAt = ctx.OperationAt.AddHours(1),
            payments = new[]
            {
                new { treasuryAccountId = dakarCash, amount = 30_000m },
                new { treasuryAccountId = wave, amount = 20_000m },
            },
            note = "Remis au guichet",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var delivered = await response.ReadJsonAsync();
        Assert.Equal(50_000m, delivered.GetProperty("amountDelivered").GetDecimal());
        Assert.Equal("XOF", delivered.GetProperty("currency").GetString());

        // La caisse approvisionnée tombe à 0 ; le wallet vide passe à −20 000 : avertissement, sans blocage.
        var warning = Assert.Single(delivered.GetProperty("negativeBalances").EnumerateArray());
        Assert.Equal(wave, warning.GetProperty("treasuryAccountId").GetGuid());
        Assert.Equal(-20_000m, warning.GetProperty("balance").GetDecimal());
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(dakarCash));

        var view = await ctx.GetAsync(ctx.DkrAgentToken, id);
        Assert.Equal("Delivered", view.GetProperty("status").GetString());
        Assert.Equal(ctx.OperationAt.AddHours(1), view.GetProperty("deliveryOperationAt").GetDateTimeOffset());

        // Écriture équilibrée en FCFA : Transferts à livrer / caisses de Dakar.
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var transfer = await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        Assert.Equal("Remis au guichet", transfer.DeliveryNote);
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == transfer.DeliveryEntryId);
        Assert.Equal("Transfers.Delivered", entry.EntryType);
        Assert.Equal(DateOnly.FromDateTime(ctx.OperationAt.UtcDateTime), entry.OperationDate);
        Assert.Equal(50_000m, entry.Lines.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount));
        Assert.Equal(50_000m, entry.Lines.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount));
    }

    [Fact]
    public async Task Cashier_and_supervisor_can_deliver_and_a_credit_transfer_is_delivered_even_if_unpaid()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (fromCredit, creditCode) = await ctx.CreateNkcToDkrAsync(withCredit: true);
        var (other, otherCode) = await ctx.CreateNkcToDkrAsync();
        var cashier = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Cashier, agencyCode: "DKR");
        var (cashierToken, _) = await ctx.Client.LoginOkAsync(cashier.Email, cashier.Password);

        using var byCashier = await ctx.DeliverAsync(cashierToken, fromCredit, creditCode, dakarCash);
        using var bySupervisor = await ctx.DeliverAsync(ctx.SupervisorToken, other, otherCode, dakarCash);

        Assert.Equal(HttpStatusCode.OK, byCashier.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bySupervisor.StatusCode);
    }

    // --- Code de retrait ---------------------------------------------------

    [Fact]
    public async Task Wrong_codes_count_down_and_the_fifth_locks_the_transfer()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (id, code) = await ctx.CreateNkcToDkrAsync();
        var wrong = WrongCode(code);

        for (var remaining = 4; remaining >= 1; remaining--)
        {
            using var response = await ctx.DeliverAsync(ctx.DkrAgentToken, id, wrong, dakarCash);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.ReadJsonAsync();
            Assert.Equal("Transfers.Delivery.WrongCode", problem.GetProperty("code").GetString());
            Assert.Equal(remaining, problem.GetProperty("remainingAttempts").GetInt32());
        }

        using var fifth = await ctx.DeliverAsync(ctx.DkrAgentToken, id, wrong, dakarCash);
        using var rightCodeTooLate = await ctx.DeliverAsync(ctx.SupervisorToken, id, code, dakarCash);

        Assert.Equal(HttpStatusCode.Conflict, fifth.StatusCode);
        Assert.Equal("Transfers.Delivery.CodeLocked", await fifth.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Delivery.CodeLocked", await rightCodeTooLate.ReadErrorCodeAsync());

        var view = await ctx.GetAsync(ctx.DkrAgentToken, id);
        Assert.True(view.GetProperty("isCodeLocked").GetBoolean());
        Assert.Equal("NotDelivered", view.GetProperty("status").GetString());
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(dakarCash));
    }

    [Fact]
    public async Task Input_errors_are_checked_before_the_code_and_consume_no_attempt()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (id, code) = await ctx.CreateNkcToDkrAsync();
        var wrong = WrongCode(code);

        using var badTotal = await ctx.DeliverAsync(ctx.DkrAgentToken, id, wrong, dakarCash, amount: 49_999m);
        using var thenWrongCode = await ctx.DeliverAsync(ctx.DkrAgentToken, id, wrong, dakarCash);

        Assert.Equal("Transfers.Delivery.TotalMismatch", await badTotal.ReadErrorCodeAsync());
        Assert.Equal(4, (await thenWrongCode.ReadJsonAsync()).GetProperty("remainingAttempts").GetInt32());
    }

    [Fact]
    public async Task Code_is_required()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (id, _) = await ctx.CreateNkcToDkrAsync();

        using var response = await ctx.DeliverAsync(ctx.DkrAgentToken, id, code: " ", dakarCash);

        Assert.Equal("Transfers.Delivery.CodeRequired", await response.ReadErrorCodeAsync());
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Sending_agency_cannot_deliver_and_read_only_has_no_permission()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (id, code) = await ctx.CreateNkcToDkrAsync();
        var readOnly = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.ReadOnly, agencyCode: "DKR");
        var (readOnlyToken, _) = await ctx.Client.LoginOkAsync(readOnly.Email, readOnly.Password);

        using var bySender = await ctx.DeliverAsync(ctx.NkcAgentToken, id, code, dakarCash);
        using var byReadOnly = await ctx.DeliverAsync(readOnlyToken, id, code, dakarCash);

        Assert.Equal(HttpStatusCode.Forbidden, bySender.StatusCode);
        Assert.Equal("Transfers.Delivery.NotDestinationAgency", await bySender.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byReadOnly.StatusCode);
    }

    [Fact]
    public async Task A_transfer_is_delivered_only_once_even_with_two_simultaneous_deliveries()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var (id, code) = await ctx.CreateNkcToDkrAsync();

        var responses = await Task.WhenAll(
            ctx.DeliverAsync(ctx.DkrAgentToken, id, code, dakarCash),
            ctx.DeliverAsync(ctx.DkrAgentToken, id, code, dakarCash));
        using var later = await ctx.DeliverAsync(ctx.DkrAgentToken, id, code, dakarCash);

        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal("Transfers.Delivery.AlreadyDelivered", await later.ReadErrorCodeAsync());
            Assert.Equal(-50_000m, await ctx.TreasuryBalanceAsync(dakarCash));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Delivery_must_use_cash_of_the_destination_agency_and_a_valid_date()
    {
        var ctx = await ArrangeAsync();
        var dakarCash = await ctx.CreateCashAsync("DKR");
        var nkcCash = await ctx.CreateCashAsync("NKC");
        var (id, code) = await ctx.CreateNkcToDkrAsync();

        using var otherAgency = await ctx.DeliverAsync(ctx.SupervisorToken, id, code, nkcCash);
        using var beforeTransfer = await ctx.DeliverAsync(
            ctx.DkrAgentToken, id, code, dakarCash, operationAt: ctx.OperationAt.AddMinutes(-1));
        using var future = await ctx.DeliverAsync(
            ctx.DkrAgentToken, id, code, dakarCash, operationAt: DateTimeOffset.UtcNow.AddHours(1));
        using var deliverUnknown = await ctx.DeliverAsync(ctx.DkrAgentToken, Guid.NewGuid(), code, dakarCash);

        Assert.Equal("Transfers.Payment.AccountNotAllowed", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Delivery.DateBeforeTransfer", await beforeTransfer.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.FutureDate", await future.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, deliverUnknown.StatusCode);

        // Rien n'a été livré ni consommé.
        var view = await ctx.GetAsync(ctx.DkrAgentToken, id);
        Assert.Equal("NotDelivered", view.GetProperty("status").GetString());
    }

    // --- Outils ------------------------------------------------------------

    private static string WrongCode(string code) =>
        ((int.Parse(code, CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

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

        var nkcCash = await CreateCashAsync(client, token, nkc, "cash");
        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero), nkcCash);
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId, string type)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type, agencyId });
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
        DateTimeOffset OperationAt,
        Guid NkcCash)
    {
        public Task<Guid> CreateCashAsync(string agencyCode, string type = "cash") =>
            DeliverTransferTests.CreateCashAsync(Client, SupervisorToken, agencyCode == "NKC" ? Nkc : Dkr, type);

        /// <summary>Nouakchott → Dakar, 50 000 FCFA ; comptant, ou entièrement à crédit sur un nouveau tiers.</summary>
        public async Task<(Guid Id, string Code)> CreateNkcToDkrAsync(bool withCredit = false)
        {
            Guid? party = null;
            if (withCredit)
            {
                using var created = await Client.CreatePartyAsync(
                    SupervisorToken, new { name = $"Débiteur {Guid.NewGuid():N}"[..30], type = "Client" });
                party = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();
            }

            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = withCredit
                        ? Array.Empty<object>()
                        : [new { treasuryAccountId = NkcCash, amount = 36_500m }],
                    creditPartyId = party,
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("withdrawalCode").GetString()!);
        }

        /// <summary>Approvisionne une caisse de Dakar : un transfert Dakar → Nouakchott payé dans cette caisse.</summary>
        public async Task FundDakarAsync(Guid dakarCash, decimal amountXof)
        {
            using var response = await Client.CreateTransferAsync(
                DkrAgentToken,
                new
                {
                    destinationAgencyId = Nkc,
                    amountXof,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = dakarCash, amount = amountXof } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        public Task<HttpResponseMessage> DeliverAsync(
            string token, Guid id, string code, Guid cash, decimal amount = 50_000m, DateTimeOffset? operationAt = null) =>
            Client.DeliverTransferAsync(token, id, new
            {
                code,
                operationAt = operationAt ?? OperationAt.AddHours(1),
                payments = new[] { new { treasuryAccountId = cash, amount } },
            });

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
    }
}
