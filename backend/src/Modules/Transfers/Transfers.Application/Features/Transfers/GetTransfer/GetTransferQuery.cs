using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.GetTransfer;

/// <summary>Fiche d'un transfert envoyé ou reçu par l'agence du compte (toutes pour le Superviseur).</summary>
public sealed record GetTransferQuery(Guid ActorId, Guid TransferId) : IQuery<TransferView>;
