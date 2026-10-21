using Balance.Domain;

namespace Balance.Application.Abstractions;

/// <summary>Enregistre les modifications du module (connexion propre : le Bilan n'écrit rien au grand livre).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Saisies du capital initial, par caisse (la plus récente de la caisse fait foi).</summary>
public interface ICapitalRepository
{
    void Add(CapitalChange change);

    Task<CapitalChange?> GetCurrentAsync(Guid agencyId, CancellationToken cancellationToken);

    /// <summary>Toutes les saisies des caisses, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<CapitalChange>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Photos du bilan, par caisse.</summary>
public interface IBalanceSnapshotRepository
{
    void Add(BalanceSnapshot snapshot);

    Task<BalanceSnapshot?> GetAsync(Guid agencyId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Photos d'une période (d'une caisse, ou des deux), de la plus ancienne à la plus récente.</summary>
    Task<IReadOnlyList<BalanceSnapshot>> ListAsync(Guid? agencyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
