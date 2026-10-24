using Hawala.SharedKernel.Money;

namespace Parties.Application.Features.Parties;

/// <summary>Un tiers tel que l'interface l'affiche (liste et fiche).</summary>
/// <param name="Type">Client, Supplier, Staff ou Other.</param>
/// <param name="AccountCode">Compte comptable déduit du type (41, 40, 42), null pour « Autre ».</param>
/// <param name="Balances">
/// Solde du tiers dans chaque agence visible (débit − crédit : positif = rouge, il nous doit ; négatif = vert, on lui doit).
/// Null si le compte connecté n'a pas la permission parties.view_balance (règle validée).
/// </param>
public sealed record PartySummary(
    Guid Id,
    string Name,
    string Type,
    string? AccountCode,
    string? Phone,
    string? Nni,
    string? Address,
    string? Note,
    bool InBalanceSheet,
    bool IsArchived,
    IReadOnlyList<PartyAgencyBalance>? Balances);

/// <param name="Currency">MRO à Nouakchott, XOF à Dakar.</param>
/// <param name="CreditLimit">Plafond de crédit dans cette agence ; 0 = aucun crédit ; null = pas de plafond.</param>
/// <param name="OpeningBalance">
/// Solde initial retenu dans cette agence (même sens que le solde). Renvoyé pour que le Superviseur voie la valeur
/// actuelle avant de la changer : la modification envoie la nouvelle valeur et le serveur écrit la différence
/// (décision A du 22 octobre 2026). Même visibilité que le solde.
/// </param>
public sealed record PartyAgencyBalance(Guid AgencyId, Currency Currency, decimal Balance, decimal? CreditLimit, decimal OpeningBalance);

/// <summary>La liste, avec ce que l'écran de l'app actuelle affiche en tête : « 12 / 73 » et les totaux.</summary>
/// <param name="Count">Nombre de tiers renvoyés (après filtres).</param>
/// <param name="TotalCount">Nombre total de tiers (sans filtre, archivés exclus sauf demande).</param>
/// <param name="Totals">Totaux par devise des tiers renvoyés ; null si les soldes ne sont pas visibles.</param>
public sealed record PartyList(
    IReadOnlyList<PartySummary> Items,
    int Count,
    int TotalCount,
    IReadOnlyList<PartyTotals>? Totals);

/// <summary>Totaux d'une devise. Jamais d'addition MRO + FCFA.</summary>
/// <param name="Red">Somme des soldes positifs (ils nous doivent).</param>
/// <param name="Green">Somme des soldes négatifs (on leur doit), négative.</param>
/// <param name="Net">Rouges + verts (= rouges − |verts|), comme le total de l'app actuelle.</param>
public sealed record PartyTotals(Currency Currency, decimal Red, decimal Green, decimal Net);
