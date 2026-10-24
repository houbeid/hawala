using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Profit.Application.Features;
using Transfers.Contracts;

namespace Hawala.Api.Endpoints.Profit;

/// <summary>
/// Export Excel du bénéfice (permission export et profit.view, comme l'écran) : deux feuilles, « Par jour » (comme le
/// rapport : transferts, FCFA envoyé, prix d'achat, bénéfice, enregistré) et « Par transfert » (le détail de chaque jour).
/// Bénéfice en pleine précision : Excel l'affiche à deux décimales, la valeur garde tous ses chiffres.
/// </summary>
public static class ProfitExport
{
    /// <param name="agencyId">Agence d'envoi ; absente : les deux agences (bénéfice général).</param>
    /// <param name="lang">« ar » pour les en-têtes en arabe ; français sinon.</param>
    public static async Task<IResult> ExportAsync(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        string? lang,
        ClaimsPrincipal user,
        IQueryHandler<GetProfitReportQuery, ProfitReport> reports,
        IQueryHandler<GetProfitDayQuery, ProfitDayDetail> days,
        IAgencyDirectory agencies,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var report = await reports.Handle(new GetProfitReportQuery(actorId, agencyId, from, to), cancellationToken);
        if (report.IsFailure)
        {
            return report.Error.ToProblem();
        }

        if (report.Value.TransferCount > ExcelExport.MaxRows)
        {
            return ExcelExport.TooLarge.ToProblem();
        }

        var transfers = new List<TransferProfitLine>();
        foreach (var day in report.Value.Days.Where(d => d.TransferCount > 0).OrderBy(d => d.Date))
        {
            var detail = await days.Handle(new GetProfitDayQuery(actorId, agencyId, day.Date), cancellationToken);
            if (detail.IsFailure)
            {
                return detail.Error.ToProblem();
            }

            transfers.AddRange(detail.Value.Transfers.OrderBy(t => t.OperationAt));
        }

        var language = ExcelExport.ParseLanguage(lang);
        var cities = (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id, a => language.City(a.Code, a.Name));

        using var workbook = ExcelExport.NewWorkbook(language, "Bénéfice", "الأرباح");
        ExcelExport.AddSheet(workbook, language, "Par jour", "حسب اليوم", DayColumns(language), report.Value.Days.OrderBy(d => d.Date));
        ExcelExport.AddSheet(workbook, language, "Par transfert", "حسب الحوالة", TransferColumns(cities), transfers);
        var content = ExcelExport.Save(workbook);

        return ExcelExport.File(content, $"Benefice-{ExcelExport.Period(from, to, clock.UtcNow)}.xlsx");
    }

    private static List<ExportColumn<ProfitDay>> DayColumns(ExportLanguage language) =>
    [
        new("Jour", "اليوم", d => d.Date, ExportFormat.Date, 12),
        new("Transferts", "الحوالات", d => d.TransferCount, ExportFormat.Integer, 10),
        new("FCFA envoyé", "الفرنك المرسل", d => d.AmountXof, ExportFormat.Integer, 16),
        new("Prix d'achat (MRO pour 5 000 FCFA)", "سعر الشراء (أوقية لكل 5000 فرنك)", d => d.PurchaseRate is { } rate ? rate : language.Pick("plusieurs", "متعدد"), ExportFormat.Rate, 18),
        new("Bénéfice MRO", "الربح بالأوقية", d => d.ProfitMro, ExportFormat.Amount, 16),
        new("Enregistré (MRO)", "المسجل (أوقية)", d => d.SavedProfitMro, ExportFormat.Amount, 16),
        new("Enregistré le", "تاريخ التسجيل", d => d.SavedAt, ExportFormat.DateTime, 18),
    ];

    private static List<ExportColumn<TransferProfitLine>> TransferColumns(IReadOnlyDictionary<Guid, string> cities) =>
    [
        new("Référence", "المرجع", t => t.Reference, Width: 26),
        new("Date de l'opération", "تاريخ العملية", t => t.OperationAt, ExportFormat.DateTime, 18),
        new("Agence d'envoi", "وكالة الإرسال", t => cities.GetValueOrDefault(t.SourceAgencyId, "?")),
        new("Agence de destination", "وكالة الوجهة", t => cities.GetValueOrDefault(t.DestinationAgencyId, "?")),
        new("Montant FCFA", "المبلغ بالفرنك", t => t.AmountXof, ExportFormat.Integer),
        new("Montant MRO", "المبلغ بالأوقية", t => t.AmountMro, ExportFormat.Amount),
        new("Prix de vente", "سعر البيع", t => t.SaleRate, ExportFormat.Rate),
        new("Prix d'achat", "سعر الشراء", t => t.PurchaseRate, ExportFormat.Rate),
        new("Bénéfice MRO", "الربح بالأوقية", t => t.ProfitMro, ExportFormat.Amount),
    ];
}
