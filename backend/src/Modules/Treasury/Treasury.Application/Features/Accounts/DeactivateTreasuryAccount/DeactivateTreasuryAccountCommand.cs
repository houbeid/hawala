using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.DeactivateTreasuryAccount;

/// <summary>Le Superviseur désactive un compte de trésorerie : seulement à solde nul (règle validée).</summary>
public sealed record DeactivateTreasuryAccountCommand(Guid AccountId) : ICommand;
