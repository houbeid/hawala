using Treasury.Application.Abstractions;
using Treasury.Contracts;

namespace Treasury.Application.Features.Accounts;

/// <summary>Implémentation de la porte publique du module ; le périmètre est appliqué par le filtre du dépôt.</summary>
public sealed class TreasuryDirectory(ITreasuryAccountRepository accounts) : ITreasuryDirectory
{
    public async Task<IReadOnlyList<TreasuryAccountRef>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var found = await accounts.GetByIdsAsync(ids, cancellationToken);
        return [.. found.Select(a => new TreasuryAccountRef(a.Id, a.Name, a.AgencyId, a.Currency, a.LedgerAccountId, a.IsActive))];
    }

    public async Task<IReadOnlyList<TreasuryAccountItem>> ListByAgencyAsync(Guid agencyId, CancellationToken cancellationToken)
    {
        var found = await accounts.ListAsync(new TreasuryAccountFilter(agencyId, IncludeInactive: true), cancellationToken);
        return [.. found.Select(a => new TreasuryAccountItem(
            a.Id, a.Name, Enum.Parse<TreasuryAccountKind>(a.Type.ToString()), a.LedgerAccountId, a.IsActive))];
    }
}
