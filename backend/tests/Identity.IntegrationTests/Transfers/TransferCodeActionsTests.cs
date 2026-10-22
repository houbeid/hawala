using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Transfers.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Actions sur le code de retrait (validées le 21 octobre 2026) : débloquer et livrer sans code (Superviseur, transfers.manage_code),
/// nouveau code (agence d'envoi ou Superviseur ; ne débloque pas ; l'ancien ne marche plus).
/// Transfert de référence : Nouakchott → Dakar, 50 000 FCFA ; chaque test fixe le taux d'une date tirée au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferCodeActionsTests(ApiFixture fixture)
{
    // --- Débloquer ---------------------------------------------------------

    [Fact]
    public async Task Supervisor_unlocks_a_locked_transfer_which_can_then_be_delivered_with_its_code()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();
        await ctx.LockAsync(id, code);

        using var unlock = await ctx.Client.UnlockTransferCodeAsync(ctx.SupervisorToken, id, "Bénéficiaire vérifié");

        Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);
        Assert.False((await ctx.GetAsync(id)).GetProperty("isCodeLocked").GetBoolean());
        using var deliver = await ctx.DeliverAsync(id, code);
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
    }

    [Fact]
    public async Task Unlocking_needs_a_locked_transfer_and_the_supervisor()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();

        using var notLocked = await ctx.Client.UnlockTransferCodeAsync(ctx.SupervisorToken, id);
        using var byAgent = await ctx.Client.UnlockTransferCodeAsync(ctx.DkrAgentToken, id);

        Assert.Equal(HttpStatusCode.Conflict, notLocked.StatusCode);
        Assert.Equal("Transfers.Code.NotLocked", await notLocked.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
    }

    // --- Livrer sans code --------------------------------------------------

    [Fact]
    public async Task Supervisor_delivers_a_locked_transfer_without_code_with_id_document_and_reason()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();
        await ctx.LockAsync(id, code);

        using var response = await ctx.Client.DeliverWithoutCodeAsync(ctx.SupervisorToken, id, new
        {
            idDocument = "CNI 0000000",
            reason = "Code perdu, identité vérifiée",
            operationAt = ctx.OperationAt.AddHours(1),
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50_000m, (await response.ReadJsonAsync()).GetProperty("amountDelivered").GetDecimal());
        Assert.Equal(-50_000m, await ctx.TreasuryBalanceAsync(ctx.DkrCash));

        var view = await ctx.GetAsync(id);
        Assert.Equal("Delivered", view.GetProperty("status").GetString());
        Assert.True(view.GetProperty("deliveredWithoutCode").GetBoolean());
        Assert.False(view.TryGetProperty("beneficiaryIdDocument", out _));

        await using var scope = ctx.Factory.Services.CreateAsyncScope();
        var transfer = await scope.ServiceProvider.GetRequiredService<TransfersDbContext>()
            .Transfers.IgnoreQueryFilters().SingleAsync(t => t.Id == id);
        Assert.Equal("CNI 0000000", transfer.BeneficiaryIdDocument);
        Assert.Equal("Code perdu, identité vérifiée", transfer.DeliveryWithoutCodeReason);
    }

    [Fact]
    public async Task Delivering_without_code_needs_the_id_document_the_reason_and_the_supervisor()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();
        var payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } };

        using var noId = await ctx.Client.DeliverWithoutCodeAsync(
            ctx.SupervisorToken, id, new { reason = "Code perdu", operationAt = ctx.OperationAt.AddHours(1), payments });
        using var noReason = await ctx.Client.DeliverWithoutCodeAsync(
            ctx.SupervisorToken, id, new { idDocument = "CNI 0000000", operationAt = ctx.OperationAt.AddHours(1), payments });
        using var byAgent = await ctx.Client.DeliverWithoutCodeAsync(
            ctx.DkrAgentToken, id, new { idDocument = "CNI 0000000", reason = "Code perdu", payments });

        Assert.Equal("Transfers.Code.IdDocumentRequired", await noId.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Code.ReasonRequired", await noReason.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("NotDelivered", (await ctx.GetAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_id_document_is_encrypted_in_the_database()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();

        using var response = await ctx.Client.DeliverWithoutCodeAsync(ctx.SupervisorToken, id, new
        {
            idDocument = "CNI 1234567",
            reason = "Code perdu, identité vérifiée",
            operationAt = ctx.OperationAt.AddHours(1),
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } },
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var connection = await ctx.Factory.Services.GetRequiredService<Npgsql.NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            """SELECT "BeneficiaryIdDocument" FROM transfers.transfers WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", id);
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.StartsWith("v1:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("1234567", stored, StringComparison.Ordinal);
    }

    // --- Nouveau code ------------------------------------------------------

    [Fact]
    public async Task Sending_agency_gives_a_new_code_and_the_old_one_stops_working()
    {
        var ctx = await ArrangeAsync();
        var (id, oldCode) = await ctx.CreateAsync();

        using var response = await ctx.Client.NewTransferCodeAsync(ctx.NkcAgentToken, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newCode = (await response.ReadJsonAsync()).GetProperty("withdrawalCode").GetString()!;
        Assert.Matches("^[0-9]{6}$", newCode);

        if (newCode != oldCode)
        {
            using var withOld = await ctx.DeliverAsync(id, oldCode);
            Assert.Equal("Transfers.Delivery.WrongCode", await withOld.ReadErrorCodeAsync());
        }

        using var withNew = await ctx.DeliverAsync(id, newCode);
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);
    }

    [Fact]
    public async Task New_code_is_refused_to_the_destination_agency_and_on_a_locked_transfer()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();

        using var byDestination = await ctx.Client.NewTransferCodeAsync(ctx.DkrAgentToken, id);
        Assert.Equal(HttpStatusCode.Forbidden, byDestination.StatusCode);
        Assert.Equal("Transfers.Code.NotSendingAgency", await byDestination.ReadErrorCodeAsync());

        await ctx.LockAsync(id, code);
        using var onLocked = await ctx.Client.NewTransferCodeAsync(ctx.SupervisorToken, id);
        Assert.Equal(HttpStatusCode.Conflict, onLocked.StatusCode);
        Assert.Equal("Transfers.Delivery.CodeLocked", await onLocked.ReadErrorCodeAsync());
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

        return new Context(
            factory, client, token, nkcToken, dkrToken, nkc, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
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
        DateTimeOffset OperationAt,
        Guid NkcCash,
        Guid DkrCash)
    {
        public async Task<(Guid Id, string Code)> CreateAsync()
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
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 36_500m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("withdrawalCode").GetString()!);
        }

        public Task<HttpResponseMessage> DeliverAsync(Guid id, string code) =>
            Client.DeliverTransferAsync(DkrAgentToken, id, new
            {
                code,
                operationAt = OperationAt.AddHours(1),
                payments = new[] { new { treasuryAccountId = DkrCash, amount = 50_000m } },
            });

        /// <summary>Cinq codes faux : le transfert est bloqué.</summary>
        public async Task LockAsync(Guid id, string code)
        {
            for (var i = 0; i < 5; i++)
            {
                using var response = await DeliverAsync(id, WrongCode(code));
            }

            Assert.True((await GetAsync(id)).GetProperty("isCodeLocked").GetBoolean());
        }

        public async Task<JsonElement> GetAsync(Guid id)
        {
            using var response = await Client.GetTransferAsync(SupervisorToken, id);
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
