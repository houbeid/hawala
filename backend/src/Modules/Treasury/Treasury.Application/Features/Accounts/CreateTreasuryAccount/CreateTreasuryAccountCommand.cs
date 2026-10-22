using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.CreateTreasuryAccount;

/// <summary>Le Superviseur crée une caisse, une banque ou un wallet dans une agence (permission treasury.manage).</summary>
/// <param name="Type">Cash, Bank ou Wallet.</param>
public sealed record CreateTreasuryAccountCommand(string? Name, string? Type, Guid? AgencyId) : ICommand<Guid>;
