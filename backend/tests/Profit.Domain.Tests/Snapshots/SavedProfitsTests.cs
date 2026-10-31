using Profit.Domain.Snapshots;

namespace Profit.Domain.Tests.Snapshots;

/// <summary>Bénéfices enregistrés des deux agences, pour le bilan général (décision du 31 octobre 2026).</summary>
public class SavedProfitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

    private static ProfitSnapshot Saved(Guid? agencyId, int day, decimal profit) =>
        ProfitSnapshot.Create(agencyId, new DateOnly(2026, 10, day), new ProfitFigures(1, 1_000_000m, 3600m, profit), Supervisor, Now);

    [Fact]
    public void Without_a_photo_of_both_agencies_the_two_agencies_are_added()
    {
        Assert.Equal(430_074m + 100_000m, SavedProfits.OfBothAgencies([Saved(Nkc, 25, 430_074m), Saved(Dkr, 25, 100_000m)]));
    }

    [Fact]
    public void A_photo_of_both_agencies_counts_alone_for_its_day_never_twice()
    {
        var total = SavedProfits.OfBothAgencies(
        [
            Saved(Nkc, 25, 430_074m),
            Saved(Dkr, 25, 100_000m),
            Saved(null, 25, 530_074m),
            Saved(Nkc, 26, 20_000m),
            Saved(null, 27, 5_000m),
        ]);

        Assert.Equal(530_074m + 20_000m + 5_000m, total);
    }

    [Fact]
    public void Nothing_saved_gives_zero()
    {
        Assert.Equal(0m, SavedProfits.OfBothAgencies([]));
    }
}
