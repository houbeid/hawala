using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.ReactivateTreasuryAccount;

/// <summary>Le Superviseur réactive un compte de trésorerie désactivé (règle validée : réactivation possible).</summary>
public sealed record ReactivateTreasuryAccountCommand(Guid AccountId) : ICommand;
