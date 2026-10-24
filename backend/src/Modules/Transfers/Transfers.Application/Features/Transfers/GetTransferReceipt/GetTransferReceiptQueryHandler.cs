using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Time;
using Identity.Contracts;
using Organization.Contracts;
using Transfers.Application.Abstractions;
using Transfers.Domain;
using Transfers.Domain.Transfers;
using Treasury.Contracts;

namespace Transfers.Application.Features.Transfers.GetTransferReceipt;

/// <summary>
/// Règles du 24 octobre 2026 : le reçu se demande à l'agence d'envoi (ou par le Superviseur) ; il montre la répartition
/// du paiement et la part à crédit, jamais les taux, la note ni le nom du tiers. Le code n'y est écrit que pour le compte
/// qui l'a obtenu (créateur ou auteur du dernier nouveau code), sur un transfert non livré : ce compte a déjà vu le code,
/// donc un code faux est refusé sans être compté (aucune information à deviner). Hors périmètre : 404.
/// </summary>
public sealed class GetTransferReceiptQueryHandler(
    ITransferRepository transfers,
    IWithdrawalCodeHasher codeHasher,
    IAgencyScope scope,
    IAgencyDirectory agencies,
    ITreasuryDirectory treasury,
    ITransferReceiptRenderer renderer,
    IClock clock)
    : IQueryHandler<GetTransferReceiptQuery, TransferReceiptFile>
{
    public async Task<Result<TransferReceiptFile>> Handle(GetTransferReceiptQuery query, CancellationToken cancellationToken)
    {
        var transfer = await transfers.GetByIdAsync(query.TransferId, cancellationToken);
        if (transfer is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        if (!scope.SeesAllAgencies && scope.AgencyId != transfer.SourceAgencyId)
        {
            return TransfersErrors.Receipt.NotSendingAgency;
        }

        if (query.Code is not null)
        {
            var checkedCode = CheckCode(transfer, query.ActorId, query.Code);
            if (checkedCode.IsFailure)
            {
                return checkedCode.Error;
            }
        }

        var source = await agencies.GetByIdAsync(transfer.SourceAgencyId, cancellationToken);
        var destination = await agencies.GetByIdAsync(transfer.DestinationAgencyId, cancellationToken);
        if (source is null || destination is null)
        {
            return TransfersErrors.Transfer.NotFound;
        }

        var paymentLines = transfer.Payments.Where(p => p.Stage == PaymentStage.Creation).ToList();
        var accounts = await treasury.GetByIdsAsync([.. paymentLines.Select(p => p.TreasuryAccountId).Distinct()], cancellationToken);
        var accountNames = accounts.ToDictionary(a => a.Id, a => a.Name);

        var receipt = new TransferReceipt(
            transfer.Reference,
            transfer.OperationAt,
            clock.UtcNow,
            IsDuplicate: query.Code is null,
            transfer.Status,
            new ReceiptAgency(source.Code, source.Name),
            new ReceiptAgency(destination.Code, destination.Name),
            transfer.SenderInfo,
            transfer.BeneficiaryInfo,
            transfer.PaidAmount,
            transfer.AmountToDeliver,
            [.. paymentLines.Select(p => new ReceiptPayment(accountNames.GetValueOrDefault(p.TreasuryAccountId, "?"), Money.Create(p.Amount, p.Currency).Value))],
            transfer.CreditMoney,
            query.Code);

        return new TransferReceiptFile($"Recu-{transfer.Reference}.pdf", renderer.Render(receipt));
    }

    private Result CheckCode(Transfer transfer, Guid actorId, string code)
    {
        if (transfer.Status != TransferStatus.NotDelivered)
        {
            return TransfersErrors.Receipt.CodeNotUsable;
        }

        if (transfer.CodeHolderId != actorId)
        {
            return TransfersErrors.Receipt.NotCodeHolder;
        }

        var wellFormed = code.Length == 6 && code.All(char.IsAsciiDigit);
        return wellFormed && codeHasher.Verify(code, transfer.WithdrawalCodeHash)
            ? Result.Success()
            : TransfersErrors.Receipt.WrongCode;
    }
}
