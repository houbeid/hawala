using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.RenameTreasuryAccount;

/// <summary>Le Superviseur renomme un compte de trésorerie (permission treasury.manage).</summary>
public sealed record RenameTreasuryAccountCommand(Guid AccountId, string? Name) : ICommand;
