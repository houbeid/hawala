using System.Globalization;
using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.Contracts;
using Transfers.Application.Features.Transfers.ResumeTransfers;

namespace Hawala.Api.Endpoints.Setup;

/// <summary>
/// Mise en service (phase 8, décisions du 24 octobre 2026) : reprise des transferts non livrés de l'ancienne app depuis un
/// 2e modèle Excel. Superviseur seulement (transfers.edit_rate : argent des transferts). « check » renvoie le rapport sans
/// rien créer, « import » crée tout ou rien.
/// </summary>
public static class TransferResumeEndpoints
{
    private static readonly Error Unreadable =
        Error.Validation("Transfers.Resume.FileUnreadable", "Fichier illisible : envoyer le modèle Excel (.xlsx) rempli.");

    /// <summary>Colonnes du modèle (clé, en-tête français, en-tête arabe), dans l'ordre.</summary>
    private static readonly (string Key, string French, string Arabic)[] Columns =
    [
        ("reference", "Référence", "المرجع"),
        ("operationAt", "Date (jj/mm/aaaa hh:mm)", "التاريخ (يوم/شهر/سنة ساعة:دقيقة)"),
        ("sourceAgency", "Agence d'envoi (NKC ou DKR)", "وكالة الإرسال (NKC أو DKR)"),
        ("amountXof", "Montant FCFA", "المبلغ بالفرنك"),
        ("amountMro", "Montant MRO", "المبلغ بالأوقية"),
        ("saleRate", "Prix de vente (MRO pour 5 000 FCFA)", "سعر البيع (أوقية لكل 5000 فرنك)"),
        ("senderInfo", "Expéditeur", "المرسل"),
        ("beneficiaryInfo", "Bénéficiaire", "المستفيد"),
        ("note", "Note", "ملاحظة"),
    ];

    public static IEndpointRouteBuilder MapTransferResumeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/setup/transfers").WithTags("Mise en service");

        group.MapGet("/template", GetTemplate).RequirePermission(Permissions.TransfersEditRate);
        group.MapPost("/check", Check).RequirePermission(Permissions.TransfersEditRate).DisableAntiforgery();
        group.MapPost("/import", Import).RequirePermission(Permissions.TransfersEditRate).DisableAntiforgery();
        return app;
    }

    private static IResult GetTemplate(string? lang)
    {
        var language = ExcelExport.ParseLanguage(lang);
        using var workbook = ExcelExport.NewWorkbook(language, "Transferts non livrés", "الحوالات غير المسلمة");
        var sheet = workbook.Worksheets.Add(language.Pick("Non livrés", "غير مسلمة"));
        sheet.RightToLeft = language == ExportLanguage.Arabic;
        for (var c = 0; c < Columns.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = language.Pick(Columns[c].French, Columns[c].Arabic);
            cell.Style.Font.Bold = true;
            sheet.Column(c + 1).Width = 24;
            if (Columns[c].Key is "reference" or "operationAt")
            {
                sheet.Column(c + 1).Style.NumberFormat.Format = "@";
            }
        }

        sheet.SheetView.FreezeRows(1);
        var help = workbook.Worksheets.Add(language.Pick("Aide", "مساعدة"));
        help.RightToLeft = sheet.RightToLeft;
        string[] lines = language == ExportLanguage.Arabic
            ? [
                "سطر لكل حوالة غير مسلّمة في التطبيق القديم، كما هي فيه.",
                "المرجع: مرجع التطبيق القديم، فريد. التاريخ: يوم/شهر/سنة ساعة:دقيقة.",
                "وكالة الإرسال: NKC أو DKR (الوجهة هي الوكالة الأخرى).",
                "المبلغ بالفرنك عدد صحيح؛ المبلغ بالأوقية القديمة رقمان عشريان على الأكثر؛ سعر البيع اختياري.",
                "لا تُمسّ الصناديق: المال موجود في رصيدها الافتتاحي. الرمز القديم غير معروف: رمز جديد أو تسليم بدون رمز.",
            ]
            : [
                "Une ligne par transfert NON LIVRÉ de l'ancienne app, tel qu'il y est enregistré.",
                "Référence : celle de l'ancienne app, unique. Date : jj/mm/aaaa hh:mm.",
                "Agence d'envoi : NKC ou DKR (la destination est l'autre agence).",
                "Montant FCFA entier ; montant MRO à 2 décimales au plus ; prix de vente facultatif.",
                "Les caisses ne sont pas touchées (l'argent est dans leur solde de départ). L'ancien code est inconnu : nouveau code ou livraison sans code.",
            ];
        for (var i = 0; i < lines.Length; i++)
        {
            help.Cell(i + 1, 1).Value = lines[i];
        }

        help.Column(1).Width = 110;
        return ExcelExport.File(ExcelExport.Save(workbook), "Modele-reprise-transferts.xlsx");
    }

    private static async Task<IResult> Check(
        IFormFile file,
        IQueryHandler<CheckResumedTransfersQuery, ResumedTransferReport> handler,
        CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var result = await handler.Handle(new CheckResumedTransfersQuery(rows.Value), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    /// <param name="operationDate">Date des écritures d'ouverture (jour de mise en service, aaaa-mm-jj) ; aujourd'hui si absente.</param>
    private static async Task<IResult> Import(
        IFormFile file,
        [Microsoft.AspNetCore.Mvc.FromForm] string? operationDate,
        ClaimsPrincipal user,
        ICommandHandler<ImportResumedTransfersCommand, ResumedTransferResult> handler,
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
                return Error.Validation("Transfers.Resume.OpeningDateInvalid", "Date de mise en service illisible (aaaa-mm-jj).").ToProblem();
            }

            date = parsed;
        }

        var rows = await ReadAsync(file);
        if (rows.IsFailure)
        {
            return rows.Error.ToProblem();
        }

        var result = await handler.Handle(new ImportResumedTransfersCommand(actorId, rows.Value, date), cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    private static async Task<Result<IReadOnlyList<ResumedTransferRow>>> ReadAsync(IFormFile file)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, french, arabic) in Columns)
        {
            headers[french] = key;
            headers[arabic] = key;
        }

        var sheet = await ExcelImport.ReadAsync(file, headers, "reference", Unreadable);
        if (sheet.IsFailure)
        {
            return sheet.Error;
        }

        return sheet.Value
            .Select(r => new ResumedTransferRow(
                r.Row, r["reference"], r["operationAt"], r["sourceAgency"], r["amountXof"], r["amountMro"], r["saleRate"],
                r["senderInfo"], r["beneficiaryInfo"], r["note"]))
            .ToList();
    }
}
