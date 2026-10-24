using System.Globalization;
using System.Security.Claims;
using ClosedXML.Excel;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Organization.Contracts;
using Parties.Application.Features.Import;

namespace Hawala.Api.Endpoints.Setup;

/// <summary>
/// Mise en service (phase 8, décisions du 24 octobre 2026) : import des tiers et de leurs soldes depuis un modèle Excel.
/// Superviseur seulement (parties.manage). Le modèle a une ligne d'en-tête (français ou arabe) et une feuille d'aide ;
/// « check » renvoie le rapport sans rien créer, « import » crée tout ou rien.
/// </summary>
public static class PartyImportEndpoints
{
    /// <summary>Un fichier Excel de plus est refusé (le modèle d'environ 100 tiers pèse quelques dizaines de Ko).</summary>
    private const long MaxFileBytes = 5 * 1024 * 1024;

    private static readonly Error Unreadable =
        Error.Validation("Parties.Import.FileUnreadable", "Fichier illisible : envoyer le modèle Excel (.xlsx) rempli.");

    public static IEndpointRouteBuilder MapPartyImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/setup/parties").WithTags("Mise en service");

        group.MapGet("/template", GetTemplate).RequirePermission(Permissions.PartiesManage);
        // Fichier envoyé avec le jeton (pas de cookie d'authentification) : pas de jeton anti-falsification.
        group.MapPost("/check", Check).RequirePermission(Permissions.PartiesManage).DisableAntiforgery();
        group.MapPost("/import", Import).RequirePermission(Permissions.PartiesManage).DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> GetTemplate(string? lang, IAgencyDirectory agencies, CancellationToken cancellationToken)
    {
        var language = ExcelExport.ParseLanguage(lang);
        var columns = Columns(language, await agencies.GetAllAsync(cancellationToken));

        using var workbook = ExcelExport.NewWorkbook(language, "Tiers à importer", "الأطراف للاستيراد");
        var sheet = workbook.Worksheets.Add(language.Pick("Tiers", "الأطراف"));
        sheet.RightToLeft = language == ExportLanguage.Arabic;
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
            sheet.Column(c + 1).Width = 20;
            // Téléphone et NNI en texte : sinon Excel retire le « + » et les zéros de tête.
            if (columns[c].Key is "phone" or "nni")
            {
                sheet.Column(c + 1).Style.NumberFormat.Format = "@";
            }
        }

        sheet.SheetView.FreezeRows(1);
        AddHelpSheet(workbook, language);
        return ExcelExport.File(ExcelExport.Save(workbook), language.Pick("Modele-import-tiers.xlsx", "Modele-import-tiers-ar.xlsx"));
    }

    private static async Task<IResult> Check(
        IFormFile file,
        IAgencyDirectory agencies,
        IQueryHandler<CheckPartyImportQuery, PartyImportReport> handler,
        CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(file, agencies, cancellationToken);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var result = await handler.Handle(new CheckPartyImportQuery(rows.Value), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="operationDate">Date des soldes initiaux (aaaa-mm-jj), jour de mise en service ; aujourd'hui si absente.</param>
    private static async Task<IResult> Import(
        IFormFile file,
        [Microsoft.AspNetCore.Mvc.FromForm] string? operationDate,
        ClaimsPrincipal user,
        IAgencyDirectory agencies,
        ICommandHandler<ImportPartiesCommand, PartyImportResult> handler,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(operationDate))
        {
            if (!DateOnly.TryParseExact(operationDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return Error.Validation("Parties.Import.DateInvalid", "Date de mise en service illisible (aaaa-mm-jj).").ToProblem();
            }

            date = parsed;
        }

        var rows = await ReadAsync(file, agencies, cancellationToken);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var result = await handler.Handle(new ImportPartiesCommand(actorId, rows.Value, date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    // --- Lecture du fichier ------------------------------------------------

    private sealed record Column(string Key, string Header, string? AgencyCode);

    /// <summary>Colonnes du modèle, dans l'ordre ; les en-têtes français et arabes sont reconnus tous les deux à la lecture.</summary>
    private static List<Column> Columns(ExportLanguage language, IReadOnlyList<AgencySummary> agencies)
    {
        var columns = new List<Column>
        {
            new("name", language.Pick("Nom", "الاسم"), null),
            new("type", language.Pick("Type", "النوع"), null),
            new("phone", language.Pick("Téléphone", "الهاتف"), null),
            new("nni", language.Pick("NNI", "رقم التعريف"), null),
            new("address", language.Pick("Adresse", "العنوان"), null),
            new("note", language.Pick("Note", "ملاحظة"), null),
            new("inBalanceSheet", language.Pick("Dans le bilan", "ضمن الميزانية"), null),
        };
        foreach (var agency in agencies.OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var city = language.City(agency.Code, agency.Name);
            var unit = ExcelExport.Unit(agency.Currency);
            columns.Add(new("balance", language.Pick($"Solde {city} ({unit})", $"الرصيد {city} ({unit})"), agency.Code));
            columns.Add(new("direction", language.Pick($"Sens {city}", $"الاتجاه {city}"), agency.Code));
            columns.Add(new("creditLimit", language.Pick($"Plafond {city} ({unit})", $"سقف الآجل {city} ({unit})"), agency.Code));
        }

        return columns;
    }

    private static async Task<Result<IReadOnlyList<PartyImportRow>>> ReadAsync(
        IFormFile? file, IAgencyDirectory agencies, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0 || file.Length > MaxFileBytes)
        {
            return Unreadable;
        }

        var all = await agencies.GetAllAsync(cancellationToken);
        var byHeader = Columns(ExportLanguage.French, all)
            .Concat(Columns(ExportLanguage.Arabic, all))
            .GroupBy(c => Normalize(c.Header))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);
            var header = sheet.Row(1);
            var map = new Dictionary<int, Column>();
            foreach (var cell in header.CellsUsed())
            {
                if (byHeader.TryGetValue(Normalize(cell.GetString()), out var column))
                {
                    map[cell.Address.ColumnNumber] = column;
                }
            }

            if (!map.Values.Any(c => c.Key == "name"))
            {
                return Unreadable;
            }

            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<PartyImportRow>();
            for (var r = 2; r <= last; r++)
            {
                var row = sheet.Row(r);
                string? Text(string key) => map.Where(m => m.Value.Key == key && m.Value.AgencyCode is null)
                    .Select(m => CellText(row.Cell(m.Key))).FirstOrDefault();
                string? AgencyText(string key, string code) => map.Where(m => m.Value.Key == key && m.Value.AgencyCode == code)
                    .Select(m => CellText(row.Cell(m.Key))).FirstOrDefault();

                rows.Add(new PartyImportRow(
                    r,
                    Text("name"),
                    Text("type"),
                    Text("phone"),
                    Text("nni"),
                    Text("address"),
                    Text("note"),
                    Text("inBalanceSheet"),
                    [.. all.Select(a => new PartyImportAgencyCells(
                        a.Code, AgencyText("balance", a.Code), AgencyText("direction", a.Code), AgencyText("creditLimit", a.Code)))]));
            }

            return rows;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException
            or InvalidOperationException or NotSupportedException or FormatException)
        {
            return Unreadable;
        }
    }

    /// <summary>Texte d'une cellule : un nombre est écrit en invariant (le serveur relit « 1250000.5 »).</summary>
    private static string? CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var text = cell.Value.Type == XLDataType.Number
            ? cell.Value.GetNumber().ToString("0.############", CultureInfo.InvariantCulture)
            : cell.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string Normalize(string header) =>
        string.Join(' ', header.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static void AddHelpSheet(XLWorkbook workbook, ExportLanguage language)
    {
        var help = workbook.Worksheets.Add(language.Pick("Aide", "مساعدة"));
        help.RightToLeft = language == ExportLanguage.Arabic;
        string[] lines = language == ExportLanguage.Arabic
            ? [
                "سطر واحد لكل طرف. الحقول الإلزامية: الاسم (30 حرفًا على الأكثر) والنوع.",
                "النوع: عميل، مورد، موظف، آخر.",
                "الهاتف بالصيغة الدولية (+222… أو +221…)، اختياري لكن فريد. رقم التعريف اختياري لكن فريد.",
                "ضمن الميزانية: نعم أو لا (فارغ = نعم).",
                "الرصيد موجب، والاتجاه: عليه (يدين لنا) أو له (ندين له). فارغ = رصيد صفر.",
                "الفرنك دائمًا عدد صحيح؛ الأوقية القديمة رقمان عشريان على الأكثر.",
                "سقف الآجل: فارغ = بلا سقف، 0 = لا آجل.",
                "البنوك والمحافظ ليست أطرافًا: تُنشأ في حسابات الخزينة.",
            ]
            : [
                "Une ligne par tiers. Obligatoires : Nom (30 caractères au plus) et Type.",
                "Type : Client, Fournisseur, Personnel, Autre.",
                "Téléphone au format international (+222…, +221…), facultatif mais unique. NNI facultatif mais unique.",
                "Dans le bilan : Oui ou Non (vide = Oui).",
                "Solde positif et Sens : « Il nous doit » ou « On lui doit » (عليه / له acceptés). Vide = solde nul.",
                "FCFA toujours entier ; MRO avec 2 décimales au plus.",
                "Plafond : vide = pas de plafond, 0 = aucun crédit.",
                "Les banques et wallets ne sont pas des tiers : les créer dans les comptes de trésorerie.",
            ];
        for (var i = 0; i < lines.Length; i++)
        {
            help.Cell(i + 1, 1).Value = lines[i];
        }

        help.Column(1).Width = 110;
    }
}
