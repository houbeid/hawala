using Hawala.SharedKernel.Money;

namespace Ledger.Contracts;

public enum EntrySide
{
    Debit,
    Credit,
}

/// <summary>
/// Une écriture demandée par un autre module (création de transfert, encaissement, solde initial…).
/// Elle doit être équilibrée pour chaque devise : Σ débits = Σ crédits.
/// </summary>
/// <param name="OperationDate">
/// Date de l'opération, dans le calendrier de l'agence. Peut être passée (règle validée) ;
/// l'instant d'enregistrement (UTC) est ajouté par le grand livre.
/// </param>
/// <param name="EntryType">Code stable choisi par le module appelant (ex. « Transfers.Created », « Parties.OpeningBalance »).</param>
/// <param name="SourceModule">Module à l'origine de l'écriture (ex. « Transfers »).</param>
/// <param name="SourceId">Identifiant de l'objet source (le transfert, le mouvement de caisse…), s'il y en a un.</param>
/// <param name="AuthorId">Compte connecté qui a fait l'action (lu dans le JWT par l'API).</param>
/// <param name="Label">Libellé lisible dans le relevé.</param>
public sealed record PostEntryRequest(
    DateOnly OperationDate,
    string EntryType,
    string SourceModule,
    Guid? SourceId,
    Guid AuthorId,
    string Label,
    IReadOnlyList<PostEntryLine> Lines);

/// <param name="Amount">Strictement positif ; sa devise doit être celle du compte.</param>
public sealed record PostEntryLine(Guid AccountId, EntrySide Side, Money Amount);
