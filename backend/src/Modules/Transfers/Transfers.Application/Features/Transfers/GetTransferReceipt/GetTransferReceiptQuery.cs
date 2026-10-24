using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.GetTransferReceipt;

/// <summary>
/// Reçu PDF de l'expéditeur. Avec <paramref name="Code"/> : le reçu de la création, qui porte le code de retrait,
/// demandé par le compte qui détient ce code ; sans : duplicata (agence d'envoi ou Superviseur).
/// </summary>
/// <param name="Code">Code de retrait à écrire sur le reçu ; vérifié par son empreinte, jamais gardé.</param>
public sealed record GetTransferReceiptQuery(Guid ActorId, Guid TransferId, string? Code) : IQuery<TransferReceiptFile>;

/// <summary>Le PDF et son nom de fichier (« Recu-TR-NKC-20261024-000001.pdf »).</summary>
public sealed record TransferReceiptFile(string FileName, ReadOnlyMemory<byte> Content);
