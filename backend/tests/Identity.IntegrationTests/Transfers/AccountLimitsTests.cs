using System.Net;
using System.Text.Json;
using Identity.Application.Features.Users.Limits;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Plafonds par compte (phase 6, règles validées le 22 octobre 2026) : par transfert, par jour et crédit par jour,
/// dans la devise de l'agence d'envoi, sur les transferts enregistrés dans la journée ; refus net avec le montant
/// encore possible ; jamais pour un Superviseur ; fixés par un Superviseur, tracés dans l'audit.
/// Taux de vente 3 650 : 10 000 FCFA = 7 300 MRO payés à Nouakchott.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountLimitsTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_transfer_above_the_limit_per_transfer_is_refused()
    {
        var ctx = await ArrangeAsync();
        using var set = await ctx.SetLimitsAsync(new { perTransfer = 7_000m });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        using var tooBig = await ctx.SendAsync(10_000m);
        using var fine = await ctx.SendAsync(5_000m);

        await AssertRefusedAsync(tooBig, "Transfers.Limits.PerTransferExceeded", remaining: 7_000m, "MRO");
        Assert.Equal(HttpStatusCode.Created, fine.StatusCode);
    }

    [Fact]
    public async Task The_day_total_cannot_exceed_the_limit_per_day()
    {
        var ctx = await ArrangeAsync();
        using var set = await ctx.SetLimitsAsync(new { perDay = 10_000m });

        using var first = await ctx.SendAsync(10_000m);
        using var second = await ctx.SendAsync(10_000m);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        await AssertRefusedAsync(second, "Transfers.Limits.PerDayExceeded", remaining: 2_700m, "MRO");
    }

    [Fact]
    public async Task A_cancelled_transfer_no_longer_counts_for_the_day()
    {
        var ctx = await ArrangeAsync();
        using var set = await ctx.SetLimitsAsync(new { perDay = 10_000m });
        using var first = await ctx.SendAsync(10_000m);
        var id = (await first.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, id, "Erreur de saisie");
        using var second = await ctx.SendAsync(10_000m);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Credit_given_in_the_day_cannot_exceed_the_credit_limit()
    {
        var ctx = await ArrangeAsync();
        using var set = await ctx.SetLimitsAsync(new { creditPerDay = 5_000m });
        var party = await ctx.CreatePartyAsync();

        // Tout à crédit : 7 300 MRO > 5 000.
        using var allOnCredit = await ctx.SendAsync(10_000m, party, payment: null);

        // 3 650 MRO payés, 3 650 à crédit : passe ; un second crédit de 3 650 dépasse (reste 1 350).
        using var halfOnCredit = await ctx.SendAsync(10_000m, party, payment: 3_650m);
        using var again = await ctx.SendAsync(10_000m, party, payment: 3_650m);

        await AssertRefusedAsync(allOnCredit, "Transfers.Limits.CreditPerDayExceeded", remaining: 5_000m, "MRO");
        Assert.Equal(HttpStatusCode.Created, halfOnCredit.StatusCode);
        await AssertRefusedAsync(again, "Transfers.Limits.CreditPerDayExceeded", remaining: 1_350m, "MRO");
    }

    [Fact]
    public async Task A_supervisor_never_has_a_limit()
    {
        var ctx = await ArrangeAsync();
        var other = await TestAccounts.CreateAsync(ctx.Factory, RoleTemplate.Supervisor);

        using var set = await ctx.Client.PutJsonAsync(ctx.SupervisorToken, $"/api/v1/users/{other.Id}/limits", new { perTransfer = 1m });

        Assert.Equal(HttpStatusCode.Conflict, set.StatusCode);
        Assert.Equal("Identity.Limits.SupervisorHasNone", await set.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_limits_are_refused()
    {
        var ctx = await ArrangeAsync();
        var dakarAgent = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "DKR");

        using var negative = await ctx.SetLimitsAsync(new { perDay = -1m });
        using var tooPreciseMro = await ctx.SetLimitsAsync(new { perDay = 10.125m });
        using var fcfaWithDecimals = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, $"/api/v1/users/{dakarAgent.Id}/limits", new { perTransfer = 1_000.5m });

        Assert.Equal("Identity.Limits.Negative", await negative.ReadErrorCodeAsync());
        Assert.Equal("Identity.Limits.TooPrecise", await tooPreciseMro.ReadErrorCodeAsync());
        Assert.Equal("Identity.Limits.TooPrecise", await fcfaWithDecimals.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Only_a_supervisor_sets_limits()
    {
        var ctx = await ArrangeAsync();
        var manager = await TestAccounts.CreateAsync(ctx.Factory, agencyCode: "NKC");
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.UsersManage];
        using var grant = await ctx.Client.PutJsonAsync(
            ctx.SupervisorToken, $"/api/v1/users/{manager.Id}/permissions", new { permissions });
        var (managerToken, _) = await ctx.Client.LoginOkAsync(manager.Email, manager.Password);

        using var set = await ctx.Client.PutJsonAsync(managerToken, $"/api/v1/users/{ctx.Agent.Id}/limits", new { perDay = 1m });
        using var read = await ctx.Client.SendGetAsync(managerToken, $"/api/v1/users/{ctx.Agent.Id}/limits");

        Assert.Equal(HttpStatusCode.Forbidden, set.StatusCode);
        Assert.Equal("Identity.Account.SupervisorOnly", await set.ReadErrorCodeAsync());

        // Il lit les plafonds d'un compte de son agence.
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Moving_the_account_clears_its_limits()
    {
        var ctx = await ArrangeAsync();
        using var set = await ctx.SetLimitsAsync(new { perTransfer = 7_000m, perDay = 10_000m, creditPerDay = 0m });

        using var move = await ctx.Client.ChangeUserAgencyAsync(ctx.SupervisorToken, ctx.Agent.Id, new { agencyId = ctx.Dkr });
        using var read = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/users/{ctx.Agent.Id}/limits");

        Assert.Equal(HttpStatusCode.NoContent, move.StatusCode);
        var limits = await read.ReadJsonAsync();
        Assert.Equal("XOF", limits.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Null, limits.GetProperty("perTransfer").ValueKind);
        Assert.Equal(JsonValueKind.Null, limits.GetProperty("perDay").ValueKind);
        Assert.Equal(JsonValueKind.Null, limits.GetProperty("creditPerDay").ValueKind);
    }

    [Fact]
    public async Task Change_is_written_to_the_audit_log()
    {
        var ctx = await ArrangeAsync();

        using var first = await ctx.SetLimitsAsync(new { perTransfer = 7_000m });
        using var same = await ctx.SetLimitsAsync(new { perTransfer = 7_000m });

        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
        await using var connection = await ctx.Factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Changes"::text FROM audit.audit_log WHERE "Action" = @action AND "EntityId" = @id""", connection);
        command.Parameters.AddWithValue("action", UserLimitsHandlers.AuditAction);
        command.Parameters.AddWithValue("id", ctx.Agent.Id);
        var changes = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                changes.Add(reader.GetString(0));
            }
        }

        using var json = JsonDocument.Parse(Assert.Single(changes));
        var change = Assert.Single(json.RootElement.EnumerateArray().ToList());
        Assert.Equal("perTransfer", change.GetProperty("field").GetString());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("before").ValueKind);
        Assert.Equal("7000", change.GetProperty("after").GetString());
    }

    // --- Outils ------------------------------------------------------------

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string code, decimal remaining, string currency)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.Equal(remaining, problem.GetProperty("remaining").GetDecimal());
        Assert.Equal(currency, problem.GetProperty("currency").GetString());
    }

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(supervisorToken, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        using var cash = await client.CreateTreasuryAccountAsync(
            supervisorToken, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = agent.AgencyId });
        Assert.Equal(HttpStatusCode.Created, cash.StatusCode);

        return new Context(
            factory, client, supervisorToken, agent, agentToken, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            (await cash.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        TestAccount Agent,
        string AgentToken,
        Guid Dkr,
        DateTimeOffset OperationAt,
        Guid Cash)
    {
        public Task<HttpResponseMessage> SetLimitsAsync(object limits) =>
            Client.PutJsonAsync(SupervisorToken, $"/api/v1/users/{Agent.Id}/limits", limits);

        /// <summary>Transfert Nouakchott → Dakar par l'agent : payé comptant en entier, ou en partie avec crédit.</summary>
        public Task<HttpResponseMessage> SendAsync(decimal amountXof, Guid? creditParty = null, decimal? payment = -1m)
        {
            var paid = payment == -1m ? amountXof * 3650m / 5000m : payment;
            var payments = paid is { } amount ? new[] { new { treasuryAccountId = Cash, amount } } : [];
            return Client.CreateTransferAsync(
                AgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments,
                    creditPartyId = creditParty,
                },
                Guid.NewGuid().ToString("N"));
        }

        public async Task<Guid> CreatePartyAsync()
        {
            using var response = await Client.CreatePartyAsync(
                SupervisorToken, new { name = $"Débiteur {Guid.NewGuid():N}"[..30], type = "Client" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }
    }
}
