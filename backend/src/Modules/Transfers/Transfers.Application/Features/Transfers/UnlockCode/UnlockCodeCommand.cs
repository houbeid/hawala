using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.UnlockCode;

/// <summary>Le Superviseur débloque un transfert bloqué par 5 codes faux (permission transfers.manage_code).</summary>
/// <param name="Reason">Motif facultatif (500 caractères au plus).</param>
public sealed record UnlockCodeCommand(Guid ActorId, Guid TransferId, string? Reason) : ICommand;
