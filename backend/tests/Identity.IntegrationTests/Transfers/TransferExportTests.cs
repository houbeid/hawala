using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Export Excel des transferts (décision du 24 octobre 2026) : permission export (Superviseur, Consultation) ; mêmes filtres,
/// même périmètre et mêmes champs masqués que la liste ; en-têtes en français ou en arabe (feuille de droite à gauche) ;
/// nombres et dates gardés comme tels ; texte saisi jamais lu comme une formule.
/// Chaque test crée ses transferts un jour tiré au hasard (1975-1995) avec un expéditeur marqué, et filtre sur les deux.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferExportTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_supervisor_exports_the_filtered_list_with_numbers_dates_and_profit()
    {
        var ctx = await ArrangeAsync();
        var reference = await ctx.CreateAsync($"{ctx.Tag} A");
        await ctx.CreateAsync($"{ctx.Tag} B");

        using var response = await ctx.ExportAsync(ctx.SupervisorToken, $"agencyId={ctx.Nkc}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        var day = ctx.Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        Assert.Equal($"Transferts-{day}-{day}.xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var workbook = await OpenAsync(response);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Transferts", sheet.Name);
        Assert.False(sheet.RightToLeft);
        var headers = Headers(sheet);
        Assert.Contains("Prix d'achat (MRO pour 5 000 FCFA)", headers);
        Assert.Contains("Bénéfice MRO", headers);
        Assert.Contains("Paiement du client", headers);
        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber());

        var row = RowOf(sheet, reference);
        Assert.Equal(50_000d, Cell(sheet, row, "Montant FCFA").GetDouble());
        Assert.Equal(36_500d, Cell(sheet, row, "Montant MRO").GetDouble());
        Assert.Equal(3650d, Cell(sheet, row, "Prix de vente (MRO pour 5 000 FCFA)").GetDouble());
        Assert.Equal(new DateTime(ctx.Day, new TimeOnly(10, 0)), Cell(sheet, row, "Date de l'opération").GetDateTime());
        Assert.Equal("Non livré", Cell(sheet, row, "Statut").GetString());
        Assert.Equal("Envoyé", Cell(sheet, row, "Sens").GetString());
        Assert.Equal("Nouakchott", Cell(sheet, row, "Agence d'envoi").GetString());
        Assert.Equal("Dakar", Cell(sheet, row, "Agence de destination").GetString());
        Assert.StartsWith("Caisse", Cell(sheet, row, "Paiement du client").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_only_exports_its_agency_without_purchase_rate_nor_profit()
    {
        var ctx = await ArrangeAsync();
        await ctx.CreateAsync($"{ctx.Tag} A");

        using var nkc = await ctx.ExportAsync(ctx.NkcReadOnlyToken, string.Empty);
        using var dkr = await ctx.ExportAsync(ctx.DkrReadOnlyToken, "direction=Sent");

        Assert.Equal(HttpStatusCode.OK, nkc.StatusCode);
        using var workbook = await OpenAsync(nkc);
        var headers = Headers(workbook.Worksheet(1));
        Assert.DoesNotContain("Prix d'achat (MRO pour 5 000 FCFA)", headers);
        Assert.DoesNotContain("Bénéfice MRO", headers);
        Assert.Equal(2, workbook.Worksheet(1).LastRowUsed()!.RowNumber());

        // Dakar a reçu ce transfert : il ne l'a pas envoyé, et ne voit ni la répartition ni le crédit.
        using var dakar = await OpenAsync(dkr);
        Assert.Equal(1, dakar.Worksheet(1).LastRowUsed()!.RowNumber());
        Assert.DoesNotContain("Paiement du client", Headers(dakar.Worksheet(1)));
    }

    [Fact]
    public async Task An_agent_cannot_export()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.ExportAsync(ctx.NkcAgentToken, string.Empty);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Arabic_headers_and_a_right_to_left_sheet()
    {
        var ctx = await ArrangeAsync();
        await ctx.CreateAsync($"{ctx.Tag} A");

        using var response = await ctx.ExportAsync(ctx.SupervisorToken, $"agencyId={ctx.Nkc}&lang=ar");

        using var workbook = await OpenAsync(response);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("الحوالات", sheet.Name);
        Assert.True(sheet.RightToLeft);
        Assert.Equal("المرجع", sheet.Cell(1, 1).GetString());
        Assert.Equal("غير مسلَّمة", Cell(sheet, 2, "الحالة").GetString());
        Assert.Equal("نواكشوط", Cell(sheet, 2, "وكالة الإرسال").GetString());
    }

    [Fact]
    public async Task Typed_text_that_looks_like_a_formula_stays_text()
    {
        var ctx = await ArrangeAsync();
        await ctx.CreateAsync($"{ctx.Tag} A", beneficiary: "=HYPERLINK(\"http://example.com\")");

        using var response = await ctx.ExportAsync(ctx.SupervisorToken, $"agencyId={ctx.Nkc}");

        using var workbook = await OpenAsync(response);
        var cell = Cell(workbook.Worksheet(1), 2, "Bénéficiaire");
        Assert.False(cell.HasFormula);
        Assert.Equal("=HYPERLINK(\"http://example.com\")", cell.GetString());
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<XLWorkbook> OpenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        return new XLWorkbook(stream);
    }

    private static List<string> Headers(IXLWorksheet sheet) =>
        [.. sheet.Row(1).CellsUsed().Select(c => c.GetString())];

    private static IXLCell Cell(IXLWorksheet sheet, int row, string header)
    {
        var column = sheet.Row(1).CellsUsed().Single(c => c.GetString() == header).Address.ColumnNumber;
        return sheet.Cell(row, column);
    }

    private static int RowOf(IXLWorksheet sheet, string reference) =>
        sheet.Column(1).CellsUsed().Single(c => c.GetString() == reference).Address.RowNumber;

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

        async Task<string> LoginAsync(RoleTemplate role, string agency)
        {
            var account = await TestAccounts.CreateAsync(factory, role, agencyCode: agency);
            return (await client.LoginOkAsync(account.Email, account.Password)).AccessToken;
        }

        using var cash = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = nkc });
        Assert.Equal(HttpStatusCode.Created, cash.StatusCode);

        return new Context(
            client, token,
            await LoginAsync(RoleTemplate.Agent, "NKC"),
            await LoginAsync(RoleTemplate.ReadOnly, "NKC"),
            await LoginAsync(RoleTemplate.ReadOnly, "DKR"),
            nkc, dkr, day, (await cash.ReadJsonAsync()).GetProperty("id").GetGuid(),
            $"Export{Guid.NewGuid():N}"[..14]);
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string NkcReadOnlyToken,
        string DkrReadOnlyToken,
        Guid Nkc,
        Guid Dkr,
        DateOnly Day,
        Guid NkcCash,
        string Tag)
    {
        public async Task<string> CreateAsync(string sender, string beneficiary = "Bénéficiaire fictif")
        {
            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = sender,
                    beneficiaryInfo = beneficiary,
                    operationAt = new DateTimeOffset(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 36_500m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("reference").GetString()!;
        }

        /// <param name="query">Filtres en plus du jour et de l'expéditeur marqué, déjà encodés (« agencyId=…&amp;lang=ar »).</param>
        public Task<HttpResponseMessage> ExportAsync(string token, string query)
        {
            var day = Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Client.ExportTransfersAsync(token, $"from={day}&to={day}&search={Tag}" + (query.Length > 0 ? $"&{query}" : string.Empty));
        }
    }
}
