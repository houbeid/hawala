using System.Globalization;
using Hawala.SharedKernel.Money;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Infrastructure.Receipts;

/// <summary>
/// Reçu de l'expéditeur en PDF (règles du 24 octobre 2026) : pas d'imprimante en agence, le reçu est montré à l'écran
/// ou envoyé au client (WhatsApp) ; page étroite, de la hauteur du contenu, lisible sur un téléphone. Bilingue : chaque
/// libellé en français à gauche et en arabe à droite, la valeur dessous. Police IBM Plex Sans Arabic (latin et arabe,
/// licence OFL, embarquée : le serveur Linux n'a pas de polices). Montants écrits comme dans l'app : groupes de trois,
/// virgule décimale, unité toujours écrite, FCFA entier. Heures en UTC (Nouakchott et Dakar sont en UTC+0).
/// Licence QuestPDF « Community » : gratuite sous 1 M$ de chiffre d'affaires annuel.
/// </summary>
public sealed class QuestPdfReceiptRenderer : ITransferReceiptRenderer
{
    private const string FontFamily = "IBM Plex Sans Arabic";
    private const string Brand = "ETS EL-VETH";
    private static readonly string Muted = Colors.Grey.Darken1;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly Lazy<bool> Setup = new(Configure);

    public byte[] Render(TransferReceipt receipt) => CreateDocument(receipt).GeneratePdf();

    /// <summary>Le document QuestPDF du reçu (aussi rendu en image pour le relire à l'œil).</summary>
    public static IDocument CreateDocument(TransferReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        _ = Setup.Value;

        return Document.Create(document => document.Page(page =>
        {
            page.ContinuousSize(90, Unit.Millimetre);
            page.Margin(6, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(style => style.FontFamily(FontFamily).FontSize(9).FontColor(Colors.Black));
            page.Content().Column(column => Compose(column, receipt));
        }))
        .WithMetadata(new DocumentMetadata { Title = $"{Brand} · {receipt.Reference}", Author = Brand });
    }

    private static void Compose(ColumnDescriptor column, TransferReceipt receipt)
    {
        column.Spacing(7);

        column.Item().AlignCenter().Text(Brand).FontSize(15).Bold();
        column.Item().Row(row =>
        {
            row.RelativeItem().Text("Reçu de transfert").Bold();
            row.RelativeItem().AlignRight().Text("إيصال حوالة").Bold();
        });

        var mark = Mark(receipt);
        if (mark is not null)
        {
            column.Item().Border(1).BorderColor(Muted).Padding(4).Row(row =>
            {
                row.RelativeItem().Text(mark.Value.French).Bold();
                row.RelativeItem().AlignRight().Text(mark.Value.Arabic).Bold();
            });
        }

        column.Item().LineHorizontal(0.5f).LineColor(Muted);

        Field(column, "Référence", "المرجع", receipt.Reference);
        Field(column, "Date de l'opération", "تاريخ العملية", Instant(receipt.OperationAt));
        Field(
            column,
            "Envoi",
            "الإرسال",
            $"{City(receipt.Source).French} → {City(receipt.Destination).French}",
            $"من {City(receipt.Source).Arabic} إلى {City(receipt.Destination).Arabic}");
        Field(column, "Expéditeur", "المرسل", receipt.SenderInfo);
        Field(column, "Bénéficiaire", "المستفيد", receipt.BeneficiaryInfo);

        column.Item().LineHorizontal(0.5f).LineColor(Muted);

        Field(column, "Montant payé", "المبلغ المدفوع", Amount(receipt.PaidAmount));
        if (receipt.Payments.Count > 0)
        {
            Labels(column, "Paiement", "طريقة الدفع");
            foreach (var payment in receipt.Payments)
            {
                column.Item().PaddingLeft(6).Row(row =>
                {
                    row.RelativeItem().Text(payment.AccountName);
                    row.AutoItem().Text(Amount(payment.Amount));
                });
            }
        }

        if (receipt.CreditAmount is { } credit)
        {
            Field(column, "Part à crédit", "المبلغ بالآجل", Amount(credit));
        }

        column.Item().Background(Colors.Grey.Lighten3).Padding(6).Column(box =>
        {
            box.Spacing(2);
            box.Item().Row(row =>
            {
                row.RelativeItem().Text("Montant à livrer").FontColor(Muted);
                row.RelativeItem().AlignRight().Text("المبلغ المستحق للمستفيد").FontColor(Muted);
            });
            box.Item().Text(Amount(receipt.AmountToDeliver)).FontSize(16).Bold();
        });

        if (receipt.WithdrawalCode is { } code)
        {
            column.Item().Border(1.5f).BorderColor(Colors.Black).Padding(6).Column(box =>
            {
                box.Spacing(3);
                box.Item().Row(row =>
                {
                    row.RelativeItem().Text("Code de retrait").Bold();
                    row.RelativeItem().AlignRight().Text("رمز السحب").Bold();
                });
                box.Item().AlignCenter().Text(code).FontSize(22).Bold().LetterSpacing(0.25f);
                box.Item().Text("À donner au bénéficiaire seulement : il le présente pour retirer l'argent.").FontSize(8);
                box.Item().AlignRight().Text("يُعطى للمستفيد فقط، ويقدّمه لسحب المبلغ.").FontSize(8);
            });
        }

        column.Item().LineHorizontal(0.5f).LineColor(Muted);
        column.Item().Row(row =>
        {
            row.RelativeItem().Text($"Émis le {Instant(receipt.IssuedAt)}").FontSize(7).FontColor(Muted);
            row.RelativeItem().AlignRight().Text($"صدر في {Instant(receipt.IssuedAt)}").FontSize(7).FontColor(Muted);
        });
    }

    /// <summary>Libellés en deux langues, puis la valeur (et sa version arabe si elle diffère).</summary>
    private static void Field(ColumnDescriptor column, string french, string arabic, string value, string? arabicValue = null)
    {
        column.Item().Column(field =>
        {
            field.Spacing(1);
            field.Item().Row(row =>
            {
                row.RelativeItem().Text(french).FontSize(7.5f).FontColor(Muted);
                row.RelativeItem().AlignRight().Text(arabic).FontSize(7.5f).FontColor(Muted);
            });
            field.Item().Text(value).FontSize(10.5f).SemiBold();
            if (arabicValue is not null)
            {
                field.Item().AlignRight().Text(arabicValue).FontSize(10.5f).SemiBold();
            }
        });
    }

    private static void Labels(ColumnDescriptor column, string french, string arabic) =>
        column.Item().Row(row =>
        {
            row.RelativeItem().Text(french).FontSize(7.5f).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(arabic).FontSize(7.5f).FontColor(Muted);
        });

    private static (string French, string Arabic)? Mark(TransferReceipt receipt) => receipt.Status switch
    {
        TransferStatus.Delivered => ("Duplicata · Livré", "نسخة · تم التسليم"),
        TransferStatus.Cancelled => ("Duplicata · Annulé", "نسخة · ملغاة"),
        _ when receipt.IsDuplicate => ("Duplicata", "نسخة"),
        _ => null,
    };

    private static (string French, string Arabic) City(ReceiptAgency agency) => agency.Code switch
    {
        "NKC" => ("Nouakchott", "نواكشوط"),
        "DKR" => ("Dakar", "داكار"),
        _ => (agency.Name, agency.Name),
    };

    private static string Instant(DateTimeOffset at) => at.ToUniversalTime().ToString("dd/MM/yyyy HH:mm", Invariant);

    /// <summary>« 1 850 000 MRO », « 123,40 MRO », « 5 000 000 FCFA » (espace insécable entre les groupes).</summary>
    internal static string Amount(Money money)
    {
        var decimals = money.Currency == Currency.XOF || decimal.Truncate(money.Amount) == money.Amount ? 0 : 2;
        var number = Math.Abs(money.Amount).ToString(decimals == 0 ? "#,0" : "#,0.00", Invariant)
            .Replace(",", " ", StringComparison.Ordinal)
            .Replace(".", ",", StringComparison.Ordinal);
        var sign = money.Amount < 0 ? "− " : string.Empty;
        var unit = money.Currency == Currency.XOF ? "FCFA" : "MRO";
        return $"{sign}{number} {unit}";
    }

    private static bool Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        // Un caractère absent de la police (emoji dans un nom) ne doit pas empêcher le reçu.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        var assembly = typeof(QuestPdfReceiptRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Police du reçu introuvable : {name}.");
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }
}
