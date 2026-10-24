using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Import;

// Import des tiers à la mise en service (phase 8, décisions du 24 octobre 2026) : un modèle Excel rempli à partir de
// l'ancienne app ; « Vérifier » (rapport ligne par ligne, rien n'est créé) puis « Importer » (une seule transaction, tout
// ou rien). Le fichier est lu par l'API (ClosedXML) : ici, chaque cellule arrive en texte, telle qu'elle a été saisie.

/// <summary>Une ligne du fichier (numéro de ligne Excel), cellules en texte (null = vide).</summary>
/// <param name="InBalanceSheet">« Oui » / « Non » (vide = oui).</param>
/// <param name="Agencies">Solde, sens et plafond de chaque agence (par code : NKC, DKR).</param>
public sealed record PartyImportRow(
    int Row,
    string? Name,
    string? Type,
    string? Phone,
    string? Nni,
    string? Address,
    string? Note,
    string? InBalanceSheet,
    IReadOnlyList<PartyImportAgencyCells> Agencies);

/// <param name="Balance">Solde initial, positif (ou négatif sans sens).</param>
/// <param name="Direction">« Il nous doit » / « On lui doit » (ou عليه / له) ; vide si le solde est nul.</param>
/// <param name="CreditLimit">Plafond de crédit ; vide = pas de plafond, 0 = aucun crédit.</param>
public sealed record PartyImportAgencyCells(string AgencyCode, string? Balance, string? Direction, string? CreditLimit);

/// <summary>Un problème d'une ligne : colonne concernée et code d'erreur stable (traduit par l'interface).</summary>
/// <param name="Row">Numéro de ligne Excel (0 = le fichier entier).</param>
/// <param name="Column">Clé de la colonne (name, type, phone, nni, address, note, inBalanceSheet, NKC.balance…), ou null.</param>
public sealed record PartyImportIssue(int Row, string? Column, string Code);

/// <summary>Rapport de vérification : nombre de tiers lus, nombre prêts à importer, problèmes ligne par ligne.</summary>
/// <param name="Totals">Total des soldes initiaux par agence (code → somme signée), pour comparer avec l'ancienne app.</param>
public sealed record PartyImportReport(
    int RowCount,
    int ValidCount,
    IReadOnlyList<PartyImportIssue> Issues,
    IReadOnlyDictionary<string, decimal> Totals);

/// <summary>« Vérifier » : aucun tiers n'est créé.</summary>
public sealed record CheckPartyImportQuery(IReadOnlyList<PartyImportRow> Rows) : IQuery<PartyImportReport>;

/// <param name="ActorId">Superviseur connecté (auteur des écritures d'ouverture et des lignes d'audit).</param>
/// <param name="OperationDate">Date des soldes initiaux (jour de mise en service) ; aujourd'hui par défaut.</param>
public sealed record ImportPartiesCommand(Guid ActorId, IReadOnlyList<PartyImportRow> Rows, DateOnly? OperationDate)
    : ICommand<PartyImportResult>;

public sealed record PartyImportResult(int Created);
