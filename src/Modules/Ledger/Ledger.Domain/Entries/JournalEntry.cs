using Hawala.SharedKernel.Ids;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Ledger.Contracts;
using Ledger.Domain.Accounts;

namespace Ledger.Domain.Entries;

/// <summary>Ligne demandée, avec son compte déjà chargé (pour vérifier qu'il est actif et dans la bonne devise).</summary>
public sealed record EntryLineDraft(LedgerAccount Account, EntrySide Side, Money Amount);

/// <summary>
/// Écriture en partie double. Règle centrale : pour chaque devise, Σ débits = Σ crédits.
/// Immuable une fois créée (jamais de UPDATE / DELETE, vérifié aussi en base) : une erreur se corrige
/// par une contre-passation, nouvelle écriture aux sens inversés.
/// </summary>
public sealed class JournalEntry
{
    public const int LabelMaxLength = 200;
    public const int TypeMaxLength = 50;
    public const int ReasonMaxLength = 500;

    private const string ReversalLabelPrefix = "Contre-passation : ";

    /// <summary>Taille de la colonne : un libellé saisi (200) plus le préfixe d'une contre-passation, sans jamais couper.</summary>
    public const int StoredLabelMaxLength = LabelMaxLength + 19;

    private readonly List<JournalLine> _lines = [];

    // Constructeur réservé à EF Core.
    private JournalEntry()
    {
        EntryType = null!;
        SourceModule = null!;
        Label = null!;
    }

    private JournalEntry(
        Guid id,
        DateOnly operationDate,
        DateTimeOffset recordedAt,
        string entryType,
        string sourceModule,
        Guid? sourceId,
        Guid authorId,
        string label,
        string? reason,
        Guid? reversesEntryId,
        IEnumerable<JournalLine> lines)
    {
        Id = id;
        OperationDate = operationDate;
        RecordedAt = recordedAt;
        EntryType = entryType;
        SourceModule = sourceModule;
        SourceId = sourceId;
        AuthorId = authorId;
        Label = label;
        Reason = reason;
        ReversesEntryId = reversesEntryId;
        _lines.AddRange(lines);
    }

    public Guid Id { get; private set; }

    /// <summary>Date de l'opération (calendrier de l'agence). Peut être passée : règle validée.</summary>
    public DateOnly OperationDate { get; private set; }

    /// <summary>Instant réel d'enregistrement, en UTC.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Code stable du module appelant (ex. « Transfers.Created »). Une contre-passation garde le type d'origine.</summary>
    public string EntryType { get; private set; }

    public string SourceModule { get; private set; }

    public Guid? SourceId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Label { get; private set; }

    /// <summary>Motif, obligatoire pour une contre-passation.</summary>
    public string? Reason { get; private set; }

    /// <summary>Écriture annulée par celle-ci ; null pour une écriture normale.</summary>
    public Guid? ReversesEntryId { get; private set; }

    public bool IsReversal => ReversesEntryId is not null;

    public IReadOnlyList<JournalLine> Lines => _lines;

    /// <summary>
    /// Crée une écriture après avoir vérifié toutes les règles.
    /// POINT OUVERT : une date d'opération dans le futur n'est pas refusée, faute de règle validée
    /// (proposition faite : la refuser). À trancher avec le patron, puis ajouter le contrôle ici.
    /// </summary>
    public static Result<JournalEntry> Post(
        DateOnly operationDate,
        string? entryType,
        string? sourceModule,
        Guid? sourceId,
        Guid authorId,
        string? label,
        IReadOnlyList<EntryLineDraft> lines,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var header = ValidateHeader(entryType, sourceModule, authorId, label);
        if (header.IsFailure)
        {
            return header.Error;
        }

        if (lines.Count < 2)
        {
            return LedgerErrors.Entry.TooFewLines;
        }

        foreach (var line in lines)
        {
            if (line.Amount.Amount <= 0m)
            {
                return LedgerErrors.Line.AmountNotPositive;
            }

            var accepted = line.Account.EnsureCanReceive(line.Amount);
            if (accepted.IsFailure)
            {
                return accepted.Error;
            }
        }

        var journalLines = lines
            .Select(l => new JournalLine(IdGenerator.NewId(now), l.Account.Id, l.Side, l.Amount.Amount, l.Amount.Currency))
            .ToList();

        if (!IsBalancedPerCurrency(journalLines))
        {
            return LedgerErrors.Entry.Unbalanced;
        }

        return new JournalEntry(
            IdGenerator.NewId(now),
            operationDate,
            now,
            entryType!.Trim(),
            sourceModule!.Trim(),
            sourceId,
            authorId,
            label!.Trim(),
            reason: null,
            reversesEntryId: null,
            journalLines);
    }

    /// <summary>
    /// Contre-passation : nouvelle écriture qui inverse chaque ligne ; celle-ci reste intacte.
    /// « Déjà contre-passée » n'est pas vérifié ici (une écriture immuable ne se marque pas) :
    /// l'Application le contrôle, et un index unique en base le garantit.
    /// POINT OUVERT : les comptes ne sont pas relus, donc une écriture qui touche un compte fermé depuis
    /// reste contre-passable. Choix volontaire : corriger une erreur doit rester possible.
    /// </summary>
    public Result<JournalEntry> Reverse(DateOnly operationDate, Guid authorId, string? reason, DateTimeOffset now)
    {
        if (IsReversal)
        {
            return LedgerErrors.Reversal.CannotReverseAReversal;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return LedgerErrors.Reversal.ReasonRequired;
        }

        if (authorId == Guid.Empty)
        {
            return LedgerErrors.Entry.AuthorRequired;
        }

        // Un motif sert à l'audit : trop long, il est refusé, jamais coupé.
        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > ReasonMaxLength)
        {
            return LedgerErrors.Reversal.ReasonTooLong;
        }

        // Le libellé d'origine (≤ 200) + le préfixe tiennent toujours dans la colonne (StoredLabelMaxLength).
        var label = ReversalLabelPrefix + Label;

        return new JournalEntry(
            IdGenerator.NewId(now),
            operationDate,
            now,
            EntryType,
            SourceModule,
            SourceId,
            authorId,
            label,
            trimmedReason,
            reversesEntryId: Id,
            _lines.Select(l => l.Inverted(now)));
    }

    private static Result ValidateHeader(string? entryType, string? sourceModule, Guid authorId, string? label)
    {
        if (string.IsNullOrWhiteSpace(entryType) || entryType.Trim().Length > TypeMaxLength)
        {
            return LedgerErrors.Entry.TypeEmpty;
        }

        if (string.IsNullOrWhiteSpace(sourceModule) || sourceModule.Trim().Length > TypeMaxLength)
        {
            return LedgerErrors.Entry.SourceEmpty;
        }

        if (authorId == Guid.Empty)
        {
            return LedgerErrors.Entry.AuthorRequired;
        }

        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > LabelMaxLength)
        {
            return LedgerErrors.Entry.LabelEmpty;
        }

        return Result.Success();
    }

    private static bool IsBalancedPerCurrency(IEnumerable<JournalLine> lines) =>
        lines.GroupBy(l => l.Currency).All(group => group.Sum(l => l.SignedAmount) == 0m);
}
