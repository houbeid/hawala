using System.Globalization;
using ClosedXML.Excel;
using Hawala.SharedKernel.Results;

namespace Hawala.Api.Exports;

/// <summary>
/// Lecture d'un modèle Excel rempli (mise en service) : première feuille, en-têtes en ligne 1 reconnus par leur texte
/// (français ou arabe, sans tenir compte de la casse ni des espaces), une ligne par élément. Chaque cellule est rendue en
/// texte : un nombre en invariant (« 1250000.5 »), une date en ISO (« 2026-10-20T14:30 »).
/// </summary>
public static class ExcelImport
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Une ligne lue : numéro de ligne Excel et cellules par clé de colonne (null = vide).</summary>
    public sealed record SheetRow(int Row, IReadOnlyDictionary<string, string?> Cells)
    {
        public string? this[string key] => Cells.GetValueOrDefault(key);
    }

    /// <param name="headers">Texte d'en-tête (toutes langues) → clé de colonne.</param>
    /// <param name="requiredKey">Colonne sans laquelle le fichier n'est pas le bon modèle.</param>
    public static async Task<Result<IReadOnlyList<SheetRow>>> ReadAsync(
        IFormFile? file, IReadOnlyDictionary<string, string> headers, string requiredKey, Error unreadable)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (file is null || file.Length == 0 || file.Length > MaxFileBytes)
        {
            return unreadable;
        }

        var byHeader = headers.ToDictionary(h => Normalize(h.Key), h => h.Value, StringComparer.Ordinal);
        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);
            var map = new Dictionary<int, string>();
            foreach (var cell in sheet.Row(1).CellsUsed())
            {
                if (byHeader.TryGetValue(Normalize(cell.GetString()), out var key))
                {
                    map[cell.Address.ColumnNumber] = key;
                }
            }

            if (!map.ContainsValue(requiredKey))
            {
                return unreadable;
            }

            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            var rows = new List<SheetRow>();
            for (var r = 2; r <= last; r++)
            {
                var row = sheet.Row(r);
                rows.Add(new SheetRow(r, map.ToDictionary(m => m.Value, m => CellText(row.Cell(m.Key)), StringComparer.Ordinal)));
            }

            return rows;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException
            or InvalidOperationException or NotSupportedException or FormatException)
        {
            return unreadable;
        }
    }

    public static string? CellText(IXLCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.IsEmpty())
        {
            return null;
        }

        var text = cell.Value.Type switch
        {
            XLDataType.Number => cell.Value.GetNumber().ToString("0.############", CultureInfo.InvariantCulture),
            XLDataType.DateTime => cell.Value.GetDateTime().ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            _ => cell.GetString(),
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string Normalize(string header) =>
        string.Join(' ', header.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
