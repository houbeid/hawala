using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;

namespace Cash.Application.Features.Movements.CreateCashMovement;

/// <summary>Nouveau mouvement manuel de caisse (permission cash.create : Superviseur, Agent, Caissier).</summary>
/// <param name="IdempotencyKey">En-tête Idempotency-Key, obligatoire.</param>
/// <param name="AgencyId">Agence : celle de l'agent (facultative pour lui) ; obligatoire pour le Superviseur.</param>
/// <param name="Type">Receipt, Payment, OwnerContribution ou OwnerWithdrawal.</param>
/// <param name="TreasuryAccountId">Méthode de paiement : compte de trésorerie actif de l'agence.</param>
/// <param name="PartyId">Tiers (« Source ») : obligatoire pour un encaissement ou un décaissement, interdit sinon.</param>
/// <param name="Unit">MRO (défaut) ou MRU (converti × 10, agence en ouguiya seulement).</param>
/// <param name="OperationAt">Date et heure de l'opération ; maintenant par défaut.</param>
public sealed record CreateCashMovementCommand(
    Guid ActorId,
    string? IdempotencyKey,
    Guid? AgencyId,
    string? Type,
    Guid? TreasuryAccountId,
    Guid? PartyId,
    decimal? Amount,
    string? Unit,
    string? Note,
    DateTimeOffset? OperationAt) : ICommand<CreatedCashMovement>;

/// <param name="Replayed">Vrai si la clé d'idempotence avait déjà servi : rien n'a été enregistré.</param>
public sealed record CreatedCashMovement(Guid Id, string Type, decimal Amount, Currency Currency, bool Replayed);
