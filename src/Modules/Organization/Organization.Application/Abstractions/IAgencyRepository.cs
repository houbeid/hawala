using Organization.Domain.Agencies;

namespace Organization.Application.Abstractions;

public interface IAgencyRepository
{
    /// <summary>Toutes les agences, triées par code (DKR, NKC).</summary>
    Task<IReadOnlyList<Agency>> GetAllAsync(CancellationToken cancellationToken);

    Task<Agency?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}