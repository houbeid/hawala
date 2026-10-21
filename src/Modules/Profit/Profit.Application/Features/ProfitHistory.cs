using Profit.Application.Abstractions;
using Profit.Contracts;

namespace Profit.Application.Features;

/// <summary>Implémentation de la porte publique du Bénéfice pour le Bilan.</summary>
public sealed class ProfitHistory(IProfitSnapshotRepository snapshots) : IProfitHistory
{
    public Task<decimal> SumSavedProfitsAsync(CancellationToken cancellationToken) =>
        snapshots.SumGeneralProfitsAsync(cancellationToken);
}
