using Identity.Contracts;
using Parties.Application.Abstractions;

namespace Parties.Application.Features.Scopes;

/// <summary>
/// Tiers visibles par le compte connecté, lus une fois par requête (pas de cache entre requêtes : effet immédiat).
/// Un Superviseur voit tout, même s'il garde une ancienne règle d'avant sa promotion.
/// Sans compte connecté (tâche de fond, appel interne) : pas de limite. Cette règle ne fait que restreindre
/// un compte limité ; le périmètre par agence (soldes) reste appliqué à part, et lui ne voit rien sans session.
/// </summary>
public sealed class PartyVisibility(ICurrentAccount current, IAgencyScope agencyScope, IPartyScopeRepository scopes)
{
    private VisibleParties? _visible;

    /// <summary>
    /// Agence d'un agent connecté : il ne voit que les tiers rattachés à elle ou aux deux (décision du 2 novembre 2026).
    /// Null pour le Superviseur et hors requête (aucune restriction).
    /// </summary>
    public Guid? HomeAgencyOnly =>
        current.AccountId is not null && !agencyScope.SeesAllAgencies ? agencyScope.AgencyId : null;

    /// <summary>
    /// Un compte connecté autre que le Superviseur ne voit pas les tiers hors bilan (décision du 4 novembre 2026).
    /// </summary>
    public bool BalanceSheetOnly => current.AccountId is not null && !agencyScope.SeesAllAgencies;

    public async Task<VisibleParties> GetAsync(CancellationToken cancellationToken)
    {
        if (_visible is not null)
        {
            return _visible;
        }

        _visible = current.AccountId is { } accountId && !agencyScope.SeesAllAgencies
            ? await scopes.GetVisibleAsync(accountId, cancellationToken)
            : VisibleParties.All;

        return _visible;
    }
}
