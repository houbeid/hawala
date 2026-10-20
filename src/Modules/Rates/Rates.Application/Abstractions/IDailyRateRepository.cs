using Rates.Domain.DailyRates;

namespace Rates.Application.Abstractions;

/// <summary>Accès aux taux du jour (un par date). Enregistrement par <see cref="IUnitOfWork"/>.</summary>
public interface IDailyRateRepository
{
    /// <summary>Le taux d'une date (suivi), ou null s'il n'a pas été fixé.</summary>
    Task<DailyRate?> GetAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Les taux fixés entre deux dates incluses, du plus récent au plus ancien. Lecture seule.</summary>
    Task<IReadOnlyList<DailyRate>> ListAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    void Add(DailyRate rate);
}

/// <summary>Enregistre en une fois les modifications du module Taux.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
