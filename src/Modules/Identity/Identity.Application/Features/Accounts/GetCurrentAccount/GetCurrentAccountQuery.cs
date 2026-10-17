using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Accounts.GetCurrentAccount;

/// <param name="AccountId">Compte connecté, lu par l'API dans le JWT.</param>
public sealed record GetCurrentAccountQuery(Guid AccountId) : IQuery<CurrentAccount>;

/// <summary>Ce que l'interface affiche sur l'utilisateur connecté (nom, rôle…).</summary>
public sealed record CurrentAccount(
    Guid Id,
    string Email,
    string? Phone,
    string FullName,
    string Role,
    bool MustChangePassword);