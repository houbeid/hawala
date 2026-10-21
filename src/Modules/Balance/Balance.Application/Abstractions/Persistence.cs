using Balance.Domain;

namespace Balance.Application.Abstractions;

/// <summary>Enregistre les modifications du module (connexion propre : le Bilan n'écrit rien au grand livre).</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Saisies du capital initial (la plus récente fait foi).</summary>
public interface ICapitalRepository
{
    void Add(CapitalChange change);

    Task<CapitalChange?> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>Toutes les saisies, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<CapitalChange>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>Photos du bilan général.</summary>
public interface IBalanceSnapshotRepository
{
    void Add(BalanceSnapshot snapshot);

    Task<BalanceSnapshot?> GetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Photos d'une période, de la plus ancienne à la plus récente (ordre de la courbe).</summary>
    Task<IReadOnlyList<BalanceSnapshot>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
