using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;

namespace Transfers.Application.Features.Transfers.NewCode;

/// <summary>
/// Nouveau code de retrait (règles validées le 21 octobre 2026) : agence d'envoi ou Superviseur ; affiché une seule fois ;
/// l'ancien ne marche plus ; ne débloque pas un transfert bloqué et ne remet pas le compteur à zéro ;
/// non livré et non annulé seulement.
/// POINT OUVERT : l'audit (qui a demandé un nouveau code) arrivera avec le module Audit ; le transfert garde déjà l'auteur.
/// </summary>
public sealed class NewCodeCommandHandler(
    ITransferRepository transfers,
    IWithdrawalCodeHasher codeHasher,
    IAgencyScope scope,
    IUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<NewCodeCommand, NewWithdrawalCode>
{
    public async Task<Result<NewWithdrawalCode>> Handle(NewCodeCommand command, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetForUpdateAsync(command.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (!scope.SeesAllAgencies && scope.AgencyId != transfer.SourceAgencyId)
        {
            return TransfersErrors.Code.NotSendingAgency;
        }

        var code = WithdrawalCodes.NewCode();
        var replaced = transfer.ReplaceCode(codeHasher.Hash(code), command.ActorId, clock.UtcNow);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new NewWithdrawalCode(transfer.Id, transfer.Reference, code);
    }
}
