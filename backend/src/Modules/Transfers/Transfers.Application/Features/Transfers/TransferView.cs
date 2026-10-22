using Hawala.SharedKernel.Money;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Features.Transfers;

/// <summary>Sens d'un transfert vu depuis une agence.</summary>
public enum TransferDirection
{
    Sent,
    Received,
}

/// <summary>
/// Un transfert tel qu'on le montre (règles validées le 20 octobre 2026) :
/// - le code de retrait n'est jamais renvoyé ;
/// - prix d'achat et bénéfice seulement avec profit.view (null sinon) ;
/// - répartition du paiement et crédit seulement pour l'agence d'envoi et le Superviseur (null pour l'agence de destination).
/// </summary>
/// <param name="Direction">Envoyé ou reçu, vu depuis l'agence de l'agent (ou celle choisie par le Superviseur) ; null sinon.</param>
/// <param name="ProfitMro">Bénéfice en pleine précision (formules validées) ; null sans profit.view.</param>
/// <param name="Payments">Répartition du paiement du client ; vide pour un crédit total ; null si non visible.</param>
/// <param name="Credit">Part à crédit et son tiers ; null sans crédit ou si non visible.</param>
/// <param name="DeliveryOperationAt">Date et heure de la livraison ; null tant que non livré.</param>
/// <param name="IsCodeLocked">Bloqué après 5 codes faux (déblocage par le Superviseur).</param>
/// <param name="DeliveredWithoutCode">Livré par le Superviseur sans le code (pièce d'identité et motif gardés, non renvoyés).</param>
/// <param name="CancellationOperationAt">Date et heure de l'annulation ; null sinon.</param>
/// <param name="CancellationReason">Motif de l'annulation, visible des deux agences ; null sinon.</param>
/// <param name="Version">Version actuelle, à renvoyer avec une modification (contrôle des modifications concurrentes).</param>
public sealed record TransferView(
    Guid Id,
    string Reference,
    TransferStatus Status,
    TransferDirection? Direction,
    Guid SourceAgencyId,
    Currency SourceCurrency,
    Guid DestinationAgencyId,
    Currency DestinationCurrency,
    decimal AmountXof,
    decimal AmountMro,
    decimal SaleRate,
    decimal? PurchaseRate,
    decimal? ProfitMro,
    string SenderInfo,
    string BeneficiaryInfo,
    string? Note,
    DateTimeOffset OperationAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TransferPaymentView>? Payments,
    TransferCreditView? Credit,
    DateTimeOffset? DeliveryOperationAt,
    bool IsCodeLocked,
    bool DeliveredWithoutCode,
    DateTimeOffset? CancellationOperationAt,
    string? CancellationReason,
    int Version);

/// <param name="TreasuryAccountName">Nom de la méthode de paiement (caisse, Bankily…).</param>
public sealed record TransferPaymentView(Guid TreasuryAccountId, string? TreasuryAccountName, decimal Amount, Currency Currency);

public sealed record TransferCreditView(Guid PartyId, string? PartyName, decimal Amount, Currency Currency);

/// <summary>
/// Une page de la liste ; <see cref="TotalCount"/> compte les transferts qui répondent aux filtres,
/// <see cref="AllCount"/> tous les transferts visibles (« 24 / 392 » de l'app actuelle).
/// </summary>
public sealed record TransferList(IReadOnlyList<TransferView> Items, int Page, int PageSize, int TotalCount, int AllCount);
