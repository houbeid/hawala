using Hawala.SharedKernel.Money;
using Transfers.Domain.Transfers;

namespace Transfers.Application.Abstractions;

/// <summary>Met le reçu de l'expéditeur en PDF (QuestPDF dans l'Infrastructure).</summary>
public interface ITransferReceiptRenderer
{
    byte[] Render(TransferReceipt receipt);
}

/// <summary>
/// Ce que montre le reçu de l'expéditeur (règles du 24 octobre 2026) : jamais les taux, la note ni le nom du tiers à crédit.
/// </summary>
/// <param name="IssuedAt">Instant où le reçu est produit (écrit en pied de page).</param>
/// <param name="IsDuplicate">Reçu sans code, demandé après la création.</param>
/// <param name="CreditAmount">Part laissée à crédit (montant seul) ; null si payé comptant.</param>
/// <param name="WithdrawalCode">Code de retrait, seulement sur le reçu demandé par le compte qui le détient.</param>
public sealed record TransferReceipt(
    string Reference,
    DateTimeOffset OperationAt,
    DateTimeOffset IssuedAt,
    bool IsDuplicate,
    TransferStatus Status,
    ReceiptAgency Source,
    ReceiptAgency Destination,
    string SenderInfo,
    string BeneficiaryInfo,
    Money PaidAmount,
    Money AmountToDeliver,
    IReadOnlyList<ReceiptPayment> Payments,
    Money? CreditAmount,
    string? WithdrawalCode);

/// <param name="Code">NKC ou DKR : le reçu écrit le nom de la ville en français et en arabe à partir du code.</param>
public sealed record ReceiptAgency(string Code, string Name);

/// <summary>Une ligne de la répartition du paiement du client : compte de trésorerie et montant.</summary>
public sealed record ReceiptPayment(string AccountName, Money Amount);
