using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;

namespace Treasury.Application.Features.Accounts.GetTreasuryAccountStatement;

/// <summary>
/// Relevé d'un compte de trésorerie (décision du 22 octobre 2026) : solde au début, chaque écriture avec le solde après,
/// solde à la fin. Lu au grand livre, il montre TOUS les mouvements du compte (paiements et livraisons de transferts,
/// mouvements de caisse, ajustements, contre-passations). Sans dates : le mois en cours, du 1er à aujourd'hui.
/// </summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : il faut cash.budget.read, comme pour les soldes.</param>
public sealed record GetTreasuryAccountStatementQuery(
    Guid ActorId,
    Guid AccountId,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<TreasuryAccountStatement>;

/// <param name="OpeningBalance">Solde à la veille de <paramref name="From"/> (positif = argent sur le compte).</param>
public sealed record TreasuryAccountStatement(
    Guid AccountId,
    string AccountName,
    string Type,
    Guid AgencyId,
    Currency Currency,
    bool IsActive,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<TreasuryStatementLine> Lines,
    decimal ClosingBalance);

/// <param name="EntryType">Type d'écriture stable (Transfers.Created, Cash.Receipt…), pour que l'interface le traduise.</param>
/// <param name="Label">Libellé écrit au grand livre (en français, avec la référence d'un transfert).</param>
/// <param name="Debit">Argent qui entre sur le compte.</param>
/// <param name="Credit">Argent qui sort du compte.</param>
public sealed record TreasuryStatementLine(
    DateOnly OperationDate,
    DateTimeOffset RecordedAt,
    string EntryType,
    string Label,
    string? Reason,
    bool IsReversal,
    decimal Debit,
    decimal Credit,
    decimal BalanceAfter);
