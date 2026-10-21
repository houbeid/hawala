using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.DeliverTransfer;

/// <summary>
/// Livre un transfert. Règles validées le 20 octobre 2026 :
/// - seule l'agence de destination livre ; le Superviseur peut livrer tout transfert ;
/// - un transfert à crédit non remboursé se livre quand même (la dette reste sur le tiers) ;
/// - le code de retrait est saisi par tous, Superviseur compris (livrer sans code : action à part) ;
/// - code faux compté par transfert et enregistré même si la livraison est refusée ; blocage au 5e ;
///   les erreurs de saisie (montants, caisses, date) sont vérifiées AVANT le code, pour ne pas consommer d'essai ;
/// - une seule livraison, répartie sur des caisses actives de l'agence de destination, total = montant à livrer ;
/// - date : maintenant par défaut, passée permise, jamais future ni avant le transfert ;
/// - caisse en négatif : livraison acceptée, avertissement dans la réponse ;
/// - écriture : débit « Transferts à livrer » / crédit des caisses, dans la devise de destination ;
/// - le transfert est verrouillé (FOR UPDATE) : deux livraisons simultanées sont impossibles.
/// POINT OUVERT : livrer en créditant un tiers (bénéficiaire qui laisse l'argent en dépôt) : pas encore.
/// POINT OUVERT : la date d'opération est lue en UTC ; Nouakchott et Dakar sont tous deux en UTC+0.
/// POINT OUVERT : l'audit (qui a livré) arrivera avec le module Audit ; le transfert garde déjà l'auteur.
/// </summary>
public sealed class DeliverTransferCommandHandler(
    ITransferRepository transfers,
    IWithdrawalCodeHasher codeHasher,
    IAgencyScope scope,
    DeliveryPosting posting,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<DeliverTransferCommand, DeliveryOutcome>
{
    public const string EntryType = DeliveryPosting.EntryType;

    public async Task<Result<DeliveryOutcome>> Handle(DeliverTransferCommand command, CancellationToken cancellationToken)
    {
        var code = command.Code?.Trim();
        if (string.IsNullOrEmpty(code))
        {
            return TransfersErrors.Delivery.CodeRequired;
        }

        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (!scope.SeesAllAgencies && scope.AgencyId != transfer.DestinationAgencyId)
        {
            return TransfersErrors.Delivery.NotDestinationAgency;
        }

        var deliverable = transfer.EnsureDeliverable();
        if (deliverable.IsFailure)
        {
            return deliverable.Error;
        }

        var lines = await posting.ResolveLinesAsync(command.Payments, transfer, cancellationToken);
        if (lines.IsFailure)
        {
            return lines.Error;
        }

        var now = clock.UtcNow;
        var operationAt = command.OperationAt ?? now;
        var paymentLines = lines.Value.Select(l => l.Line).ToList();
        var valid = transfer.ValidateDelivery(paymentLines, operationAt, command.Note, now);
        if (valid.IsFailure)
        {
            return valid.Error;
        }

        if (!codeHasher.Verify(code, transfer.WithdrawalCodeHash))
        {
            // Enregistré malgré le refus : le décorateur valide la transaction, car le résultat est un succès technique.
            var refusal = transfer.RegisterWrongCode(command.ActorId, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return DeliveryOutcome.Refused(refusal, transfer.RemainingCodeAttempts);
        }

        transfer.Deliver(paymentLines, operationAt, command.Note, command.ActorId, now);

        var entry = await posting.PostAsync(transfer, lines.Value, operationAt, command.ActorId, cancellationToken);
        if (entry.IsFailure)
        {
            return entry.Error;
        }

        transfer.AttachDeliveryEntry(entry.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var warnings = await posting.NegativeBalancesAsync(lines.Value, cancellationToken);
        return DeliveryOutcome.Success(new DeliveredTransfer(
            transfer.Id,
            transfer.Reference,
            transfer.AmountToDeliver.Amount,
            transfer.DestinationCurrency,
            operationAt,
            warnings));
    }
}
