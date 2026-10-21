using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Transfers.Application.Features.Transfers.CreateTransfer;

namespace Transfers.Application.Features.Transfers.DeliverTransfer;

/// <summary>Livraison au bénéficiaire par l'agence de destination (permission transfers.deliver).</summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT.</param>
/// <param name="Code">Code de retrait donné par le bénéficiaire (6 chiffres).</param>
/// <param name="OperationAt">Date et heure de la livraison ; maintenant par défaut.</param>
/// <param name="Payments">Remise répartie sur les caisses de l'agence de destination, dans sa devise.</param>
/// <param name="Note">Note facultative (500 caractères au plus).</param>
public sealed record DeliverTransferCommand(
    Guid ActorId,
    Guid TransferId,
    string? Code,
    DateTimeOffset? OperationAt,
    IReadOnlyList<CreateTransferPayment>? Payments,
    string? Note) : ICommand<DeliveryOutcome>;

/// <summary>
/// Résultat d'une livraison. Un code faux est un REFUS, mais le compteur d'essais doit être enregistré :
/// le handler renvoie donc un succès technique (la transaction valide le compteur) qui porte le refus,
/// et l'API le traduit en erreur avec les essais restants.
/// </summary>
/// <param name="Refusal">Code faux ou blocage ; null si livré.</param>
/// <param name="RemainingAttempts">Essais de code restants, avec un refus.</param>
public sealed record DeliveryOutcome(DeliveredTransfer? Delivered, Error? Refusal, int RemainingAttempts)
{
    public static DeliveryOutcome Success(DeliveredTransfer delivered) => new(delivered, null, 0);

    public static DeliveryOutcome Refused(Error refusal, int remainingAttempts) => new(null, refusal, remainingAttempts);
}

/// <param name="NegativeBalances">
/// Avertissement sans blocage (règle validée) : caisses de la livraison passées en négatif, avec leur nouveau solde.
/// </param>
public sealed record DeliveredTransfer(
    Guid Id,
    string Reference,
    decimal AmountDelivered,
    Currency Currency,
    DateTimeOffset DeliveryOperationAt,
    IReadOnlyList<NegativeBalanceWarning> NegativeBalances);

public sealed record NegativeBalanceWarning(Guid TreasuryAccountId, string Name, decimal Balance, Currency Currency);
