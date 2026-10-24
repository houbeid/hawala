using System.Globalization;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers.ResumeTransfers;

// Reprise des transferts non livrés de l'ancienne app à la mise en service (phase 8, décisions du 24 octobre 2026) :
// 2e modèle Excel sur l'écran « Mise en service » ; « vérifier » (rapport, rien n'est créé) puis « importer » (tout ou
// rien). Chaque cellule arrive en texte (lue par l'API).

/// <param name="OperationAt">Date (et heure) d'origine : « jj/mm/aaaa hh:mm », « jj/mm/aaaa » ou ISO.</param>
/// <param name="SourceAgency">Agence d'envoi : NKC, DKR, Nouakchott, Dakar (ou en arabe).</param>
/// <param name="SaleRate">Prix de vente (MRO pour 5 000 FCFA), facultatif : déduit des deux montants s'il manque.</param>
public sealed record ResumedTransferRow(
    int Row,
    string? Reference,
    string? OperationAt,
    string? SourceAgency,
    string? AmountXof,
    string? AmountMro,
    string? SaleRate,
    string? SenderInfo,
    string? BeneficiaryInfo,
    string? Note);

public sealed record ResumedTransferIssue(int Row, string? Column, string Code);

/// <param name="Totals">Montant à livrer par agence de destination (code → somme, dans sa devise), à comparer avec l'ancienne app.</param>
public sealed record ResumedTransferReport(
    int RowCount,
    int ValidCount,
    IReadOnlyList<ResumedTransferIssue> Issues,
    IReadOnlyDictionary<string, decimal> Totals);

public sealed record CheckResumedTransfersQuery(IReadOnlyList<ResumedTransferRow> Rows) : IQuery<ResumedTransferReport>;

/// <param name="OperationDate">Date de l'écriture d'ouverture (jour de mise en service) ; aujourd'hui par défaut.</param>
public sealed record ImportResumedTransfersCommand(Guid ActorId, IReadOnlyList<ResumedTransferRow> Rows, DateOnly? OperationDate)
    : ICommand<ResumedTransferResult>;

public sealed record ResumedTransferResult(int Created);

internal sealed record ParsedResumedTransfer(int Row, string Reference, ResumedTransfer Transfer, string DestinationCode);

/// <summary>
/// Vérifie chaque ligne : référence obligatoire (60 caractères), unique dans le fichier et dans la base ; date lisible et
/// jamais future ; agence d'envoi connue (la destination est l'autre agence) ; FCFA entier positif ; MRO positif à 2
/// décimales au plus ; prix de vente à 2 décimales au plus (déduit des montants s'il manque) ; expéditeur et bénéficiaire
/// obligatoires (200 caractères), note (500). Une ligne vide est ignorée ; 2 000 au plus.
/// </summary>
public sealed class ResumedTransferValidator(ITransferRepository transfers, IAgencyDirectory agencies, IClock clock)
{
    public const int MaxRows = 2_000;

    private static readonly string[] DateFormats =
    [
        "dd/MM/yyyy HH:mm", "dd/MM/yyyy H:mm", "dd/MM/yyyy", "d/M/yyyy HH:mm", "d/M/yyyy H:mm", "d/M/yyyy",
        "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd",
    ];

    internal async Task<(ResumedTransferReport Report, IReadOnlyList<ParsedResumedTransfer> Rows)> ValidateAsync(
        IReadOnlyList<ResumedTransferRow> rows, CancellationToken cancellationToken)
    {
        var filled = rows.Where(r => !IsEmpty(r)).ToList();
        if (filled.Count == 0)
        {
            return (Report(0, [], [new ResumedTransferIssue(0, null, "Transfers.Resume.Empty")]), []);
        }

        if (filled.Count > MaxRows)
        {
            return (Report(filled.Count, [], [new ResumedTransferIssue(0, null, "Transfers.Resume.TooManyRows")]), []);
        }

        var all = await agencies.GetAllAsync(cancellationToken);
        var now = clock.UtcNow;
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issues = new List<ResumedTransferIssue>();
        var parsed = new List<ParsedResumedTransfer>();

        foreach (var row in filled)
        {
            var before = issues.Count;
            void Problem(string column, string code) => issues.Add(new ResumedTransferIssue(row.Row, column, code));

            var reference = row.Reference?.Trim() ?? string.Empty;
            if (reference.Length == 0)
            {
                Problem("reference", "Transfers.Resume.ReferenceRequired");
            }
            else if (reference.Length > Transfer.ReferenceMaxLength)
            {
                Problem("reference", "Transfers.Resume.ReferenceTooLong");
            }
            else if (!references.Add(reference))
            {
                Problem("reference", "Transfers.Resume.ReferenceDuplicateInFile");
            }
            else if (await transfers.ReferenceExistsAsync(reference, cancellationToken))
            {
                Problem("reference", "Transfers.Resume.ReferenceTaken");
            }

            DateTimeOffset operationAt = default;
            if (!DateTime.TryParseExact(row.OperationAt?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Problem("operationAt", "Transfers.Resume.DateInvalid");
            }
            else
            {
                // Nouakchott et Dakar sont en UTC+0 : l'heure de l'ancienne app est l'heure UTC.
                operationAt = new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc));
                if (operationAt > now)
                {
                    Problem("operationAt", "Transfers.Transfer.FutureDate");
                }
            }

            var source = FindAgency(all, row.SourceAgency);
            var destination = source is null ? null : all.FirstOrDefault(a => a.Id != source.Id);
            if (source is null || destination is null)
            {
                Problem("sourceAgency", "Transfers.Resume.AgencyUnknown");
            }

            var xof = ReadMoney(row.AmountXof, Currency.XOF);
            if (xof is null)
            {
                Problem("amountXof", "Transfers.Resume.AmountInvalid");
            }

            var mro = ReadMoney(row.AmountMro, Currency.MRO);
            if (mro is null)
            {
                Problem("amountMro", "Transfers.Resume.AmountInvalid");
            }

            ExchangeRate? rate = null;
            if (!string.IsNullOrWhiteSpace(row.SaleRate))
            {
                if (!TryReadNumber(row.SaleRate, out var value) || ExchangeRate.Create(value) is not { IsSuccess: true } given)
                {
                    Problem("saleRate", "Transfers.Resume.RateInvalid");
                }
                else
                {
                    rate = given.Value;
                }
            }
            else if (xof is not null && mro is not null)
            {
                // Déduit des montants de l'ancienne app, à 2 décimales (pour l'affichage ; le MRO enregistré reste celui du fichier).
                rate = ExchangeRate.Create(Math.Round(mro.Amount * 5000m / xof.Amount, 2, MidpointRounding.AwayFromZero)).Value;
            }

            if (issues.Count > before || source is null || destination is null || xof is null || mro is null || rate is null)
            {
                continue;
            }

            var resumed = new ResumedTransfer(
                source.Id, source.Currency, destination.Id, destination.Currency, xof, mro, rate,
                row.SenderInfo, row.BeneficiaryInfo, row.Note, operationAt);
            // Mêmes règles qu'à la création (textes, corridor) : contrôlées sur un transfert d'essai, jamais enregistré.
            var trial = Transfer.Resume(resumed, reference, "essai", "essai", Guid.Empty, now);
            if (trial.IsFailure)
            {
                Problem(ColumnOf(trial.Error.Code), trial.Error.Code);
                continue;
            }

            parsed.Add(new ParsedResumedTransfer(row.Row, reference, resumed, destination.Code));
        }

        return (Report(filled.Count, parsed, issues), parsed);
    }

    private static ResumedTransferReport Report(int rowCount, IReadOnlyList<ParsedResumedTransfer> parsed, IReadOnlyList<ResumedTransferIssue> issues) =>
        new(
            rowCount,
            parsed.Count,
            issues,
            parsed.GroupBy(p => p.DestinationCode)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(p => p.Transfer.DestinationCurrency == Currency.XOF ? p.Transfer.AmountXof.Amount : p.Transfer.AmountMro.Amount),
                    StringComparer.Ordinal));

    private static AgencySummary? FindAgency(IReadOnlyList<AgencySummary> agencies, string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return agencies.FirstOrDefault(a =>
            string.Equals(a.Code, value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(a.Name, value, StringComparison.OrdinalIgnoreCase)
            || (a.Code == "NKC" && value is "Nouakchott" or "نواكشوط")
            || (a.Code == "DKR" && value is "Dakar" or "داكار"));
    }

    private static Money? ReadMoney(string? text, Currency currency) =>
        TryReadNumber(text, out var value) && value > 0m && Money.Create(value, currency) is { IsSuccess: true } money ? money.Value : null;

    private static bool TryReadNumber(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = new string([.. text.Trim().Where(c => !char.IsWhiteSpace(c))]).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsEmpty(ResumedTransferRow row) =>
        string.IsNullOrWhiteSpace(row.Reference) && string.IsNullOrWhiteSpace(row.AmountXof) && string.IsNullOrWhiteSpace(row.AmountMro)
        && string.IsNullOrWhiteSpace(row.SenderInfo) && string.IsNullOrWhiteSpace(row.BeneficiaryInfo);

    private static string ColumnOf(string code) => code switch
    {
        _ when code.Contains("Sender", StringComparison.Ordinal) => "senderInfo",
        _ when code.Contains("Beneficiary", StringComparison.Ordinal) => "beneficiaryInfo",
        _ when code.Contains("TextTooLong", StringComparison.Ordinal) => "note",
        _ when code.Contains("Amount", StringComparison.Ordinal) => "amountXof",
        _ when code.Contains("FutureDate", StringComparison.Ordinal) => "operationAt",
        _ => "sourceAgency",
    };
}
