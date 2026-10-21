using Hawala.SharedKernel.Messaging;
using Transfers.Application.Features.Transfers.CreateTransfer;
using Transfers.Application.Features.Transfers.DeliverTransfer;

namespace Transfers.Application.Features.Transfers.DeliverWithoutCode;

/// <summary>Code perdu : le Superviseur livre sans code (permission transfers.manage_code).</summary>
/// <param name="IdDocument">Pièce d'identité du bénéficiaire (type et numéro), obligatoire.</param>
/// <param name="Reason">Motif obligatoire.</param>
public sealed record DeliverWithoutCodeCommand(
    Guid ActorId,
    Guid TransferId,
    string? IdDocument,
    string? Reason,
    DateTimeOffset? OperationAt,
    IReadOnlyList<CreateTransferPayment>? Payments,
    string? Note) : ICommand<DeliveredTransfer>;
