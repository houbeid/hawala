using System.Globalization;
using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Transfers.Application.Features.Transfers;
using Transfers.Application.Features.Transfers.ListTransfers;
using Transfers.Domain.Transfers;

namespace Hawala.Api.Endpoints.Transfers;

/// <summary>
/// Export Excel de la liste des transferts (permission export ; décision du 24 octobre 2026). Lit la liste page par page
/// avec la requête de l'écran : mêmes filtres, même périmètre (un agent : son agence), mêmes champs masqués (prix d'achat
/// et bénéfice sans profit.view ; répartition et crédit pour l'agence de destination). Les colonnes du prix d'achat et
/// du bénéfice n'apparaissent que si le compte les voit. Plus de 10 000 transferts : refus (réduire la période).
/// </summary>
public static class TransferExport
{
    /// <param name="lang">« ar » pour les en-têtes en arabe (feuille de droite à gauche) ; français sinon.</param>
    public static async Task<IResult> ExportAsync(
        DateOnly? from,
        DateOnly? to,
        string? status,
        string? direction,
        Guid? agencyId,
        string? search,
        string? lang,
        ClaimsPrincipal user,
        IQueryHandler<ListTransfersQuery, TransferList> handler,
        IAgencyDirectory agencies,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var items = new List<TransferView>();
        var seen = new HashSet<Guid>();
        for (var page = 1; ; page++)
        {
            var result = await handler.Handle(
                new ListTransfersQuery(actorId, from, to, status, direction, agencyId, search, page), cancellationToken);
            if (result.IsFailure)
            {
                return result.Error.ToProblem();
            }

            var list = result.Value;
            if (list.TotalCount > ExcelExport.MaxRows)
            {
                return ExcelExport.TooLarge.ToProblem();
            }

            // Un transfert créé pendant la lecture décale les pages : jamais deux fois la même ligne.
            items.AddRange(list.Items.Where(t => seen.Add(t.Id)));
            if (list.Items.Count < list.PageSize || page * list.PageSize >= list.TotalCount)
            {
                break;
            }
        }

        var language = ExcelExport.ParseLanguage(lang);
        var cities = (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Id, a => City(language, a));
        var content = ExcelExport.Build(language, "Transferts", "الحوالات", Columns(language, cities, items), items);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var first = (from ?? today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var last = (to ?? today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return ExcelExport.File(content, $"Transferts-{first}-{last}.xlsx");
    }

    private static List<ExportColumn<TransferView>> Columns(
        ExportLanguage language, IReadOnlyDictionary<Guid, string> cities, IReadOnlyList<TransferView> items)
    {
        string CityOf(Guid id) => cities.GetValueOrDefault(id, "?");

        var columns = new List<ExportColumn<TransferView>>
        {
            new("Référence", "المرجع", t => t.Reference, Width: 26),
            new("Date de l'opération", "تاريخ العملية", t => t.OperationAt, ExportFormat.DateTime, 18),
            new("Statut", "الحالة", t => Status(language, t.Status)),
            new("Sens", "الاتجاه", t => Direction(language, t.Direction), Width: 10),
            new("Agence d'envoi", "وكالة الإرسال", t => CityOf(t.SourceAgencyId)),
            new("Agence de destination", "وكالة الوجهة", t => CityOf(t.DestinationAgencyId)),
            new("Montant FCFA", "المبلغ بالفرنك", t => t.AmountXof, ExportFormat.Integer),
            new("Montant MRO", "المبلغ بالأوقية", t => t.AmountMro, ExportFormat.Amount),
            new("Prix de vente (MRO pour 5 000 FCFA)", "سعر البيع (أوقية لكل 5000 فرنك)", t => t.SaleRate, ExportFormat.Rate, 18),
        };

        // Champs masqués par la vue : leur colonne n'existe que si le compte les voit.
        if (items.Any(t => t.PurchaseRate is not null))
        {
            columns.Add(new("Prix d'achat (MRO pour 5 000 FCFA)", "سعر الشراء (أوقية لكل 5000 فرنك)", t => t.PurchaseRate, ExportFormat.Rate, 18));
            columns.Add(new("Bénéfice MRO", "الربح بالأوقية", t => t.ProfitMro, ExportFormat.Amount));
        }

        columns.AddRange(
        [
            new("Expéditeur", "المرسل", t => t.SenderInfo, Width: 28),
            new("Bénéficiaire", "المستفيد", t => t.BeneficiaryInfo, Width: 28),
            new("Note", "ملاحظة", t => t.Note, Width: 28),
        ]);

        if (items.Any(t => t.Payments is not null))
        {
            columns.Add(new("Paiement du client", "دفع الزبون", t => Payments(t.Payments), Width: 32));
            columns.Add(new("Crédit : tiers", "الآجل: الطرف", t => t.Credit?.PartyName, Width: 22));
            columns.Add(new("Crédit : montant", "الآجل: المبلغ", t => t.Credit?.Amount, ExportFormat.Amount));
        }

        columns.AddRange(
        [
            new("Livré le", "تاريخ التسليم", t => t.DeliveryOperationAt, ExportFormat.DateTime, 18),
            new("Livré sans code", "سُلِّم بدون رمز", t => t.DeliveredWithoutCode ? language.Pick("Oui", "نعم") : null, Width: 12),
            new("Annulé le", "تاريخ الإلغاء", t => t.CancellationOperationAt, ExportFormat.DateTime, 18),
            new("Motif de l'annulation", "سبب الإلغاء", t => t.CancellationReason, Width: 28),
            new("Enregistré le", "تاريخ التسجيل", t => t.CreatedAt, ExportFormat.DateTime, 18),
        ]);
        return columns;
    }

    /// <summary>« Caisse principale : 612500 ; Bankily : 300000 » (montants dans la devise de l'agence d'envoi).</summary>
    private static string? Payments(IReadOnlyList<TransferPaymentView>? payments) =>
        payments is null
            ? null
            : string.Join(" ; ", payments.Select(p =>
                $"{p.TreasuryAccountName ?? "?"} : {p.Amount.ToString("0.##", CultureInfo.InvariantCulture)} {(p.Currency == Hawala.SharedKernel.Money.Currency.XOF ? "FCFA" : "MRO")}"));

    private static string Status(ExportLanguage language, TransferStatus status) => status switch
    {
        TransferStatus.Delivered => language.Pick("Livré", "مسلَّمة"),
        TransferStatus.Cancelled => language.Pick("Annulé", "ملغاة"),
        _ => language.Pick("Non livré", "غير مسلَّمة"),
    };

    private static string? Direction(ExportLanguage language, TransferDirection? direction) => direction switch
    {
        TransferDirection.Sent => language.Pick("Envoyé", "مرسلة"),
        TransferDirection.Received => language.Pick("Reçu", "مستلمة"),
        _ => null,
    };

    private static string City(ExportLanguage language, AgencySummary agency) => agency.Code switch
    {
        "NKC" => language.Pick("Nouakchott", "نواكشوط"),
        "DKR" => language.Pick("Dakar", "داكار"),
        _ => agency.Name,
    };
}
