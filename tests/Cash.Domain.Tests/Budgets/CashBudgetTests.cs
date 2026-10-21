using Cash.Domain.Budgets;
using Hawala.SharedKernel.Money;

namespace Cash.Domain.Tests.Budgets;

public class CashBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Cashier = Guid.Parse("0199f0a0-0000-7000-8000-00000000b003");

    [Fact]
    public void Budget_subtracts_creditors_and_undelivered_transfers()
    {
        // Chiffres de la capture « Bilan par caisse » (Dakar), plus 10 000 000 FCFA non livrés.
        var figures = new CashBudgetFigures(35_492_511m, 52_232_424m, 242_899_766m, 235_035_941m, 10_000_000m);

        Assert.Equal(85_588_760m, figures.Budget);
    }

    [Fact]
    public void Saving_again_the_same_day_replaces_the_figures_and_the_author()
    {
        var snapshot = CashBudgetSnapshot.Create(
            Dkr, new DateOnly(2026, 10, 21), Currency.XOF, new CashBudgetFigures(100m, 0m, 0m, 0m, 0m), Cashier, Now);
        var later = Now.AddHours(1);
        var supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

        snapshot.Replace(new CashBudgetFigures(100m, 50m, 10m, 5m, 20m), supervisor, later);

        Assert.Equal(135m, snapshot.Budget);
        Assert.Equal(supervisor, snapshot.SavedBy);
        Assert.Equal(later, snapshot.SavedAt);
        Assert.Equal(new DateOnly(2026, 10, 21), snapshot.Date);
    }
}
