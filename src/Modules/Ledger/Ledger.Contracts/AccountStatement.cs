using Hawala.SharedKernel.Money;

namespace Ledger.Contracts;

/// <summary>
/// Relevé d'un compte sur une période (dates d'opération incluses) : solde au début, lignes dans l'ordre, solde à la fin.
/// Les soldes suivent la règle du grand livre : débit − crédit.
/// </summary>
/// <param name="OpeningBalance">Solde à la veille de <paramref name="From"/>.</param>
/// <param name="ClosingBalance">Solde à la fin de <paramref name="To"/> (= début + toutes les lignes).</param>
public sealed record AccountStatement(
    Guid AccountId,
    Currency Currency,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<StatementLine> Lines,
    decimal ClosingBalance);

/// <summary>Une ligne du relevé. Un seul des deux montants est non nul.</summary>
/// <param name="BalanceAfter">Solde du compte juste après cette ligne.</param>
/// <param name="IsReversal">Vrai pour une contre-passation (son motif est dans <paramref name="Reason"/>).</param>
public sealed record StatementLine(
    Guid EntryId,
    DateOnly OperationDate,
    DateTimeOffset RecordedAt,
    string EntryType,
    string Label,
    string? Reason,
    bool IsReversal,
    decimal Debit,
    decimal Credit,
    decimal BalanceAfter);
