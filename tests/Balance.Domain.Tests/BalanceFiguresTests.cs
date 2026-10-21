using Balance.Domain;
using Hawala.SharedKernel.Money;

namespace Balance.Domain.Tests;

/// <summary>
/// Bilan général. Chiffres de référence relevés sur l'écran « الميزانية العامة » de l'app actuelle (totaux seulement) :
/// budget Nouakchott 48 497 821 MRO, budget Dakar 160 604 259 FCFA, taux 3 600, capital 165 500 000
/// → bilan général 164 132 887 (affiché), résultat net −1 367 113 (−0,83 %).
/// </summary>
public class BalanceFiguresTests
{
    private static ExchangeRate Rate(decimal value) => ExchangeRate.Create(value).Value;

    [Fact]
    public void General_balance_is_the_sum_of_both_budgets_converted_at_the_balance_rate()
    {
        var figures = BalanceFigures.Compute(new BalanceInputs(48_497_821m, 160_604_259m, Rate(3600m), 165_500_000m, 0m, 0m));

        Assert.Equal(115_635_066.48m, figures.XofBudgetInMro);
        Assert.Equal(164_132_887.48m, figures.GeneralBalance);
        Assert.Equal(164_132_887m, Math.Round(figures.GeneralBalance, 0, MidpointRounding.AwayFromZero));
        Assert.Equal(-1_367_112.52m, figures.NetResult);
        Assert.Equal(-1_367_113m, Math.Round(figures.NetResult!.Value, 0, MidpointRounding.AwayFromZero));
        Assert.Equal(-0.83m, Math.Round(figures.NetResultPercent!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Second_reference_net_result_is_minus_0_16_percent_of_the_capital()
    {
        // −261 005 sur un capital de 165 500 000 (deuxième valeur de référence).
        var figures = BalanceFigures.Compute(new BalanceInputs(165_238_995m, 0m, Rate(3600m), 165_500_000m, 0m, 0m));

        Assert.Equal(-261_005m, figures.NetResult);
        Assert.Equal(-0.16m, Math.Round(figures.NetResultPercent!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Owner_contributions_and_withdrawals_count_in_the_capital_not_as_a_result()
    {
        // Le patron a retiré 1 000 000 MRO à Nouakchott et apporté 5 000 000 FCFA à Dakar (3 600 000 MRO au taux 3 600).
        var figures = BalanceFigures.Compute(new BalanceInputs(99_000_000m, 5_000_000m, Rate(3600m), 100_000_000m, -1_000_000m, 5_000_000m));

        Assert.Equal(2_600_000m, figures.OwnerContributionsMro);
        Assert.Equal(102_600_000m, figures.CapitalUsed);
        Assert.Equal(0m, figures.NetResult);
    }

    [Fact]
    public void Without_initial_capital_there_is_no_net_result()
    {
        var figures = BalanceFigures.Compute(new BalanceInputs(10m, 0m, Rate(3600m), null, 0m, 0m));

        Assert.Equal(10m, figures.GeneralBalance);
        Assert.Null(figures.CapitalUsed);
        Assert.Null(figures.NetResult);
        Assert.Null(figures.NetResultPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(10.123)]
    public void Initial_capital_must_be_a_positive_mro_amount(double? amount)
    {
        var result = CapitalChange.Create((decimal?)amount, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(BalanceErrors.Capital.AmountInvalid, result.Error);
    }

    [Fact]
    public void A_capital_change_keeps_its_reason()
    {
        var change = CapitalChange.Create(165_500_000m, "  Mise en service  ", Guid.NewGuid(), DateTimeOffset.UtcNow).Value;

        Assert.Equal(165_500_000m, change.Amount);
        Assert.Equal("Mise en service", change.Reason);
    }
}
