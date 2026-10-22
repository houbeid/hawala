using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Historique, version et modification des textes (validés le 21 octobre 2026) : chaque action écrit une ligne ;
/// les textes d'un non livré se modifient par l'agence d'envoi ou le Superviseur, avec la version lue.
/// Transfert de référence : Nouakchott → Dakar, 50 000 FCFA ; taux fixé sur une date tirée au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferHistoryTests(ApiFixture fixture)
{
    [Fact]
    public async Task History_tells_the_whole_life_of_a_transfer_to_both_agencies()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();

        using (var wrong = await ctx.DeliverAsync(id, WrongCode(code)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using var newCode = await ctx.Client.NewTransferCodeAsync(ctx.NkcAgentToken, id);
        var code2 = (await newCode.ReadJsonAsync()).GetProperty("withdrawalCode").GetString()!;
        using var edit = await ctx.EditAsync(ctx.NkcAgentToken, id, version: 3, beneficiary: "Autre bénéficiaire", reason: "Faute de frappe");
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        using var deliver = await ctx.DeliverAsync(id, code2);
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);

        foreach (var token in new[] { ctx.NkcAgentToken, ctx.DkrAgentToken, ctx.SupervisorToken })
        {
            var history = await ctx.HistoryAsync(token, id);
            Assert.Equal(
                ["Created", "WrongCode", "CodeReplaced", "Edited", "Delivered"],
                history.Select(e => e.GetProperty("type").GetString()));
            Assert.Equal(ctx.NkcAgentId, history[0].GetProperty("authorId").GetGuid());
            Assert.Equal(ctx.DkrAgentId, history[4].GetProperty("authorId").GetGuid());
        }

        Assert.Equal(5, (await ctx.GetAsync(id)).GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task History_gives_the_author_name_even_to_an_agent_who_cannot_read_the_accounts()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();

        // L'agent de Dakar n'a pas users.manage : le nom lui vient de la route de l'historique (22 octobre 2026).
        var created = (await ctx.HistoryAsync(ctx.DkrAgentToken, id)).Single();

        Assert.Equal(ctx.NkcAgentId, created.GetProperty("authorId").GetGuid());
        Assert.Equal("Agent Test", created.GetProperty("authorName").GetString());
    }

    [Fact]
    public async Task Sending_agency_edits_the_texts_and_the_old_values_stay_in_the_history()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();
        Assert.Equal(1, (await ctx.GetAsync(id)).GetProperty("version").GetInt32());

        using var response = await ctx.EditAsync(ctx.NkcAgentToken, id, version: 1, beneficiary: " Autre bénéficiaire ", reason: "Faute de frappe");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var view = await ctx.GetAsync(id);
        Assert.Equal("Autre bénéficiaire", view.GetProperty("beneficiaryInfo").GetString());
        Assert.Equal(JsonValueKind.Null, view.GetProperty("note").ValueKind);
        Assert.Equal(2, view.GetProperty("version").GetInt32());

        var edited = (await ctx.HistoryAsync(ctx.SupervisorToken, id)).Last();
        Assert.Equal("Edited", edited.GetProperty("type").GetString());
        Assert.Equal("Faute de frappe", edited.GetProperty("reason").GetString());
        var changes = edited.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal("BeneficiaryInfo", changes[0].GetProperty("field").GetString());
        Assert.Equal("Bénéficiaire fictif", changes[0].GetProperty("before").GetString());
        Assert.Equal("Autre bénéficiaire", changes[0].GetProperty("after").GetString());
        Assert.Equal("Note", changes[1].GetProperty("field").GetString());
    }

    [Fact]
    public async Task A_stale_or_missing_version_is_refused()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();
        using var first = await ctx.EditAsync(ctx.NkcAgentToken, id, version: 1, beneficiary: "Premier");

        using var stale = await ctx.EditAsync(ctx.SupervisorToken, id, version: 1, beneficiary: "Second");
        using var missing = await ctx.EditAsync(ctx.SupervisorToken, id, version: null, beneficiary: "Second");

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Transfers.Transfer.VersionConflict", await stale.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Transfer.VersionRequired", await missing.ReadErrorCodeAsync());
        Assert.Equal("Premier", (await ctx.GetAsync(id)).GetProperty("beneficiaryInfo").GetString());
    }

    [Fact]
    public async Task Only_the_sending_agency_or_the_supervisor_edits_an_undelivered_transfer()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();

        using var byDestination = await ctx.EditAsync(ctx.DkrAgentToken, id, version: 1, beneficiary: "Autre");
        using var bySupervisor = await ctx.EditAsync(ctx.SupervisorToken, id, version: 1, beneficiary: "Par le Superviseur");
        using var deliver = await ctx.DeliverAsync(id, code);
        using var afterDelivery = await ctx.EditAsync(ctx.NkcAgentToken, id, version: 3, beneficiary: "Trop tard");
        using var unknown = await ctx.EditAsync(ctx.SupervisorToken, Guid.NewGuid(), version: 1, beneficiary: "Autre");

        Assert.Equal(HttpStatusCode.Forbidden, byDestination.StatusCode);
        Assert.Equal("Transfers.Transfer.NotSendingAgency", await byDestination.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, bySupervisor.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, afterDelivery.StatusCode);
        Assert.Equal("Transfers.Transfer.NotEditable", await afterDelivery.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task History_of_an_unknown_transfer_is_not_found()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, $"/api/v1/transfers/{Guid.NewGuid()}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            client, token, nkcToken, nkcAgent.Id, dkrToken, dkrAgent.Id, dkr,
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
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        Guid NkcAgentId,
        string DkrAgentToken,
        Guid DkrAgentId,
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
                    note = "Note fictive",
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

        /// <summary>Garde l'expéditeur, change le bénéficiaire, retire la note.</summary>
        public Task<HttpResponseMessage> EditAsync(string token, Guid id, int? version, string beneficiary, string? reason = null) =>
            Client.EditTransferTextsAsync(token, id, new
            {
                version,
                senderInfo = "Expéditeur fictif",
                beneficiaryInfo = beneficiary,
                note = (string?)null,
                reason,
            });

        public async Task<JsonElement> GetAsync(Guid id)
        {
            using var response = await Client.GetTransferAsync(SupervisorToken, id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public async Task<IReadOnlyList<JsonElement>> HistoryAsync(string token, Guid id)
        {
            using var response = await Client.SendGetAsync(token, $"/api/v1/transfers/{id}/history");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).EnumerateArray()];
        }
    }
}
