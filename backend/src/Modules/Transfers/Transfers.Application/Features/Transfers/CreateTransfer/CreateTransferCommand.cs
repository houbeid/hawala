using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.CreateTransfer;

/// <summary>Création d'un transfert payé comptant par l'agence d'envoi (permission transfers.create).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT.</param>
/// <param name="IdempotencyKey">En-tête Idempotency-Key, obligatoire.</param>
/// <param name="SourceAgencyId">Agence d'envoi : celle de l'agent (facultatif pour lui) ; obligatoire pour le Superviseur.</param>
/// <param name="AmountXof">Montant de référence, en FCFA entier.</param>
/// <param name="OperationAt">Date et heure de l'opération ; maintenant par défaut.</param>
/// <param name="Payments">Répartition du paiement du client, dans la devise de l'agence d'envoi.</param>
/// <param name="CreditPartyId">
/// Crédit activé : tiers qui porte la part non payée (permission transfers.credit). Null : payé comptant.
/// </param>
public sealed record CreateTransferCommand(
    Guid ActorId,
    string? IdempotencyKey,
    Guid? SourceAgencyId,
    Guid? DestinationAgencyId,
    decimal? AmountXof,
    string? SenderInfo,
    string? BeneficiaryInfo,
    string? Note,
    DateTimeOffset? OperationAt,
    IReadOnlyList<CreateTransferPayment>? Payments,
    Guid? CreditPartyId = null) : ICommand<CreatedTransfer>;

public sealed record CreateTransferPayment(Guid TreasuryAccountId, decimal Amount);

/// <param name="WithdrawalCode">
/// Code de retrait de 6 chiffres, renvoyé UNE seule fois (à imprimer sur le reçu de l'expéditeur) ;
/// null quand la même clé d'idempotence renvoie un transfert déjà créé.
/// </param>
/// <param name="CreditAmount">Part à crédit, dans la devise de l'agence d'envoi (0 sans crédit).</param>
/// <param name="Replayed">Vrai si la clé d'idempotence avait déjà servi : rien n'a été créé.</param>
public sealed record CreatedTransfer(
    Guid Id,
    string Reference,
    string? WithdrawalCode,
    decimal AmountXof,
    decimal AmountMro,
    decimal SaleRate,
    decimal CreditAmount,
    bool Replayed);
