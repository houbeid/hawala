using System.Net;
using Identity.Domain.Roles;
using Hawala.SharedKernel.Messaging;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parties.Infrastructure.Persistence;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Transfert à crédit (règles validées le 20 octobre 2026) : la part non payée va en dette sur un tiers choisi,
/// dans l'agence d'envoi ; crédit partiel ou total ; plafond bloquant pour tous (solde après transfert ≤ plafond).
/// Chaque test crée son tiers, ses caisses et fixe le taux d'une date tirée au hasard entre 1975 et 1995.
/// Taux de vente 3 650 : 5 000 FCFA = 3 650 MRO.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CreditTransferTests(ApiFixture fixture)
{
    // --- Crédit accepté ----------------------------------------------------

    [Fact]
    public async Task Partial_credit_puts_the_unpaid_part_in_red_on_the_chosen_party()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.CreateCashAsync("NKC");
        var party = await ctx.CreatePartyAsync();
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        // 50 000 FCFA = 36 500 MRO : 20 000 payés, 16 500 à crédit.
        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, ctx.Body(50_000m, party, (cash, 20_000m)), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync();
        Assert.Equal(16_500m, created.GetProperty("creditAmount").GetDecimal());
        Assert.Equal(16_500m, await ctx.PartyBalanceAsync(party, ctx.Nkc));
        Assert.Equal(20_000m, await ctx.TreasuryBalanceAsync(cash));

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var transfer = await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().SingleAsync(t => t.Id == created.GetProperty("id").GetGuid());
        Assert.Equal(party, transfer.CreditPartyId);
        Assert.Equal(16_500m, transfer.CreditAmount);
    }

    [Fact]
    public async Task Full_credit_needs_no_payment()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var response = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(36_500m, (await response.ReadJsonAsync()).GetProperty("creditAmount").GetDecimal());
        Assert.Equal(36_500m, await ctx.PartyBalanceAsync(party, ctx.Nkc));
    }

    [Fact]
    public async Task Credit_up_to_exactly_the_limit_is_accepted()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        await ctx.SetLimitAsync(party, ctx.Nkc, 36_500m);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var response = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task With_a_zero_limit_a_party_we_owe_can_still_use_what_we_owe_it()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        await ctx.SetLimitAsync(party, ctx.Nkc, 0m);

        // Vert : on lui doit 40 000 MRO.
        using var opening = await ctx.Client.SetOpeningBalanceAsync(ctx.SupervisorToken, party, ctx.Nkc, new { amount = -40_000m });
        Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var accepted = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey());
        using var refused = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(10_000m, party), NewKey());

        // −40 000 + 36 500 = −3 500 ≤ 0 : accepté ; −3 500 + 7 300 = 3 800 > 0 : refusé.
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("Transfers.Credit.LimitExceeded", await refused.ReadErrorCodeAsync());
        Assert.Equal(-3_500m, await ctx.PartyBalanceAsync(party, ctx.Nkc));
    }

    // --- Crédit refusé -----------------------------------------------------

    [Fact]
    public async Task Credit_above_the_limit_is_refused_even_for_the_supervisor_and_writes_nothing()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.CreateCashAsync("NKC");
        var party = await ctx.CreatePartyAsync();
        await ctx.SetLimitAsync(party, ctx.Nkc, 10_000m);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        // 16 500 à crédit pour un plafond de 10 000.
        using var byAgent = await ctx.Client.CreateTransferAsync(
            agentToken, ctx.Body(50_000m, party, (cash, 20_000m)), NewKey());
        using var bySupervisor = await ctx.Client.CreateTransferAsync(
            ctx.SupervisorToken, ctx.Body(50_000m, party, (cash, 20_000m), source: ctx.Nkc), NewKey());

        Assert.Equal("Transfers.Credit.LimitExceeded", await byAgent.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Credit.LimitExceeded", await bySupervisor.ReadErrorCodeAsync());
        Assert.Equal(0m, await ctx.PartyBalanceAsync(party, ctx.Nkc));
        Assert.Equal(0m, await ctx.TreasuryBalanceAsync(cash));
    }

    [Fact]
    public async Task Limit_counts_the_existing_debt_of_the_party()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        await ctx.SetLimitAsync(party, ctx.Nkc, 40_000m);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var first = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey());
        using var second = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(10_000m, party), NewKey());

        // 36 500 puis 36 500 + 7 300 = 43 800 > 40 000.
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal("Transfers.Credit.LimitExceeded", await second.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Two_simultaneous_credits_cannot_exceed_the_limit_together()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        await ctx.SetLimitAsync(party, ctx.Nkc, 50_000m);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        // Chacun passe seul (36 500 ≤ 50 000), pas les deux (73 000).
        var responses = await Task.WhenAll(
            ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey()),
            ctx.Client.CreateTransferAsync(agentToken, ctx.Body(50_000m, party), NewKey()));

        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(36_500m, await ctx.PartyBalanceAsync(party, ctx.Nkc));
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
    public async Task Credit_with_everything_paid_is_refused()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.CreateCashAsync("NKC");
        var party = await ctx.CreatePartyAsync();
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, ctx.Body(50_000m, party, (cash, 36_500m)), NewKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Transfers.Credit.NotNeeded", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Unknown_or_archived_party_is_refused()
    {
        var ctx = await ArrangeAsync();
        var archived = await ctx.CreatePartyAsync();
        using var archive = await ctx.Client.ArchivePartyAsync(ctx.SupervisorToken, archived);
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var unknown = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(5000m, Guid.NewGuid()), NewKey());
        using var onArchived = await ctx.Client.CreateTransferAsync(agentToken, ctx.Body(5000m, archived), NewKey());

        Assert.Equal("Transfers.Credit.PartyNotFound", await unknown.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Credit.PartyArchived", await onArchived.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Paying_more_than_the_amount_is_refused_even_with_credit()
    {
        var ctx = await ArrangeAsync();
        var cash = await ctx.CreateCashAsync("NKC");
        var party = await ctx.CreatePartyAsync();
        var agentToken = await ctx.LoginAsync(RoleTemplate.Agent);

        using var response = await ctx.Client.CreateTransferAsync(
            agentToken, ctx.Body(50_000m, party, (cash, 40_000m)), NewKey());

        Assert.Equal("Transfers.Payment.TotalMismatch", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Without_transfers_credit_the_credit_is_refused()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();

        // Aucun rôle n'a aujourd'hui transfers.create sans transfers.credit (retraits par compte : phase 6) :
        // le handler est appelé directement, avec un caissier, qui n'a pas transfers.credit.
        var cashier = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Cashier);
        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateTransferCommand, CreatedTransfer>>();

        var result = await handler.Handle(
            new CreateTransferCommand(
                cashier.Id, NewKey(), null, ctx.Dkr, 5000m, "Expéditeur fictif", "Bénéficiaire fictif", null,
                ctx.OperationAt, [], party),
            CancellationToken.None);

        Assert.Equal("Transfers.Credit.NotAllowed", result.Error.Code);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewKey() => Guid.NewGuid().ToString("N");

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

        return new Context(factory, client, token, nkc, dkr, new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));
    }

    private sealed record Context(
        HawalaApiFactory Factory, HttpClient Client, string SupervisorToken, Guid Nkc, Guid Dkr, DateTimeOffset OperationAt)
    {
        /// <summary>Transfert Nouakchott → Dakar à crédit sur <paramref name="party"/>.</summary>
        public object Body(decimal amountXof, Guid party, params (Guid Account, decimal Amount)[] payments) =>
            Body(amountXof, party, payments, source: null);

        public object Body(decimal amountXof, Guid party, (Guid Account, decimal Amount) payment, Guid? source) =>
            Body(amountXof, party, [payment], source);

        private object Body(decimal amountXof, Guid party, (Guid Account, decimal Amount)[] payments, Guid? source) => new
        {
            sourceAgencyId = source,
            destinationAgencyId = Dkr,
            amountXof,
            senderInfo = "Expéditeur fictif",
            beneficiaryInfo = "Bénéficiaire fictif",
            operationAt = OperationAt,
            payments = payments.Select(p => new { treasuryAccountId = p.Account, amount = p.Amount }).ToArray(),
            creditPartyId = party,
        };

        public async Task<Guid> CreateCashAsync(string agencyCode)
        {
            using var response = await Client.CreateTreasuryAccountAsync(
                SupervisorToken,
                new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = agencyCode == "NKC" ? Nkc : Dkr });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<Guid> CreatePartyAsync()
        {
            using var response = await Client.CreatePartyAsync(
                SupervisorToken, new { name = $"Débiteur {Guid.NewGuid():N}"[..30], type = "Client" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task SetLimitAsync(Guid party, Guid agencyId, decimal? limit)
        {
            using var response = await Client.SetCreditLimitAsync(SupervisorToken, party, agencyId, limit);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public async Task<string> LoginAsync(RoleTemplate role)
        {
            var account = await TestAccounts.CreateAsync(Factory, role, agencyCode: "NKC");
            var (token, _) = await Client.LoginOkAsync(account.Email, account.Password);
            return token;
        }

        public async Task<decimal> TreasuryBalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }

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
