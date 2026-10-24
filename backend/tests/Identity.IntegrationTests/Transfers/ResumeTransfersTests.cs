using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClosedXML.Excel;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Reprise des transferts non livrés de l'ancienne app (phase 8, décisions du 24 octobre 2026) : 2e modèle Excel ; vérifier
/// (rien n'est créé) puis importer (tout ou rien) ; « Non livré », historique « Repris », référence et date d'origine,
/// caisses non touchées ; livrable avec un nouveau code ; exclu du bénéfice et du tableau de bord ; montant non
/// modifiable. Chaque test prend un jour tiré au hasard (1975-1995) et des références uniques. Données fictives.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ResumeTransfersTests(ApiFixture fixture)
{
    private static readonly string[] Header =
    [
        "Référence", "Date (jj/mm/aaaa hh:mm)", "Agence d'envoi (NKC ou DKR)", "Montant FCFA", "Montant MRO",
        "Prix de vente (MRO pour 5 000 FCFA)", "Expéditeur", "Bénéficiaire", "Note",
    ];

    [Fact]
    public async Task The_template_has_the_expected_columns()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/setup/transfers/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        Assert.Equal(Header, workbook.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()));
    }

    [Fact]
    public async Task Checking_reports_each_problem_and_creates_nothing()
    {
        var ctx = await ArrangeAsync();
        var future = DateTime.UtcNow.AddDays(3).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var file = Workbook(
            [ctx.Ref("A"), ctx.DayText, "NKC", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", ""],
            [ctx.Ref("A"), ctx.DayText, "Dakar", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", ""],
            [ctx.Ref("B"), ctx.DayText, "Rosso", "50000,5", "36500", "", "Expéditeur fictif", "", ""],
            [ctx.Ref("C"), future, "NKC", "50000", "36500", "3650", "Expéditeur fictif", "Bénéficiaire fictif", ""]);

        var report = await ctx.CheckAsync(file);

        Assert.Equal(4, report.GetProperty("rowCount").GetInt32());
        Assert.Equal(1, report.GetProperty("validCount").GetInt32());
        var issues = report.GetProperty("issues").EnumerateArray()
            .Select(i => (i.GetProperty("row").GetInt32(), i.GetProperty("column").GetString(), i.GetProperty("code").GetString()))
            .ToList();
        Assert.Contains((3, "reference", "Transfers.Resume.ReferenceDuplicateInFile"), issues);
        Assert.Contains((4, "sourceAgency", "Transfers.Resume.AgencyUnknown"), issues);
        Assert.Contains((4, "amountXof", "Transfers.Resume.AmountInvalid"), issues);
        Assert.Contains((5, "operationAt", "Transfers.Transfer.FutureDate"), issues);
        // Nouakchott → Dakar : à livrer en FCFA à Dakar.
        Assert.Equal(50_000m, report.GetProperty("totals").GetProperty("DKR").GetDecimal());
        Assert.Null(await ctx.FindAsync(ctx.Ref("A")));
    }

    [Fact]
    public async Task A_reference_already_in_the_app_is_refused()
    {
        var ctx = await ArrangeAsync();
        var first = Workbook([ctx.Ref("A"), ctx.DayText, "NKC", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", ""]);
        using var imported = await ctx.ImportAsync(first);
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);

        var report = await ctx.CheckAsync(first);

        var issue = Assert.Single(report.GetProperty("issues").EnumerateArray());
        Assert.Equal("Transfers.Resume.ReferenceTaken", issue.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_imported_transfer_is_not_delivered_resumed_and_leaves_the_cash_untouched()
    {
        var ctx = await ArrangeAsync();
        var file = Workbook([ctx.Ref("A"), $"{ctx.DayText} 14:30", "NKC", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", "Ancienne app"]);

        using var response = await ctx.ImportAsync(file);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, (await response.ReadJsonAsync()).GetProperty("created").GetInt32());
        var transfer = (await ctx.FindAsync(ctx.Ref("A")))!.Value;
        Assert.Equal("NotDelivered", transfer.GetProperty("status").GetString());
        Assert.True(transfer.GetProperty("isResumed").GetBoolean());
        Assert.Equal(36_500m, transfer.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3_650m, transfer.GetProperty("saleRate").GetDecimal());
        Assert.Equal(new DateTimeOffset(ctx.Day.ToDateTime(new TimeOnly(14, 30)), TimeSpan.Zero), transfer.GetProperty("operationAt").GetDateTimeOffset());
        Assert.Empty(transfer.GetProperty("payments").EnumerateArray());
        Assert.Equal(0m, await ctx.CashBalanceAsync(ctx.NkcCash));

        using var history = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/transfers/{transfer.GetProperty("id").GetGuid()}/history");
        var events = (await history.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Equal("Resumed", Assert.Single(events).GetProperty("type").GetString());
    }

    [Fact]
    public async Task It_is_delivered_with_a_new_code_and_counts_neither_in_profit_nor_in_the_dashboard()
    {
        var ctx = await ArrangeAsync();
        using var imported = await ctx.ImportAsync(
            Workbook([ctx.Ref("A"), $"{ctx.DayText} 10:00", "NKC", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", ""]));
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var id = (await ctx.FindAsync(ctx.Ref("A")))!.Value.GetProperty("id").GetGuid();

        using var renewed = await ctx.Client.NewTransferCodeAsync(ctx.NkcAgentToken, id);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var code = (await renewed.ReadJsonAsync()).GetProperty("withdrawalCode").GetString();
        using var delivered = await ctx.Client.DeliverTransferAsync(ctx.DkrAgentToken, id, new
        {
            code,
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 50_000m } },
        });
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        Assert.Equal(-50_000m, await ctx.CashBalanceAsync(ctx.DkrCash));

        var day = ctx.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var profit = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/profit?agencyId={ctx.Nkc}&from={day}&to={day}");
        Assert.Equal(0, (await profit.ReadJsonAsync()).GetProperty("transferCount").GetInt32());
        using var dashboard = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/transfers/dashboard?from={day}&to={day}");
        var nkc = (await dashboard.ReadJsonAsync()).GetProperty("caisses").EnumerateArray().Single(c => c.GetProperty("agencyId").GetGuid() == ctx.Nkc);
        Assert.Equal(0, nkc.GetProperty("sent").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Its_amount_cannot_be_edited_and_only_the_supervisor_imports()
    {
        var ctx = await ArrangeAsync();
        using var imported = await ctx.ImportAsync(
            Workbook([ctx.Ref("A"), ctx.DayText, "NKC", "50000", "36500", "", "Expéditeur fictif", "Bénéficiaire fictif", ""]));
        var transfer = (await ctx.FindAsync(ctx.Ref("A")))!.Value;

        using var edit = await ctx.Client.EditTransferAmountsAsync(ctx.SupervisorToken, transfer.GetProperty("id").GetGuid(), new
        {
            version = transfer.GetProperty("version").GetInt32(),
            amountXof = 60_000m,
            payments = Array.Empty<object>(),
            reason = "Correction",
        });
        using var byAgent = await ctx.Client.SendAsync(Upload("/api/v1/setup/transfers/check", ctx.NkcAgentToken, Workbook()));

        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.Equal("Transfers.Transfer.ResumedAmountsLocked", await edit.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static byte[] Workbook(params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Non livrés");
        for (var c = 0; c < Header.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Header[c];
        }

        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                if (rows[r][c].Length > 0)
                {
                    sheet.Cell(r + 2, c + 1).Value = rows[r][c];
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static HttpRequestMessage Upload(string url, string token, byte[] file)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(part, "file", "reprise.xlsx");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
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

        async Task<string> LoginAsync(string agency)
        {
            var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: agency);
            return (await client.LoginOkAsync(agent.Email, agent.Password)).AccessToken;
        }

        async Task<Guid> CashAsync(Guid agencyId)
        {
            using var response = await client.CreateTreasuryAccountAsync(
                token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        return new Context(
            client, token, await LoginAsync("NKC"), await LoginAsync("DKR"), nkc, await CashAsync(nkc), await CashAsync(dkr),
            new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500)), $"OLD-{Guid.NewGuid():N}"[..16]);
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Nkc,
        Guid NkcCash,
        Guid DkrCash,
        DateOnly Day,
        string Prefix)
    {
        public string DayText => Day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public string Ref(string suffix) => $"{Prefix}-{suffix}";

        public async Task<JsonElement> CheckAsync(byte[] file)
        {
            using var response = await Client.SendAsync(Upload("/api/v1/setup/transfers/check", SupervisorToken, file));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return await response.ReadJsonAsync();
        }

        public Task<HttpResponseMessage> ImportAsync(byte[] file) =>
            Client.SendAsync(Upload("/api/v1/setup/transfers/import", SupervisorToken, file));

        public async Task<JsonElement?> FindAsync(string reference)
        {
            var day = Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/transfers?from={day}&to={day}&search={reference}");
            var items = (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToList();
            return items.Count == 0 ? null : items.Single();
        }

        public async Task<decimal> CashBalanceAsync(Guid accountId)
        {
            using var response = await Client.GetTreasuryAccountAsync(SupervisorToken, accountId);
            return (await response.ReadJsonAsync()).GetProperty("balance").GetDecimal();
        }
    }
}
