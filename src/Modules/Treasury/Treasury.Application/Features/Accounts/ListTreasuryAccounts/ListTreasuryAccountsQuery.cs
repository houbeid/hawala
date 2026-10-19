using Hawala.SharedKernel.Messaging;

namespace Treasury.Application.Features.Accounts.ListTreasuryAccounts;

/// <summary>
/// Comptes de trésorerie visibles par le compte connecté : ceux de son agence pour un agent, des deux pour
/// le Superviseur (périmètre appliqué par le filtre global, pas par ces paramètres).
/// </summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : décide si les soldes sont montrés.</param>
/// <param name="Type">Cash, Bank ou Wallet.</param>
public sealed record ListTreasuryAccountsQuery(
    Guid ActorId,
    Guid? AgencyId = null,
    string? Type = null,
    bool IncludeInactive = false) : IQuery<IReadOnlyList<TreasuryAccountSummary>>;
