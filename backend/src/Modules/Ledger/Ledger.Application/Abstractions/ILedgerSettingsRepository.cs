using Ledger.Domain.Settings;

namespace Ledger.Application.Abstractions;

/// <summary>La ligne unique des réglages du grand livre (absente tant que la date de démarrage n'est pas fixée).</summary>
public interface ILedgerSettingsRepository
{
    Task<LedgerSettings?> GetAsync(CancellationToken cancellationToken);

    void Add(LedgerSettings settings);
}
