using Hawala.SharedKernel.Messaging;

namespace Transfers.Application.Features.Transfers.NewCode;

/// <summary>
/// L'expéditeur a perdu le code : l'agence d'envoi (permission transfers.create) ou le Superviseur en génère un nouveau.
/// </summary>
public sealed record NewCodeCommand(Guid ActorId, Guid TransferId) : ICommand<NewWithdrawalCode>;

/// <param name="WithdrawalCode">Nouveau code de 6 chiffres, renvoyé UNE seule fois ; l'ancien ne marche plus.</param>
public sealed record NewWithdrawalCode(Guid Id, string Reference, string WithdrawalCode);
