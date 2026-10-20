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
/// Création d'un transfert payé comptant : transfert, répartition et écriture au grand livre dans une seule transaction.
/// La base est partagée et un taux est unique par date : chaque test fixe le taux d'une date tirée au hasard
/// (entre 1975 et 1995, plage que les tests des taux n'utilisent pas) et crée ses propres comptes de trésorerie.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CreateTransferTests(ApiFixture fixture)
{
    // --- Création réussie --------------------------------------------------

    [Fact]
    public async Task Agent_of_nouakchott_creates_a_transfer_paid_in_mro_and_split_between_two_methods()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var wallet = await ctx.CreateTreasuryAccountAsync("NKC", "wallet");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken,
            Body(ctx.Dkr, 5_000_000m, date, (cash, 2_000_000m), (wallet, 1_650_000m)),
            NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        var id = created.GetProperty("id").GetGuid();
        var code = created.GetProperty("withdrawalCode").GetString();
        Assert.Equal($"/api/v1/transfers/{id}", response.Headers.Location?.ToString());
        Assert.Matches("^[0-9]{6}$", code);
        Assert.StartsWith("TR-NKC-", created.GetProperty("reference").GetString(), StringComparison.Ordinal);
        Assert.Equal(5_000_000m, created.GetProperty("amountXof").GetDecimal());
        Assert.Equal(3_650_000m, created.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3650m, created.GetProperty("saleRate").GetDecimal());
        Assert.False(created.GetProperty("replayed").GetBoolean());

        // Enregistré avec ses deux taux, statut « non livré » ; le code n'est gardé que haché.
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var transfer = await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        Assert.Equal(ctx.Nkc, transfer.SourceAgencyId);
        Assert.Equal(ctx.Dkr, transfer.DestinationAgencyId);
        Assert.Equal(3600m, transfer.PurchaseRate.MroPer5000Xof);
        Assert.Equal("NotDelivered", transfer.Status.ToString());
        Assert.DoesNotContain(code!, transfer.WithdrawalCodeHash, StringComparison.Ordinal);
        Assert.Equal(2, transfer.Payments.Count);

        // Écriture équilibrée par devise : MRO encaissés à Nouakchott, FCFA à livrer à Dakar.
        var entry = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .JournalEntries.Include(e => e.Lines).SingleAsync(e => e.Id == transfer.CreationEntryId);
        Assert.Equal("Transfers.Created", entry.EntryType);
        Assert.Equal(id, entry.SourceId);
        Assert.Equal(DateOnly.FromDateTime(date.UtcDateTime), entry.OperationDate);
        AssertBalanced(entry.Lines, "MRO", 3_650_000m);
        AssertBalanced(entry.Lines, "XOF", 5_000_000m);

        // Les caisses de l'agence d'envoi ont reçu leur part.
        Assert.Equal(2_000_000m, await ctx.TreasuryBalanceAsync(cash));
        Assert.Equal(1_650_000m, await ctx.TreasuryBalanceAsync(wallet));
    }

    [Fact]
    public async Task Agent_of_dakar_creates_a_transfer_paid_in_fcfa_to_nouakchott()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3580m, sale: 3604.77m);
        var cash = await ctx.CreateTreasuryAccountAsync("DKR", "cash");
        var agentToken = await ctx.LoginAgentAsync("DKR");

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Nkc, 1_000_000m, date, (cash, 1_000_000m)), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        Assert.StartsWith("TR-DKR-", created.GetProperty("reference").GetString(), StringComparison.Ordinal);

        // 1 000 000 × 3 604,77 / 5000 = 720 954 MRO, à livrer à Nouakchott.
        Assert.Equal(720_954m, created.GetProperty("amountMro").GetDecimal());
        Assert.Equal(1_000_000m, await ctx.TreasuryBalanceAsync(cash));
    }

    [Fact]
    public async Task Supervisor_chooses_the_sending_agency()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("DKR", "cash");

        using var withoutSource = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, Body(ctx.Nkc, 100_000m, date, (cash, 100_000m)), NewKey());
        using var fromDakar = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, Body(ctx.Nkc, 100_000m, date, (cash, 100_000m), source: ctx.Dkr), NewKey());

        Assert.Equal("Transfers.Transfer.SourceRequired", await withoutSource.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Created, fromDakar.StatusCode);
    }

    [Fact]
    public async Task References_follow_each_other_for_the_same_agency_and_day()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        var first = await CreateOkAsync(ctx, agentToken, Body(ctx.Dkr, 5000m, date, (cash, 3650m)));
        var second = await CreateOkAsync(ctx, agentToken, Body(ctx.Dkr, 5000m, date, (cash, 3650m)));

        var firstReference = first.GetProperty("reference").GetString()!;
        var secondReference = second.GetProperty("reference").GetString()!;
        Assert.Equal(SequenceOf(firstReference) + 1, SequenceOf(secondReference));
        Assert.Equal(firstReference[..^7], secondReference[..^7]);
    }

    [Fact]
    public async Task Operation_date_defaults_to_now_with_the_rate_of_today()
    {
        var ctx = await ArrangeAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var rate = await ctx.Client.SetDailyRateAsync(ctx.SupervisorToken, today, 3600m, 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        var created = await CreateOkAsync(ctx, agentToken, Body(ctx.Dkr, 10_000m, operationAt: null, (cash, 7300m)));

        Assert.Equal(7300m, created.GetProperty("amountMro").GetDecimal());
    }

    // --- Idempotence ------------------------------------------------------

    [Fact]
    public async Task Same_idempotency_key_returns_the_existing_transfer_without_its_code()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");
        var key = NewKey();
        var body = Body(ctx.Dkr, 50_000m, date, (cash, 36_500m));

        using var first = await ctx.Client.CreateTransferAsync(agentToken, body, key);
        using var again = await ctx.Client.CreateTransferAsync(agentToken, body, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var created = await first.ReadJsonAsync();
        var replayed = await again.ReadJsonAsync();
        Assert.Equal(created.GetProperty("id").GetGuid(), replayed.GetProperty("id").GetGuid());
        Assert.True(replayed.GetProperty("replayed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, replayed.GetProperty("withdrawalCode").ValueKind);

        // Un seul transfert, une seule fois l'argent en caisse.
        Assert.Equal(36_500m, await ctx.TreasuryBalanceAsync(cash));
    }

    [Fact]
    public async Task Idempotency_key_is_required()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5000m, date, (cash, 3650m)), idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Transfers.Transfer.IdempotencyKeyRequired", await response.ReadErrorCodeAsync());
    }

    // --- Refus ------------------------------------------------------------

    [Fact]
    public async Task Agent_cannot_create_a_transfer_from_the_other_agency()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("DKR", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Nkc, 5000m, date, (cash, 5000m), source: ctx.Dkr), NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Transfers.Transfer.SourceNotAllowed", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Transfer_without_a_rate_for_its_day_is_refused()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        // 1970 : aucun test ne fixe de taux avant 1975.
        var noRateDay = new DateTimeOffset(1970, 6, 1, 10, 0, 0, TimeSpan.Zero);
        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5000m, noRateDay, (cash, 3650m)), NewKey());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Transfers.Transfer.RateNotSet", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Payment_method_must_be_an_active_account_of_the_sending_agency()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var dakarCash = await ctx.CreateTreasuryAccountAsync("DKR", "cash");
        var closed = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        using var deactivate = await ctx.Client.DeactivateTreasuryAccountAsync(ctx.SupervisorToken, closed);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        using var otherAgency = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, Body(ctx.Dkr, 5000m, date, (dakarCash, 3650m), source: ctx.Nkc), NewKey());
        using var inactive = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, Body(ctx.Dkr, 5000m, date, (closed, 3650m), source: ctx.Nkc), NewKey());
        using var unknown = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, Body(ctx.Dkr, 5000m, date, (Guid.NewGuid(), 3650m), source: ctx.Nkc), NewKey());

        Assert.Equal("Transfers.Payment.AccountNotAllowed", await otherAgency.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Payment.AccountNotAllowed", await inactive.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Payment.AccountNotAllowed", await unknown.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Refused_transfer_writes_nothing()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");
        var key = NewKey();

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5_000_000m, date, (cash, 3_000_000m)), key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Transfers.Payment.TotalMismatch", await response.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(cash));

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().AnyAsync(t => t.IdempotencyKey == key));
    }

    [Fact]
    public async Task Invalid_inputs_are_refused_with_their_code()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var agentToken = await ctx.LoginAgentAsync("NKC");

        using var decimalFcfa = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5000.5m, date, (cash, 3650m)), NewKey());
        using var sameAgency = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Nkc, 5000m, date, (cash, 3650m)), NewKey());
        using var noDestination = await ctx.Client.CreateTransferAsync(
            agentToken, Body(null, 5000m, date, (cash, 3650m)), NewKey());
        using var future = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5000m, DateTimeOffset.UtcNow.AddHours(2), (cash, 3650m)), NewKey());
        using var noPayment = await ctx.Client.CreateTransferAsync(
            agentToken, Body(ctx.Dkr, 5000m, date), NewKey());
        using var noSender = await ctx.Client.CreateTransferAsync(
            agentToken,
            new { destinationAgencyId = ctx.Dkr, amountXof = 5000m, beneficiaryInfo = "Bénéficiaire fictif", operationAt = date, payments = new[] { new { treasuryAccountId = cash, amount = 3650m } } },
            NewKey());

        Assert.Equal("Transfers.Transfer.AmountInvalid", await decimalFcfa.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.CorridorInvalid", await sameAgency.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.DestinationNotFound", await noDestination.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.FutureDate", await future.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Payment.Required", await noPayment.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.SenderRequired", await noSender.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Cashier_cannot_create_a_transfer_and_anonymous_is_rejected()
    {
        var ctx = await ArrangeAsync();
        var date = await ctx.SetRateAsync(purchase: 3600m, sale: 3650m);
        var cash = await ctx.CreateTreasuryAccountAsync("NKC", "cash");
        var cashier = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Cashier);
        var (cashierToken, _) = await ctx.Client.LoginOkAsync(cashier.Email, cashier.Password);

        using var byCashier = await ctx.Client.CreateTransferAsync(
            cashierToken, Body(ctx.Dkr, 5000m, date, (cash, 3650m)), NewKey());
        using var anonymous = await ctx.Client.CreateTransferAsync(
            accessToken: null, Body(ctx.Dkr, 5000m, date, (cash, 3650m)), NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, byCashier.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static object Body(
        Guid? destination,
        decimal amountXof,
        DateTimeOffset? operationAt,
        params (Guid Account, decimal Amount)[] payments) =>
        Body(destination, amountXof, operationAt, payments, source: null);

    private static object Body(
        Guid? destination,
        decimal amountXof,
        DateTimeOffset? operationAt,
        (Guid Account, decimal Amount) payment,
        Guid? source) =>
        Body(destination, amountXof, operationAt, [payment], source);

    private static object Body(
        Guid? destination,
        decimal amountXof,
        DateTimeOffset? operationAt,
        (Guid Account, decimal Amount)[] payments,
        Guid? source) => new
        {
            sourceAgencyId = source,
            destinationAgencyId = destination,
            amountXof,
            senderInfo = "Expéditeur fictif 22200000",
            beneficiaryInfo = "Bénéficiaire fictif 770000000",
            note = "Note fictive",
            operationAt,
            payments = payments.Select(p => new { treasuryAccountId = p.Account, amount = p.Amount }).ToArray(),
        };

    private static int SequenceOf(string reference) =>
        int.Parse(reference[^6..], CultureInfo.InvariantCulture);

    private static void AssertBalanced(IEnumerable<global::Ledger.Domain.Entries.JournalLine> lines, string currency, decimal expected)
    {
        var inCurrency = lines.Where(l => l.Currency.ToString() == currency).ToList();
        Assert.Equal(expected, inCurrency.Where(l => l.Side == EntrySide.Debit).Sum(l => l.Amount));
        Assert.Equal(expected, inCurrency.Where(l => l.Side == EntrySide.Credit).Sum(l => l.Amount));
    }

    private static async Task<JsonElement> CreateOkAsync(Context ctx, string token, object body)
    {
        using var response = await ctx.Client.CreateTransferAsync(token, body, NewKey());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return new Context(factory, client, token, nkc, dkr);
    }

    private sealed record Context(HawalaApiFactory Factory, HttpClient Client, string SupervisorToken, Guid Nkc, Guid Dkr)
    {
        /// <summary>Fixe les taux d'une date passée au hasard et renvoie un instant de ce jour-là.</summary>
        public async Task<DateTimeOffset> SetRateAsync(decimal purchase, decimal sale)
        {
            var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
            using var response = await Client.SetDailyRateAsync(SupervisorToken, day, purchase, sale);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            return new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        }

        public async Task<Guid> CreateTreasuryAccountAsync(string agencyCode, string type)
        {
            var agencyId = agencyCode == "NKC" ? Nkc : Dkr;
            using var response = await Client.CreateTreasuryAccountAsync(
                SupervisorToken, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type, agencyId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<string> LoginAgentAsync(string agencyCode)
        {
            var agent = await TestAccounts.CreateAsync(Factory, RoleTemplate.Agent, agencyCode: agencyCode);
            var (token, _) = await Client.LoginOkAsync(agent.Email, agent.Password);
            return token;
        }

        public async Task<decimal> TreasuryBalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }
    }
}
