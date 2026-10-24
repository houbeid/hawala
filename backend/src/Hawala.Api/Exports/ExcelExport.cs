using ClosedXML.Excel;
using Hawala.SharedKernel.Results;

namespace Hawala.Api.Exports;

/// <summary>Langue des en-têtes et des mots d'un export ; l'arabe met aussi la feuille de droite à gauche.</summary>
public enum ExportLanguage
{
    French,
    Arabic,
}

/// <summary>Mise en forme d'une colonne : les nombres et les dates restent des nombres et des dates pour Excel.</summary>
public enum ExportFormat
{
    Text,

    /// <summary>FCFA, compteurs : « 1 250 000 ».</summary>
    Integer,

    /// <summary>Montant en MRO : deux décimales.</summary>
    Amount,

    /// <summary>Taux « MRO pour 5 000 FCFA » : deux décimales, sans séparateur de milliers.</summary>
    Rate,

    DateTime,

    Date,
}

/// <param name="Value">Valeur de la cellule pour une ligne : texte, nombre, date, ou null (cellule vide).</param>
public sealed record ExportColumn<T>(string French, string Arabic, Func<T, object?> Value, ExportFormat Format = ExportFormat.Text, double Width = 16);

/// <summary>
/// Socle des exports Excel (.xlsx, décision du 24 octobre 2026) : une feuille, une ligne d'en-tête (en gras, figée, avec
/// filtres), une ligne par élément. Les exports réutilisent les requêtes des listes : mêmes filtres, même périmètre et
/// mêmes champs masqués que l'écran. Heures en UTC (Nouakchott et Dakar sont en UTC+0). Le texte saisi (expéditeur,
/// note…) est écrit comme du texte, jamais comme une formule. Les exports ne sont pas tracés dans l'audit (règle du
/// 22 octobre 2026 : seulement les actions).
/// </summary>
public static class ExcelExport
{
    /// <summary>Au-delà, l'export est refusé : réduire la période ou filtrer.</summary>
    public const int MaxRows = 10_000;

    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static readonly Error TooLarge = Error.Validation(
        "Api.Export.TooLarge", $"Plus de {MaxRows} lignes : réduire la période ou ajouter un filtre.") with
    {
        Details = new Dictionary<string, object?> { ["maxRows"] = MaxRows },
    };

    /// <summary>« ar » : arabe ; toute autre valeur (ou rien) : français.</summary>
    public static ExportLanguage ParseLanguage(string? lang) =>
        string.Equals(lang, "ar", StringComparison.OrdinalIgnoreCase) ? ExportLanguage.Arabic : ExportLanguage.French;

    public static string Pick(this ExportLanguage language, string french, string arabic) =>
        language == ExportLanguage.Arabic ? arabic : french;

    /// <summary>Classeur d'une seule feuille.</summary>
    public static byte[] Build<T>(
        ExportLanguage language,
        string sheetFrench,
        string sheetArabic,
        IReadOnlyList<ExportColumn<T>> columns,
        IEnumerable<T> rows)
    {
        using var workbook = NewWorkbook(language, sheetFrench, sheetArabic);
        AddSheet(workbook, language, sheetFrench, sheetArabic, columns, rows);
        return Save(workbook);
    }

    /// <summary>Classeur vide (titre et auteur posés) : on y ajoute ses feuilles avec <see cref="AddSheet"/>.</summary>
    public static XLWorkbook NewWorkbook(ExportLanguage language, string titleFrench, string titleArabic)
    {
        var workbook = new XLWorkbook();
        workbook.Properties.Title = language.Pick(titleFrench, titleArabic);
        workbook.Properties.Author = "ETS EL-VETH";
        return workbook;
    }

    public static byte[] Save(XLWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>Une feuille : en-tête en gras, figé, avec filtres ; une ligne par élément.</summary>
    public static void AddSheet<T>(
        XLWorkbook workbook,
        ExportLanguage language,
        string sheetFrench,
        string sheetArabic,
        IReadOnlyList<ExportColumn<T>> columns,
        IEnumerable<T> rows)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        var sheet = workbook.Worksheets.Add(language.Pick(sheetFrench, sheetArabic));
        sheet.RightToLeft = language == ExportLanguage.Arabic;

        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = language.Pick(columns[c].French, columns[c].Arabic);
            cell.Style.Font.Bold = true;
            sheet.Column(c + 1).Width = columns[c].Width;
        }

        var row = 2;
        foreach (var item in rows)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                Write(sheet.Cell(row, c + 1), columns[c].Value(item), columns[c].Format);
            }

            row++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Range(1, 1, Math.Max(row - 1, 1), Math.Max(columns.Count, 1)).SetAutoFilter();
    }

    /// <summary>« Nouakchott » / « نواكشوط » à partir du code de l'agence (le code ne change jamais).</summary>
    public static string City(this ExportLanguage language, string code, string name) => code switch
    {
        "NKC" => language.Pick("Nouakchott", "نواكشوط"),
        "DKR" => language.Pick("Dakar", "داكار"),
        _ => name,
    };

    /// <summary>« 20261001-20261024 » pour le nom du fichier ; aujourd'hui (UTC) par défaut.</summary>
    public static string Period(DateOnly? from, DateOnly? to, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return $"{(from ?? today).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}-{(to ?? today).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public static string Unit(Hawala.SharedKernel.Money.Currency currency) =>
        currency == Hawala.SharedKernel.Money.Currency.XOF ? "FCFA" : "MRO";

    /// <summary>Réponse du fichier : jamais gardée en cache (données de l'agence).</summary>
    public static IResult File(byte[] content, string fileName) =>
        new NoStoreResult(TypedResults.File(content, ContentType, fileName));

    private static void Write(IXLCell cell, object? value, ExportFormat format)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                // Texte saisi : toujours une valeur, jamais une formule (« =… » reste écrit tel quel).
                cell.Value = text;
                break;
            case decimal number:
                cell.Value = number;
                break;
            case int number:
                cell.Value = number;
                break;
            case DateTimeOffset at:
                cell.Value = at.UtcDateTime;
                break;
            case DateOnly day:
                cell.Value = day.ToDateTime(TimeOnly.MinValue);
                break;
            default:
                cell.Value = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                break;
        }

        cell.Style.NumberFormat.Format = format switch
        {
            ExportFormat.Integer => "#,##0",
            ExportFormat.Amount => "#,##0.00",
            ExportFormat.Rate => "0.00",
            ExportFormat.DateTime => "dd/mm/yyyy hh:mm",
            ExportFormat.Date => "dd/mm/yyyy",
            _ => "@",
        };
    }
}

/// <summary>Ajoute « Cache-Control: no-store » à une réponse de fichier (reçu, export).</summary>
public sealed class NoStoreResult(IResult inner) : IResult
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Headers.CacheControl = "no-store";
        return inner.ExecuteAsync(httpContext);
    }
}
