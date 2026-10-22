namespace Transfers.Contracts;

/// <summary>
/// Porte publique des Transferts pour le module Bénéfice : le bénéfice de chaque transfert non annulé d'une période.
/// Respecte le périmètre du compte connecté.
/// </summary>
public interface ITransferProfits
{
    /// <param name="sourceAgencyId">Transferts envoyés par cette agence (le bénéfice compte pour l'agence d'envoi) ; null : les deux.</param>
    Task<IReadOnlyList<TransferProfitLine>> ListAsync(
        DateTimeOffset from, DateTimeOffset toExclusive, Guid? sourceAgencyId, CancellationToken cancellationToken);
}

/// <summary>Un transfert et son bénéfice (formules validées, pleine précision).</summary>
/// <param name="OperationAt">Date et heure de l'opération : le jour du bénéfice.</param>
public sealed record TransferProfitLine(
    Guid TransferId,
    string Reference,
    Guid SourceAgencyId,
    Guid DestinationAgencyId,
    DateTimeOffset OperationAt,
    decimal AmountXof,
    decimal AmountMro,
    decimal SaleRate,
    decimal PurchaseRate,
    decimal ProfitMro);
