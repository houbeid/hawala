using System.Security.Claims;
using Cash.Application.Features.Movements;
using Cash.Application.Features.Movements.ListCashMovements;
using Cash.Domain.Movements;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;

namespace Hawala.Api.Endpoints.Cash;

/// <summary>
/// Export Excel des mouvements de caisse (permission export et cash.budget.read, comme l'écran). Lit la liste page par
/// page avec la requête de l'écran : mêmes filtres et même périmètre. Une ligne par mouvement, annulés compris (avec leur
/// statut et leur motif) ; le sens est écrit en mots (entrée, sortie, virement interne), le montant toujours positif.
/// </summary>
public static class CashExport
{
    /// <param name="lang">« ar » pour les en-têtes en arabe ; français sinon.</param>
    public static async Task<IResult> ExportAsync(
        Guid? agencyId,
        DateOnly? from,
        DateOnly? to,
        string? type,
        Guid? treasuryAccountId,
        string? search,
        string? lang,
        ClaimsPrincipal user,
        IQueryHandler<ListCashMovementsQuery, CashMovementList> handler,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var items = new List<CashMovementView>();
        var seen = new HashSet<Guid>();
        for (var page = 1; ; page++)
        {
            var result = await handler.Handle(
                new ListCashMovementsQuery(actorId, agencyId, from, to, type, treasuryAccountId, search, page), cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var list = result.Value;
            if (list.TotalCount > ExcelExport.MaxRows)
            {
                return ExcelExport.TooLarge.ToProblem();
            }

            items.AddRange(list.Items.Where(m => seen.Add(m.Id)));
            if (list.Items.Count < list.PageSize || page * list.PageSize >= list.TotalCount)
            {
                break;
            }
        }

        var language = ExcelExport.ParseLanguage(lang);
        var content = ExcelExport.Build(language, "Caisse", "الصندوق", Columns(language), items);
        return ExcelExport.File(content, $"Caisse-{ExcelExport.Period(from, to, clock.UtcNow)}.xlsx");
    }

    private static List<ExportColumn<CashMovementView>> Columns(ExportLanguage language) =>
    [
        new("Date de l'opération", "تاريخ العملية", m => m.OperationAt, ExportFormat.DateTime, 18),
        new("Opération", "العملية", m => Type(language, m.Type), Width: 22),
        new("Sens", "الاتجاه", m => Direction(language, m), Width: 16),
        new("Montant", "المبلغ", m => m.Amount, ExportFormat.Amount),
        new("Devise", "العملة", m => ExcelExport.Unit(m.Currency), Width: 8),
        new("Compte", "الحساب", m => m.TreasuryAccountName, Width: 24),
        new("Compte de destination", "الحساب المستلم", m => m.DestinationTreasuryAccountName, Width: 24),
        new("Tiers", "الطرف", m => m.PartyName, Width: 24),
        new("Note", "ملاحظة", m => m.Note, Width: 28),
        new("Statut", "الحالة", m => m.Status == CashMovementStatus.Cancelled ? language.Pick("Annulé", "ملغى") : language.Pick("Actif", "ساري"), Width: 10),
        new("Motif de l'annulation", "سبب الإلغاء", m => m.CancellationReason, Width: 28),
        new("Enregistré le", "تاريخ التسجيل", m => m.CreatedAt, ExportFormat.DateTime, 18),
    ];

    private static string Type(ExportLanguage language, CashMovementType type) => type switch
    {
        CashMovementType.Receipt => language.Pick("Encaissement", "قبض"),
        CashMovementType.Payment => language.Pick("Décaissement", "صرف"),
        CashMovementType.OwnerContribution => language.Pick("Approvisionnement de caisse", "تزويد الصندوق"),
        CashMovementType.OwnerWithdrawal => language.Pick("Retrait du patron", "سحب المالك"),
        CashMovementType.InternalTransfer => language.Pick("Virement interne", "تحويل داخلي"),
        _ => type.ToString(),
    };

    private static string Direction(ExportLanguage language, CashMovementView movement) =>
        movement.IsInternal ? language.Pick("Virement interne", "تحويل داخلي")
        : movement.Incoming ? language.Pick("Entrée", "دخول")
        : language.Pick("Sortie", "خروج");
}
