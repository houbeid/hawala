using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.EditTransferTexts;

/// <summary>
/// Règle validée : les textes d'un transfert non livré se modifient par l'agence d'envoi (ou le Superviseur) ;
/// l'ancienne valeur reste dans l'historique. Le transfert est verrouillé pendant la commande, et la version lue doit être à jour.
/// </summary>
public sealed class EditTransferTextsCommandHandler(
    ITransferRepository transfers,
    IAgencyScope scope,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<EditTransferTextsCommand>
{
    public async Task<Result> Handle(EditTransferTextsCommand command, CancellationToken cancellationToken)
    {
        if (command.Version is not { } version)
        {
            return TransfersErrors.Transfer.VersionRequired;
        }

        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (!scope.SeesAllAgencies && scope.AgencyId != transfer.SourceAgencyId)
        {
            return TransfersErrors.Transfer.NotSendingAgency;
        }

        var edited = transfer.EditTexts(
            version, command.SenderInfo, command.BeneficiaryInfo, command.Note, command.Reason, command.ActorId, clock.UtcNow);
        if (edited.IsFailure)
        {
            return edited;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
