using Profit.Domain.Snapshots;

namespace Profit.Domain.Tests.Snapshots;

public class ProfitSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

    [Fact]
    public void A_snapshot_keeps_the_figures_of_the_day()
    {
        var snapshot = ProfitSnapshot.Create(Nkc, new DateOnly(2026, 10, 21), new ProfitFigures(24, 581_509_320m, 3600m, 536_893m), Supervisor, Now);

        Assert.Equal(Nkc, snapshot.AgencyId);
        Assert.Equal(24, snapshot.TransferCount);
        Assert.Equal(536_893m, snapshot.ProfitMro);
        Assert.Equal(3600m, snapshot.PurchaseRate);
        Assert.Equal(Supervisor, snapshot.SavedBy);
    }

    [Fact]
    public void Saving_again_replaces_the_figures_the_author_and_the_time()
    {
        var snapshot = ProfitSnapshot.Create(null, new DateOnly(2026, 10, 21), new ProfitFigures(1, 5_000_000m, 3600m, 50_000m), Supervisor, Now);
        var other = Guid.Parse("0199f0a0-0000-7000-8000-00000000b009");

        snapshot.Replace(new ProfitFigures(2, 6_000_000m, null, 40_000m), other, Now.AddHours(1));

        Assert.Null(snapshot.AgencyId);
        Assert.Equal(40_000m, snapshot.ProfitMro);
        Assert.Null(snapshot.PurchaseRate);
        Assert.Equal(other, snapshot.SavedBy);
        Assert.Equal(Now.AddHours(1), snapshot.SavedAt);
    }
}
