using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;

namespace Parties.Application.Features.Parties.GetPartyStatement;

/// <summary>
/// Relevé d'un tiers dans une agence (règle validée) : solde au début, écritures avec le solde après chacune, solde à la fin.
/// Sans dates : le mois en cours, du 1er à aujourd'hui.
/// </summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : il faut parties.view_balance.</param>
/// <param name="AgencyId">Agence du relevé ; un agent n'a que la sienne (utilisée par défaut), le Superviseur doit choisir.</param>
public sealed record GetPartyStatementQuery(
    Guid ActorId,
    Guid PartyId,
    Guid? AgencyId = null,
    DateOnly? From = null,
    DateOnly? To = null) : IQuery<PartyStatement>;

/// <param name="OpeningBalance">Solde à la veille de <paramref name="From"/> (positif = rouge, il nous doit ; négatif = vert).</param>
public sealed record PartyStatement(
    Guid PartyId,
    string PartyName,
    Guid AgencyId,
    Currency Currency,
    DateOnly From,
    DateOnly To,
    decimal OpeningBalance,
    IReadOnlyList<PartyStatementLine> Lines,
    decimal ClosingBalance);

/// <param name="EntryType">
/// Type d'écriture stable (Parties.OpeningBalance, Transfers.Created, Cash.Receipt…), pour que l'interface le traduise
/// (24 octobre 2026) ; le libellé reste écrit en français par le serveur.
/// </param>
/// <param name="Debit">Augmente ce qu'il nous doit (rouge).</param>
/// <param name="Credit">Augmente ce qu'on lui doit (vert).</param>
public sealed record PartyStatementLine(
    DateOnly OperationDate,
    DateTimeOffset RecordedAt,
    string EntryType,
    string Label,
    string? Reason,
    bool IsReversal,
    decimal Debit,
    decimal Credit,
    decimal BalanceAfter);
