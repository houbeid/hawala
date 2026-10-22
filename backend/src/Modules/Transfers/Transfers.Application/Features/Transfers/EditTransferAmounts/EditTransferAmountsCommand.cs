using Hawala.SharedKernel.Messaging;
using Transfers.Application.Features.Transfers.CreateTransfer;

namespace Transfers.Application.Features.Transfers.EditTransferAmounts;

/// <summary>
/// Modification d'argent d'un transfert non livré par le Superviseur (permission transfers.edit_rate).
/// Montant, prix de vente et date absents = inchangés ; répartition et crédit = état complet (crédit absent = comptant).
/// </summary>
/// <param name="Version">Version lue avec la fiche : obligatoire ; si elle a changé, conflit (409).</param>
/// <param name="SaleRate">Prix de vente de ce transfert (MRO pour 5 000 FCFA) ; le prix d'achat ne change jamais.</param>
/// <param name="Reason">Motif obligatoire (contre-passation au grand livre).</param>
public sealed record EditTransferAmountsCommand(
    Guid ActorId,
    Guid TransferId,
    int? Version,
    decimal? AmountXof,
    decimal? SaleRate,
    DateTimeOffset? OperationAt,
    IReadOnlyList<CreateTransferPayment>? Payments,
    Guid? CreditPartyId,
    string? Reason) : ICommand;
