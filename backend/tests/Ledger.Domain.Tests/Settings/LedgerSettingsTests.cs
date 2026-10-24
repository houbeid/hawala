using Ledger.Domain;
using Ledger.Domain.Settings;

namespace Ledger.Domain.Tests.Settings;

/// <summary>Date de démarrage (phase 8) : jamais future ; le jour même est accepté.</summary>
public class LedgerSettingsTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Today_or_a_past_day_can_be_the_start_date()
    {
        var settings = LedgerSettings.FixStartDate(Today, Today, Guid.Empty, Now).Value;

        Assert.Equal(Today, settings.StartDate);
        Assert.Equal(LedgerSettings.SingletonId, settings.Id);
        Assert.True(LedgerSettings.FixStartDate(Today.AddDays(-30), Today, Guid.Empty, Now).IsSuccess);
    }

    [Fact]
    public void A_future_start_date_is_refused() =>
        Assert.Equal(LedgerErrors.StartDate.Future, LedgerSettings.FixStartDate(Today.AddDays(1), Today, Guid.Empty, Now).Error);
}
