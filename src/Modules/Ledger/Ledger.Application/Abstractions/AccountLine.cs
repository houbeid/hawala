using Ledger.Contracts;

namespace Ledger.Application.Abstractions;

/// <summary>Une ligne d'écriture d'un compte, avec ce que le relevé affiche de son écriture.</summary>
public sealed record AccountLine(
    Guid EntryId,
    DateOnly OperationDate,
    DateTimeOffset RecordedAt,
    string EntryType,
    string Label,
    string? Reason,
    bool IsReversal,
    EntrySide Side,
    decimal Amount);
