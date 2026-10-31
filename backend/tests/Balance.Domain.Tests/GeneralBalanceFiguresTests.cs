using Balance.Domain;
using Hawala.SharedKernel.Money;

namespace Balance.Domain.Tests;

/// <summary>
/// Bilan général (décisions du 31 octobre 2026) : Nouakchott en MRO + Dakar converti au taux du bilan ; résultat net =
/// bilan général − (capital général + apports nets du patron). Chiffres de référence de l'ancienne app (capture du
/// 31 octobre 2026) : bilan Nouakchott 144 127 657 MRO, bilan Dakar 32 302 475 FCFA, taux 3 675,25, capital 165 500 000 MRO
/// → bilan général 167 871 591 MRO, résultat net 2 371 591 MRO (1,43 %).
/// </summary>
public class GeneralBalanceFiguresTests
{
    private static readonly ExchangeRate ReferenceRate = ExchangeRate.Create(3675.25m).Value;

    private static CaisseComponents Mro(decimal cash = 0m, decimal banks = 0m, decimal debtors = 0m, decimal creditors = 0m,
        decimal undelivered = 0m, decimal owner = 0m) =>
        new(Currency.MRO, cash, banks, debtors, creditors, undelivered, owner);

    private static CaisseComponents Xof(decimal cash = 0m, decimal banks = 0m, decimal debtors = 0m, decimal creditors = 0m,
        decimal undelivered = 0m, decimal owner = 0m) =>
        new(Currency.XOF, cash, banks, debtors, creditors, undelivered, owner);

    [Fact]
    public void Reference_figures_of_the_current_app_are_found_again()
    {
        var figures = GeneralBalanceFigures.Compute(Mro(banks: 144_127_657m), Xof(banks: 32_302_475m), ReferenceRate, 165_500_000m);

        // 32 302 475 × 3 675,25 ÷ 5 000 = 23 743 934,24875 MRO, sans arrondi.
        Assert.Equal(23_743_934.24875m, figures.DakarInMro.Budget);
        Assert.Equal(167_871_591.24875m, figures.GeneralBalance);
        Assert.Equal(2_371_591.24875m, figures.NetResult);
        Assert.Equal(1.43m, Math.Round(figures.NetResultPercent!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Each_component_is_added_after_converting_dakar()
    {
        var figures = GeneralBalanceFigures.Compute(
            Mro(cash: 1_000_000m, banks: 2_000_000m, debtors: 500_000m, creditors: 300_000m, undelivered: 100_000m),
            Xof(cash: 5_000_000m, banks: 10_000m, debtors: 50_000m, creditors: 20_000m, undelivered: 5_000m),
            ExchangeRate.Create(3600m).Value,
            capital: null);

        // 1 FCFA = 0,72 MRO au taux de 3 600.
        Assert.Equal(1_000_000m + 3_600_000m, figures.Total.Cash);
        Assert.Equal(2_000_000m + 7_200m, figures.Total.Banks);
        Assert.Equal(500_000m + 36_000m, figures.Total.Debtors);
        Assert.Equal(300_000m + 14_400m, figures.Total.Creditors);
        Assert.Equal(100_000m + 3_600m, figures.Total.Undelivered);
        Assert.Equal(figures.Total.Budget, figures.GeneralBalance);
        Assert.Equal(3_100_000m + 3_625_200m, figures.GeneralBalance);
    }

    [Fact]
    public void Owner_contributions_of_both_caisses_count_in_the_capital_not_as_a_result()
    {
        // Le patron apporte 10 000 000 FCFA à Dakar (7 200 000 MRO à 3 600) : le bilan monte d'autant, le résultat ne bouge pas.
        var rate = ExchangeRate.Create(3600m).Value;
        var before = GeneralBalanceFigures.Compute(Mro(cash: 50_000_000m), Xof(cash: 0m), rate, 50_000_000m);
        var after = GeneralBalanceFigures.Compute(Mro(cash: 50_000_000m), Xof(cash: 10_000_000m, owner: 10_000_000m), rate, 50_000_000m);

        Assert.Equal(7_200_000m, after.OwnerContributionsMro);
        Assert.Equal(57_200_000m, after.CapitalUsed);
        Assert.Equal(before.NetResult, after.NetResult);
        Assert.Equal(0m, after.NetResult);
    }

    [Fact]
    public void Without_a_general_capital_there_is_no_net_result()
    {
        var figures = GeneralBalanceFigures.Compute(Mro(cash: 1_000m), Xof(cash: 1_000m), ReferenceRate, capital: null);

        Assert.Null(figures.Capital);
        Assert.Null(figures.CapitalUsed);
        Assert.Null(figures.NetResult);
        Assert.Null(figures.NetResultPercent);
    }

    [Fact]
    public void The_rate_only_moves_the_dakar_side()
    {
        var low = GeneralBalanceFigures.Compute(Mro(cash: 1_000_000m), Xof(cash: 5_000_000m), ExchangeRate.Create(3600m).Value, null);
        var high = GeneralBalanceFigures.Compute(Mro(cash: 1_000_000m), Xof(cash: 5_000_000m), ExchangeRate.Create(3700m).Value, null);

        Assert.Equal(1_000_000m, high.Nouakchott.Budget);
        Assert.Equal(100_000m, high.GeneralBalance - low.GeneralBalance);
    }

    [Fact]
    public void The_caisses_must_be_one_in_mro_and_one_in_fcfa()
    {
        Assert.Throws<ArgumentException>(() => GeneralBalanceFigures.Compute(Xof(), Mro(), ReferenceRate, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(1000.555)]
    public void General_capital_is_positive_in_mro_with_two_decimals_at_most(double? amount)
    {
        var change = GeneralCapitalChange.Create((decimal?)amount, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(BalanceErrors.GeneralCapital.AmountInvalid, change.Error);
    }

    [Fact]
    public void A_snapshot_keeps_the_rate_and_each_caisse_in_its_currency_and_is_replaced_the_same_day()
    {
        var day = new DateOnly(2026, 10, 31);
        var first = GeneralBalanceFigures.Compute(Mro(banks: 144_127_657m), Xof(banks: 32_302_475m), ReferenceRate, 165_500_000m);
        var snapshot = GeneralBalanceSnapshot.Create(day, first, 430_074m, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(3675.25m, snapshot.Rate);
        Assert.Equal(32_302_475m, snapshot.DakarBanks);
        Assert.Equal(167_871_591.24875m, snapshot.GeneralBalance);

        var second = GeneralBalanceFigures.Compute(Mro(banks: 1m), Xof(banks: 1m), ExchangeRate.Create(3600m).Value, null);
        snapshot.Replace(second, 0m, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(day, snapshot.Date);
        Assert.Equal(3600m, snapshot.Rate);
        Assert.Null(snapshot.NetResult);
    }
}
