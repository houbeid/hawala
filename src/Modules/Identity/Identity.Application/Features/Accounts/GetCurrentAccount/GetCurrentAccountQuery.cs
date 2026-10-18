using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Accounts.GetCurrentAccount;

/// <param name="AccountId">Compte connecté, lu par l'API dans le JWT.</param>
public sealed record GetCurrentAccountQuery(Guid AccountId) : IQuery<CurrentAccount>;

/// <summary>Ce que l'interface affiche sur l'utilisateur connecté (nom, rôle, agence…).</summary>
/// <param name="Agency">Agence de l'agent ; null pour un Superviseur, qui voit les deux.</param>
public sealed record CurrentAccount(
    Guid Id,
    string Email,
    string? Phone,
    string FullName,
    string Role,
    bool MustChangePassword,
    CurrentAccountAgency? Agency);

/// <summary>Résumé de l'agence, pour le badge et la couleur de l'interface.</summary>
public sealed record CurrentAccountAgency(Guid Id, string Code, string Name, string Color);