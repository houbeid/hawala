using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using ClosedXML.Excel;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Import des tiers à la mise en service (phase 8, décisions du 24 octobre 2026) : modèle Excel ; « vérifier » rend un rapport
/// ligne par ligne sans rien créer ; « importer » crée tout ou rien (tiers, soldes initiaux par écriture d'ouverture à la
/// date de mise en service, plafonds, hors bilan) ; Superviseur seulement. Chaque test marque ses tiers d'un nom unique
/// et de téléphones tirés au hasard. Données fictives uniquement.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PartyImportTests(ApiFixture fixture)
{
    private static readonly string[] Header =
    [
        "Nom", "Type", "Téléphone", "NNI", "Adresse", "Note", "Dans le bilan",
        "Solde Dakar (FCFA)", "Sens Dakar", "Plafond Dakar (FCFA)",
        "Solde Nouakchott (MRO)", "Sens Nouakchott", "Plafond Nouakchott (MRO)",
    ];

    [Fact]
    public async Task The_template_has_the_expected_columns_and_a_help_sheet()
    {
        var ctx = await ArrangeAsync();

        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/setup/parties/template");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var headers = workbook.Worksheet(1).Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        Assert.Equal(Header, headers);
        Assert.Equal("Aide", workbook.Worksheet(2).Name);
    }

    [Fact]
    public async Task Checking_reports_each_problem_by_row_and_column_and_creates_nothing()
    {
        var ctx = await ArrangeAsync();
        var phone = ctx.Phone();
        var file = Workbook(
            [$"{ctx.Tag} Bon", "Client", phone, "", "", "", "", "", "", "", "1500", "Il nous doit", ""],
            [$"{ctx.Tag} Type", "Banque", "", "", "", "", "", "", "", "", "", "", ""],
            [$"{ctx.Tag} Doublon", "Fournisseur", phone, "", "", "", "Peut-être", "100,5", "On lui doit", "", "", "", ""],
            ["", "", "", "", "", "", "", "", "", "", "", "", ""],
            ["", "Autre", "", "", "", "", "", "", "", "", "200", "", ""]);

        var report = await ctx.CheckAsync(file);

        Assert.Equal(4, report.GetProperty("rowCount").GetInt32());
        Assert.Equal(1, report.GetProperty("validCount").GetInt32());
        var issues = report.GetProperty("issues").EnumerateArray()
            .Select(i => (i.GetProperty("row").GetInt32(), i.GetProperty("column").GetString(), i.GetProperty("code").GetString()))
            .ToList();
        Assert.Contains((3, "type", "Parties.Import.TypeUnknown"), issues);
        Assert.Contains((4, "phone", "Parties.Import.PhoneDuplicateInFile"), issues);
        Assert.Contains((4, "inBalanceSheet", "Parties.Import.YesNoUnknown"), issues);
        Assert.Contains((4, "DKR.balance", "Parties.Import.AmountTooPrecise"), issues);
        Assert.Contains((6, "name", "Parties.Party.NameEmpty"), issues);
        Assert.Contains((6, "NKC.direction", "Parties.Import.DirectionRequired"), issues);
        Assert.Equal(1_500m, report.GetProperty("totals").GetProperty("NKC").GetDecimal());
        Assert.Equal(0, await ctx.CountAsync());
    }

    [Fact]
    public async Task Importing_creates_parties_with_their_opening_balances_limits_and_balance_sheet_flag()
    {
        var ctx = await ArrangeAsync();
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var file = Workbook(
            [$"{ctx.Tag} Boutique", "Client", ctx.Phone(), "", "Marché fictif", "Ancienne app", "Oui", "20000", "On lui doit", "", "1500,50", "Il nous doit", "50000"],
            [$"{ctx.Tag} Bailleur", "Fournisseur", "", "", "", "", "Non", "", "", "0", "", "", ""]);

        using var response = await ctx.ImportAsync(file, day);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.ReadJsonAsync()).GetProperty("created").GetInt32());

        var shop = await ctx.FindAsync($"{ctx.Tag} Boutique");
        Assert.Equal("Client", shop.GetProperty("type").GetString());
        Assert.True(shop.GetProperty("inBalanceSheet").GetBoolean());
        var nkc = Balance(shop, ctx.Nkc);
        Assert.Equal(1_500.5m, nkc.GetProperty("balance").GetDecimal());
        Assert.Equal(1_500.5m, nkc.GetProperty("openingBalance").GetDecimal());
        Assert.Equal(50_000m, nkc.GetProperty("creditLimit").GetDecimal());
        var dkr = Balance(shop, ctx.Dkr);
        Assert.Equal(-20_000m, dkr.GetProperty("balance").GetDecimal());
        Assert.Equal(JsonValueKind.Null, dkr.GetProperty("creditLimit").ValueKind);

        var landlord = await ctx.FindAsync($"{ctx.Tag} Bailleur");
        Assert.Equal("Supplier", landlord.GetProperty("type").GetString());
        Assert.False(landlord.GetProperty("inBalanceSheet").GetBoolean());
        Assert.Equal(0m, Balance(landlord, ctx.Dkr).GetProperty("creditLimit").GetDecimal());

        // Solde initial écrit à la date de mise en service.
        var id = shop.GetProperty("id").GetGuid();
        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var statement = await ctx.Client.GetPartyStatementAsync(ctx.SupervisorToken, id, $"agencyId={ctx.Nkc}&from={dayText}&to={dayText}");
        var line = Assert.Single((await statement.ReadJsonAsync()).GetProperty("lines").EnumerateArray());
        Assert.Equal("Parties.OpeningBalance", line.GetProperty("entryType").GetString());
    }

    [Fact]
    public async Task One_bad_row_and_nothing_is_imported()
    {
        var ctx = await ArrangeAsync();
        var file = Workbook(
            [$"{ctx.Tag} Bon", "Client", "", "", "", "", "", "", "", "", "100", "Il nous doit", ""],
            [$"{ctx.Tag} Mauvais", "Client", "", "", "", "", "", "", "", "", "100", "Plus tard", ""]);

        using var response = await ctx.ImportAsync(file, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.Equal("Parties.Import.Invalid", body.GetProperty("code").GetString());
        var issue = Assert.Single(body.GetProperty("report").GetProperty("issues").EnumerateArray());
        Assert.Equal("Parties.Import.DirectionUnknown", issue.GetProperty("code").GetString());
        Assert.Equal(0, await ctx.CountAsync());
    }

    [Fact]
    public async Task A_phone_already_used_by_an_existing_party_is_reported()
    {
        var ctx = await ArrangeAsync();
        var phone = ctx.Phone();
        using var existing = await ctx.Client.CreatePartyAsync(ctx.SupervisorToken, new { name = $"{ctx.Tag} Existant", type = "Client", phone });
        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);

        var report = await ctx.CheckAsync(Workbook([$"{ctx.Tag} Nouveau", "Client", phone, "", "", "", "", "", "", "", "", "", ""]));

        var issue = Assert.Single(report.GetProperty("issues").EnumerateArray());
        Assert.Equal("Parties.Party.PhoneTaken", issue.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Arabic_headers_and_words_are_understood()
    {
        var ctx = await ArrangeAsync();
        string[] arabic =
        [
            "الاسم", "النوع", "الهاتف", "رقم التعريف", "العنوان", "ملاحظة", "ضمن الميزانية",
            "الرصيد داكار (FCFA)", "الاتجاه داكار", "سقف الآجل داكار (FCFA)",
            "الرصيد نواكشوط (MRO)", "الاتجاه نواكشوط", "سقف الآجل نواكشوط (MRO)",
        ];
        var file = WorkbookWithHeader(arabic, [$"{ctx.Tag} عربي", "مورد", "", "", "", "", "لا", "", "", "", "300", "له", ""]);

        var report = await ctx.CheckAsync(file);

        Assert.Equal(1, report.GetProperty("validCount").GetInt32());
        Assert.Equal(-300m, report.GetProperty("totals").GetProperty("NKC").GetDecimal());
    }

    [Fact]
    public async Task Only_the_supervisor_imports_and_an_unreadable_file_is_refused()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.SendAsync(Upload("/api/v1/setup/parties/check", ctx.AgentToken, Workbook()));
        using var notExcel = await ctx.Client.SendAsync(Upload("/api/v1/setup/parties/check", ctx.SupervisorToken, "pas un fichier Excel"u8.ToArray()));

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Parties.Import.FileUnreadable", await notExcel.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Marqueur en lettres seulement : la recherche des tiers compare aussi les chiffres aux téléphones des autres tests.</summary>
    private static string Tag(string prefix) =>
        prefix + new string([.. Guid.NewGuid().ToString("N")[..8].Select(c => (char)('g' + Convert.ToInt32(c.ToString(), 16)))]);

    private static byte[] Workbook(params string[][] rows) => WorkbookWithHeader(Header, rows);

    private static byte[] WorkbookWithHeader(string[] header, params string[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Tiers");
        for (var c = 0; c < header.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = header[c];
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

    private static HttpRequestMessage Upload(string url, string token, byte[] file, DateOnly? operationDate = null)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(part, "file", "tiers.xlsx");
        if (operationDate is { } day)
        {
            content.Add(new StringContent(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), "operationDate");
        }

        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static JsonElement Balance(JsonElement party, Guid agencyId) =>
        party.GetProperty("balances").EnumerateArray().Single(b => b.GetProperty("agencyId").GetGuid() == agencyId);

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        var agent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return new Context(client, token, agentToken, nkc, dkr, Tag("Imp"));
    }

    private sealed record Context(HttpClient Client, string SupervisorToken, string AgentToken, Guid Nkc, Guid Dkr, string Tag)
    {
        /// <summary>Numéro mauritanien fictif tiré au hasard (8 chiffres après +222).</summary>
        public string Phone() => $"+2222{Random.Shared.Next(0, 10_000_000):D7}";

        public async Task<JsonElement> CheckAsync(byte[] file)
        {
            using var response = await Client.SendAsync(Upload("/api/v1/setup/parties/check", SupervisorToken, file));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        public Task<HttpResponseMessage> ImportAsync(byte[] file, DateOnly? operationDate) =>
            Client.SendAsync(Upload("/api/v1/setup/parties/import", SupervisorToken, file, operationDate));

        public async Task<int> CountAsync()
        {
            using var response = await Client.ListPartiesAsync(SupervisorToken, $"search={Tag}");
            return (await response.ReadJsonAsync()).GetProperty("count").GetInt32();
        }

        public async Task<JsonElement> FindAsync(string name)
        {
            using var response = await Client.ListPartiesAsync(SupervisorToken, $"search={Uri.EscapeDataString(name)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("name").GetString() == name);
        }
    }
}
