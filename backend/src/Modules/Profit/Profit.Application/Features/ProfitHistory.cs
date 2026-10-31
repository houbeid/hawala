using Profit.Application.Abstractions;
using Profit.Contracts;
using Profit.Domain.Snapshots;

namespace Profit.Application.Features;

/// <summary>Implémentation de la porte publique du Bénéfice pour le Bilan.</summary>
public sealed class ProfitHistory(IProfitSnapshotRepository snapshots) : IProfitHistory
{
    public Task<decimal> SumSavedProfitsAsync(Guid agencyId, CancellationToken cancellationToken) =>
        snapshots.SumProfitsAsync(agencyId, cancellationToken);

    public async Task<decimal> SumSavedProfitsOfBothAgenciesAsync(CancellationToken cancellationToken) =>
        SavedProfits.OfBothAgencies(await snapshots.ListAllAsync(cancellationToken));
}
