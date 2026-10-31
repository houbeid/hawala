using Balance.Domain;
using Hawala.SharedKernel.Money;

namespace Balance.Domain.Tests;

/// <summary>« Expliquer la variation » (décision du 31 octobre 2026) : décomposition exacte de l'écart du bilan général.</summary>
public class GeneralBalanceVariationTests
{
    private static GeneralBalanceFigures Figures(CaisseComponents nouakchott, CaisseComponents dakar, decimal rate, decimal? capital) =>
        GeneralBalanceFigures.Compute(nouakchott, dakar, ExchangeRate.Create(rate).Value, capital);

    [Fact]
    public void Each_component_and_the_rate_explain_exactly_the_change()
    {
        var before = Figures(
            new CaisseComponents(Currency.MRO, 10_000_000m, 50_000_000m, 8_000_000m, 3_000_000m, 1_000_000m, 0m),
            new CaisseComponents(Currency.XOF, 20_000_000m, 5_000_000m, 1_000_000m, 2_000_000m, 4_000_000m, 0m),
            3600m,
            80_000_000m);
        var after = Figures(
            new CaisseComponents(Currency.MRO, 11_000_000m, 50_000_000m, 7_500_000m, 3_200_000m, 1_000_000m, 0m),
            new CaisseComponents(Currency.XOF, 18_000_000m, 5_000_000m, 1_000_000m, 2_000_000m, 6_000_000m, 0m),
            3675.25m,
            80_000_000m);

        var variation = GeneralBalanceVariation.Between(before, after);

        // Espèces : +1 000 000 à Nouakchott, −2 000 000 FCFA à Dakar (−1 470 100 MRO à 3 675,25).
        var cash = variation.Components.Single(c => c.Component == BalanceComponent.Cash);
        Assert.Equal(1_000_000m, cash.NouakchottChange);
        Assert.Equal(-2_000_000m, cash.DakarChange);
        Assert.Equal(-1_470_100m, cash.DakarChangeMro);
        Assert.Equal(-470_100m, cash.Effect);

        // Dettes et non livrés pèsent en moins : 200 000 de dettes en plus = −200 000 ; 2 000 000 FCFA à livrer en plus = −1 470 100.
        Assert.Equal(-200_000m, variation.Components.Single(c => c.Component == BalanceComponent.Creditors).Effect);
        Assert.Equal(-1_470_100m, variation.Components.Single(c => c.Component == BalanceComponent.Undelivered).Effect);

        // Effet du taux sur le bilan de Dakar de la photo (20 + 5 + 1 − 2 − 4 millions de FCFA) : 20 000 000 × 75,25 ÷ 5 000.
        Assert.Equal(20_000_000m * 75.25m / 5000m, variation.RateEffect);
        Assert.Equal(after.GeneralBalance - before.GeneralBalance, variation.GeneralBalanceChange);
        Assert.Equal(variation.GeneralBalanceChange, variation.Components.Sum(c => c.Effect) + variation.RateEffect);
        Assert.Equal(0m, variation.CapitalChange);
        Assert.Equal(variation.GeneralBalanceChange, variation.NetResultChange);
    }

    [Fact]
    public void An_owner_contribution_changes_the_balance_but_not_the_net_result()
    {
        var before = Figures(
            new CaisseComponents(Currency.MRO, 10_000_000m, 0m, 0m, 0m, 0m, 0m),
            new CaisseComponents(Currency.XOF, 0m, 0m, 0m, 0m, 0m, 0m),
            3600m,
            10_000_000m);
        var after = Figures(
            new CaisseComponents(Currency.MRO, 11_000_000m, 0m, 0m, 0m, 0m, 1_000_000m),
            new CaisseComponents(Currency.XOF, 0m, 0m, 0m, 0m, 0m, 0m),
            3600m,
            10_000_000m);

        var variation = GeneralBalanceVariation.Between(before, after);

        Assert.Equal(1_000_000m, variation.GeneralBalanceChange);
        Assert.Equal(1_000_000m, variation.OwnerContributionsChange);
        Assert.Equal(0m, variation.NetResultChange);
    }

    [Fact]
    public void Without_a_capital_on_one_side_there_is_no_capital_or_net_result_change()
    {
        var empty = new CaisseComponents(Currency.MRO, 1m, 0m, 0m, 0m, 0m, 0m);
        var dakar = new CaisseComponents(Currency.XOF, 0m, 0m, 0m, 0m, 0m, 0m);

        var variation = GeneralBalanceVariation.Between(Figures(empty, dakar, 3600m, null), Figures(empty, dakar, 3600m, 5m));

        Assert.Null(variation.CapitalChange);
        Assert.Null(variation.NetResultChange);
        Assert.Equal(0m, variation.GeneralBalanceChange);
    }
}
