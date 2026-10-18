using Organization.Application.Abstractions;
using Organization.Contracts;

namespace Organization.Application.Agencies;

/// <summary>Implémentation de la porte publique du module, au-dessus du repository.</summary>
public sealed class AgencyDirectory(IAgencyRepository agencies) : IAgencyDirectory
{
    public async Task<IReadOnlyList<AgencySummary>> GetAllAsync(CancellationToken cancellationToken)
    {
        var all = await agencies.GetAllAsync(cancellationToken);
        return all.Select(a => a.ToSummary()).ToList();
    }

    public async Task<AgencySummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var agency = await agencies.GetByIdAsync(id, cancellationToken);
        return agency?.ToSummary();
    }
}