using Identity.Contracts;
using Parties.Contracts;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers;

/// <summary>
/// Construit les vues de transferts pour le compte connecté : masque ce qu'il n'a pas le droit de voir
/// et lit en une fois les noms des méthodes de paiement et des tiers.
/// </summary>
public sealed class TransferViews(
    IAgencyScope scope,
    IPermissionChecker permissions,
    ITreasuryDirectory treasury,
    IPartyDirectory parties)
{
    /// <param name="referenceAgencyId">Agence choisie par le Superviseur pour lire le sens (envoyé / reçu) ; ignorée pour un agent.</param>
    public async Task<IReadOnlyList<TransferView>> BuildAsync(
        Guid actorId, IReadOnlyList<Transfer> transfers, Guid? referenceAgencyId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        if (transfers.Count == 0)
        {
            return [];
        }

        var seesProfit = await permissions.HasPermissionAsync(actorId, Permissions.ProfitView, cancellationToken);
        var reference = scope.SeesAllAgencies ? referenceAgencyId : scope.AgencyId;

        var withDetails = transfers.Where(SeesSendingDetails).ToList();
        var accounts = await treasury.GetByIdsAsync(
            [.. withDetails.SelectMany(t => t.Payments).Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);
        var partyNames = await parties.GetNamesAsync(
            [.. withDetails.Where(t => t.CreditPartyId is not null).Select(t => t.CreditPartyId!.Value).Distinct()],
            cancellationToken);

        return [.. transfers.Select(t => ToView(t, seesProfit, reference, accountNames, partyNames))];
    }

    // Répartition et crédit : l'agence d'envoi et le Superviseur seulement (validé le 20 octobre 2026).
    private bool SeesSendingDetails(Transfer transfer) =>
        scope.SeesAllAgencies || scope.AgencyId == transfer.SourceAgencyId;

    private TransferView ToView(
        Transfer t,
        bool seesProfit,
        Guid? referenceAgencyId,
        IReadOnlyDictionary<Guid, string> accountNames,
        IReadOnlyDictionary<Guid, string> partyNames)
    {
        var details = SeesSendingDetails(t);

        TransferDirection? direction = referenceAgencyId == t.SourceAgencyId ? TransferDirection.Sent
            : referenceAgencyId == t.DestinationAgencyId ? TransferDirection.Received
            : null;

        var payments = details
            ? t.Payments
                .Where(p => p.Stage == PaymentStage.Creation)
                .Select(p => new TransferPaymentView(
                    p.TreasuryAccountId, accountNames.GetValueOrDefault(p.TreasuryAccountId), p.Amount, p.Currency))
                .ToList()
            : null;

        var credit = details && t.CreditPartyId is { } partyId
            ? new TransferCreditView(partyId, partyNames.GetValueOrDefault(partyId), t.CreditAmount, t.SourceCurrency)
            : null;

        return new TransferView(
            t.Id, t.Reference, t.Status, direction,
            t.SourceAgencyId, t.SourceCurrency, t.DestinationAgencyId, t.DestinationCurrency,
            t.AmountXof, t.AmountMro, t.SaleRate.MroPer5000Xof,
            seesProfit ? t.PurchaseRate.MroPer5000Xof : null,
            seesProfit ? t.ProfitMro : null,
            t.SenderInfo, t.BeneficiaryInfo, t.Note, t.OperationAt, t.CreatedAt,
            payments, credit, t.DeliveryOperationAt, t.IsCodeLocked, t.DeliveredWithoutCode, t.CancellationOperationAt, t.CancellationReason, t.Version,
            t.IsResumed);
    }
}
