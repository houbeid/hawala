using Profit.Domain.Snapshots;

namespace Profit.Application.Abstractions;

/// <summary>Enregistre les modifications du module (connexion propre : le Bénéfice n'écrit rien au grand livre).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Photos du bénéfice du jour.</summary>
public interface IProfitSnapshotRepository
{
    void Add(ProfitSnapshot snapshot);

    /// <summary>La photo d'un jour pour une agence (null : les deux agences), suivie, ou null.</summary>
    Task<ProfitSnapshot?> GetAsync(Guid? agencyId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Photos d'une période pour une agence (null : les deux), du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<ProfitSnapshot>> ListAsync(Guid? agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Somme des bénéfices de toutes les photos générales (les deux agences).</summary>
    Task<decimal> SumGeneralProfitsAsync(CancellationToken cancellationToken);
}
