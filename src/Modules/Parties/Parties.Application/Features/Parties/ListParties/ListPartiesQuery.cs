using Hawala.SharedKernel.Messaging;

namespace Parties.Application.Features.Parties.ListParties;

/// <summary>
/// Liste des tiers (communs aux deux agences), comme l'écran « Tiers » de l'app actuelle :
/// recherche, filtres, « 12 / 73 », totaux rouge / vert / net.
/// </summary>
/// <param name="ActorId">Compte connecté, lu dans le JWT : décide si les soldes sont montrés.</param>
/// <param name="AgencyId">Agence dont on veut les soldes (le Superviseur choisit ; un agent ne voit que la sienne).</param>
/// <param name="Type">Client, Supplier, Staff ou Other.</param>
/// <param name="BalanceSheet">All (défaut), In (« dans le bilan ») ou Out (« hors bilan »).</param>
/// <param name="Balance">
/// All (défaut, « الكل »), Red (« عليه » : il nous doit) ou Green (« له » : on lui doit), sur le solde de l'agence choisie.
/// Mêmes choix que le filtre « Solde » de l'app actuelle (vérifié le 19 octobre 2026).
/// </param>
public sealed record ListPartiesQuery(
    Guid ActorId,
    Guid? AgencyId = null,
    string? Search = null,
    string? Type = null,
    string? BalanceSheet = null,
    string? Balance = null,
    bool IncludeArchived = false) : IQuery<PartyList>;

public enum BalanceSheetFilter
{
    All,
    In,
    Out,
}

public enum BalanceFilter
{
    All,
    Red,
    Green,
}
