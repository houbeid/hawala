using Hawala.SharedKernel.Results;

namespace Ledger.Domain.Settings;

/// <summary>
/// Réglages du grand livre : une seule ligne. Date de démarrage (phase 8, décision du 24 octobre 2026) : fixée une fois par
/// le Superviseur à la mise en service ; toute écriture datée avant est refusée (la période de l'ancienne app est fermée).
/// Jamais dans le futur ; refusée s'il existe déjà des écritures datées avant elle.
/// POINT OUVERT : corriger une date de démarrage déjà fixée (refusé pour l'instant).
/// </summary>
public sealed class LedgerSettings
{
    public const int SingletonId = 1;

    private LedgerSettings()
    {
    }

    public int Id { get; private set; }

    public DateOnly StartDate { get; private set; }

    /// <summary>Instant où la date a été fixée (UTC) et par qui.</summary>
    public DateTimeOffset StartDateSetAt { get; private set; }

    public Guid StartDateSetBy { get; private set; }

    public static Result<LedgerSettings> FixStartDate(DateOnly startDate, DateOnly today, Guid setBy, DateTimeOffset now)
    {
        if (startDate > today)
        {
            return LedgerErrors.StartDate.Future;
        }

        return new LedgerSettings { Id = SingletonId, StartDate = startDate, StartDateSetAt = now, StartDateSetBy = setBy };
    }
}
