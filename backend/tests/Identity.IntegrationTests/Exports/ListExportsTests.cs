using System.Globalization;
using System.Net;
using ClosedXML.Excel;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Exports;

/// <summary>
/// Exports Excel de la caisse, des tiers et du bénéfice (décision du 24 octobre 2026) : permission export en plus de la
/// lecture de l'écran ; mêmes filtres et même périmètre que les listes ; fichier relu avec ClosedXML. Chaque test travaille
/// un jour tiré au hasard (1975-1995) avec un marqueur unique pour ne lire que ses propres données.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListExportsTests(ApiFixture fixture)
{
    // --- Caisse ------------------------------------------------------------

    [Fact]
    public async Task Cash_export_lists_movements_with_their_type_direction_and_cancellation()
    {
        var ctx = await ArrangeAsync();
        await ctx.MovementAsync("Receipt", 120_000m);
        var cancelled = await ctx.MovementAsync("Payment", 45_000.5m);
        using var cancel = await ctx.Client.CancelCashMovementAsync(ctx.SupervisorToken, cancelled, "Saisi en double");
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        using var response = await ctx.Client.ExportAsync(
            ctx.SupervisorToken, "cash-movements", $"agencyId={ctx.Nkc}&from={ctx.DayText}&to={ctx.DayText}&search={ctx.Tag}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"Caisse-{ctx.DayCompact}-{ctx.DayCompact}.xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var workbook = await OpenAsync(response);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(3, sheet.LastRowUsed()!.RowNumber());

        var receipt = RowWhere(sheet, "Opération", "Encaissement");
        Assert.Equal("Entrée", Cell(sheet, receipt, "Sens").GetString());
        Assert.Equal(120_000d, Cell(sheet, receipt, "Montant").GetDouble());
        Assert.Equal("MRO", Cell(sheet, receipt, "Devise").GetString());
        Assert.Equal(ctx.PartyName, Cell(sheet, receipt, "Tiers").GetString());
        Assert.Equal("Actif", Cell(sheet, receipt, "Statut").GetString());

        var payment = RowWhere(sheet, "Opération", "Décaissement");
        Assert.Equal("Sortie", Cell(sheet, payment, "Sens").GetString());
        Assert.Equal(45_000.5d, Cell(sheet, payment, "Montant").GetDouble());
        Assert.Equal("Annulé", Cell(sheet, payment, "Statut").GetString());
        Assert.Equal("Saisi en double", Cell(sheet, payment, "Motif de l'annulation").GetString());
    }

    [Fact]
    public async Task Cash_export_needs_export_and_the_cash_reading()
    {
        var ctx = await ArrangeAsync();
        var query = $"from={ctx.DayText}&to={ctx.DayText}";

        using var byAgent = await ctx.Client.ExportAsync(ctx.AgentToken, "cash-movements", query);
        using var byReadOnly = await ctx.Client.ExportAsync(ctx.ReadOnlyToken, "cash-movements", query);

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byReadOnly.StatusCode);
    }

    // --- Tiers -------------------------------------------------------------

    [Fact]
    public async Task Party_export_gives_each_agency_balance_its_direction_and_limit_never_the_nni()
    {
        var ctx = await ArrangeAsync();
        using var opening = await ctx.Client.SetOpeningBalanceAsync(ctx.SupervisorToken, ctx.Party, ctx.Nkc, new { amount = 1_500m });
        Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);
        using var limit = await ctx.Client.SetCreditLimitAsync(ctx.SupervisorToken, ctx.Party, ctx.Nkc, 50_000m);
        Assert.Equal(HttpStatusCode.NoContent, limit.StatusCode);

        using var response = await ctx.Client.ExportAsync(ctx.SupervisorToken, "parties", $"search={ctx.Tag}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = await OpenAsync(response);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(ctx.PartyName, Cell(sheet, 2, "Nom").GetString());
        Assert.Equal("Client", Cell(sheet, 2, "Type").GetString());
        Assert.Equal(1_500d, Cell(sheet, 2, "Solde Nouakchott (MRO)").GetDouble());
        Assert.Equal("Il nous doit", Cell(sheet, 2, "Sens Nouakchott").GetString());
        Assert.Equal(50_000d, Cell(sheet, 2, "Plafond Nouakchott (MRO)").GetDouble());
        Assert.Equal("Soldé", Cell(sheet, 2, "Sens Dakar").GetString());
        Assert.True(Cell(sheet, 2, "Plafond Dakar (FCFA)").IsEmpty());
        Assert.DoesNotContain(sheet.CellsUsed(), c => c.GetString().Contains(ctx.PartyNni, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Party_export_needs_the_export_permission()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.ExportAsync(ctx.AgentToken, "parties", $"search={ctx.Tag}");
        using var byReadOnly = await ctx.Client.ExportAsync(ctx.ReadOnlyToken, "parties", $"search={ctx.Tag}");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byReadOnly.StatusCode);
    }

    // --- Bénéfice ----------------------------------------------------------

    [Fact]
    public async Task Profit_export_has_a_sheet_per_day_and_a_sheet_per_transfer()
    {
        var ctx = await ArrangeAsync();
        var reference = await ctx.TransferAsync();

        using var response = await ctx.Client.ExportAsync(
            ctx.SupervisorToken, "profit", $"agencyId={ctx.Nkc}&from={ctx.DayText}&to={ctx.DayText}&lang=ar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal($"Benefice-{ctx.DayCompact}-{ctx.DayCompact}.xlsx", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        using var workbook = await OpenAsync(response);
        Assert.Equal(["حسب اليوم", "حسب الحوالة"], workbook.Worksheets.Select(s => s.Name));
        Assert.All(workbook.Worksheets, s => Assert.True(s.RightToLeft));

        var days = workbook.Worksheet(1);
        Assert.Equal(ctx.Day.ToDateTime(TimeOnly.MinValue), Cell(days, 2, "اليوم").GetDateTime());
        Assert.True(Cell(days, 2, "الحوالات").GetDouble() >= 1);

        // Nouakchott reçoit des MRO et paie des FCFA : B = M − F × T / 5000 = 36 500 − 50 000 × 3 600 / 5 000 = 500.
        var transfers = workbook.Worksheet(2);
        var row = RowWhere(transfers, "المرجع", reference);
        Assert.Equal(500d, Cell(transfers, row, "الربح بالأوقية").GetDouble());
        Assert.Equal(3600d, Cell(transfers, row, "سعر الشراء").GetDouble());
        Assert.Equal("نواكشوط", Cell(transfers, row, "وكالة الإرسال").GetString());
    }

    [Fact]
    public async Task Profit_export_needs_export_and_profit_view()
    {
        var ctx = await ArrangeAsync();

        using var byReadOnly = await ctx.Client.ExportAsync(ctx.ReadOnlyToken, "profit", $"from={ctx.DayText}&to={ctx.DayText}");

        Assert.Equal(HttpStatusCode.Forbidden, byReadOnly.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Marqueur en lettres seulement : la recherche des tiers compare aussi les chiffres aux téléphones des autres tests.</summary>
    private static string Tag(string prefix) =>
        prefix + new string([.. Guid.NewGuid().ToString("N")[..8].Select(c => (char)('g' + Convert.ToInt32(c.ToString(), 16)))]);

    private static async Task<XLWorkbook> OpenAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
    }

    private static IXLCell Cell(IXLWorksheet sheet, int row, string header)
    {
        var column = sheet.Row(1).CellsUsed().Single(c => c.GetString() == header).Address.ColumnNumber;
        return sheet.Cell(row, column);
    }

    private static int RowWhere(IXLWorksheet sheet, string header, string value)
    {
        var column = sheet.Row(1).CellsUsed().Single(c => c.GetString() == header).Address.ColumnNumber;
        return sheet.Column(column).CellsUsed().Single(c => c.GetString() == value).Address.RowNumber;
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

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(token, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        async Task<string> LoginAsync(RoleTemplate role)
        {
            var account = await TestAccounts.CreateAsync(factory, role, agencyCode: "NKC");
            return (await client.LoginOkAsync(account.Email, account.Password)).AccessToken;
        }

        using var cash = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId = nkc });
        Assert.Equal(HttpStatusCode.Created, cash.StatusCode);

        var tag = Tag("Exp");
        var partyName = $"Tiers {tag}";
        var nni = "T" + Guid.NewGuid().ToString("N")[..12];
        using var party = await client.CreatePartyAsync(token, new { name = partyName, type = "Client", nni });
        Assert.Equal(HttpStatusCode.Created, party.StatusCode);

        return new Context(
            client, token, await LoginAsync(RoleTemplate.Agent), await LoginAsync(RoleTemplate.ReadOnly), nkc, dkr, day,
            (await cash.ReadJsonAsync()).GetProperty("id").GetGuid(), tag,
            (await party.ReadJsonAsync()).GetProperty("id").GetGuid(), partyName, nni);
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string AgentToken,
        string ReadOnlyToken,
        Guid Nkc,
        Guid Dkr,
        DateOnly Day,
        Guid Cash,
        string Tag,
        Guid Party,
        string PartyName,
        string PartyNni)
    {
        public string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public string DayCompact => Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        private DateTimeOffset OperationAt => new(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        public async Task<Guid> MovementAsync(string type, decimal amount)
        {
            using var response = await Client.CreateCashMovementAsync(
                AgentToken,
                new { type, treasuryAccountId = Cash, partyId = Party, amount, note = $"Mouvement {Tag}", operationAt = OperationAt },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<string> TransferAsync()
        {
            using var response = await Client.CreateTransferAsync(
                AgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = $"Expéditeur {Tag}",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = Cash, amount = 36_500m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("reference").GetString()!;
        }
    }
}
