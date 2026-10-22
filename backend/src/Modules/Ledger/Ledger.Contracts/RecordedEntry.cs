namespace Ledger.Contracts;

/// <summary>En-tête d'une écriture enregistrée, pour le contrôle de cohérence.</summary>
/// <param name="SourceId">Objet source (ex. le tiers d'un solde initial).</param>
/// <param name="Amount">Total des débits de l'écriture (le montant de l'opération pour une écriture à deux lignes).</param>
public sealed record RecordedEntry(
    Guid EntryId,
    string EntryType,
    DateOnly OperationDate,
    DateTimeOffset RecordedAt,
    Guid? SourceId,
    Guid AuthorId,
    string Label,
    decimal Amount);
