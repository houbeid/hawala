using Balance.Domain;
using Hawala.SharedKernel.Money;

namespace Balance.Domain.Tests;

/// <summary>
/// Bilan par caisse (décision du 21 octobre 2026 : deux caisses, deux capitaux, aucune conversion).
/// Exemple Dakar : budget 95 588 760 FCFA (capture « Bilan par caisse »), capital fictif 100 000 000 FCFA.
/// </summary>
public class BalanceFiguresTests
{
    [Fact]
    public void Net_result_of_a_caisse_is_its_budget_minus_its_capital_in_its_own_currency()
    {
        var figures = BalanceFigures.Compute(new BalanceInputs(Currency.XOF, 95_588_760m, 100_000_000m, 0m));

        Assert.Equal(Currency.XOF, figures.Currency);
        Assert.Equal(100_000_000m, figures.CapitalUsed);
        Assert.Equal(-4_411_240m, figures.NetResult);
        Assert.Equal(-4.41m, Math.Round(figures.NetResultPercent!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Reference_net_result_is_minus_0_16_percent_of_the_capital()
    {
        // −261 005 sur un capital de 165 500 000 (valeur de référence de l'app actuelle).
        var figures = BalanceFigures.Compute(new BalanceInputs(Currency.MRO, 165_238_995m, 165_500_000m, 0m));

        Assert.Equal(-261_005m, figures.NetResult);
        Assert.Equal(-0.16m, Math.Round(figures.NetResultPercent!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Owner_contributions_and_withdrawals_count_in_the_capital_not_as_a_result()
    {
        // Le patron a retiré 1 000 000 MRO de Nouakchott : la caisse a 1 000 000 de moins, le capital aussi.
        var figures = BalanceFigures.Compute(new BalanceInputs(Currency.MRO, 99_000_000m, 100_000_000m, -1_000_000m));

        Assert.Equal(99_000_000m, figures.CapitalUsed);
        Assert.Equal(0m, figures.NetResult);
    }

    [Fact]
    public void Without_initial_capital_there_is_no_net_result()
    {
        var figures = BalanceFigures.Compute(new BalanceInputs(Currency.MRO, 10m, null, 0m));

        Assert.Equal(10m, figures.Budget);
        Assert.Null(figures.CapitalUsed);
        Assert.Null(figures.NetResult);
        Assert.Null(figures.NetResultPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(10.123)]
    public void Initial_capital_must_be_a_positive_mro_amount_for_nouakchott(double? amount)
    {
        var result = CapitalChange.Create(Guid.NewGuid(), Currency.MRO, (decimal?)amount, null, Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(BalanceErrors.Capital.AmountInvalid, result.Error);
    }

    [Fact]
    public void Initial_capital_of_dakar_is_a_whole_fcfa_amount()
    {
        var refused = CapitalChange.Create(Guid.NewGuid(), Currency.XOF, 100.5m, null, Guid.NewGuid(), DateTimeOffset.UtcNow);
        var accepted = CapitalChange.Create(Guid.NewGuid(), Currency.XOF, 100_000_000m, " Mise en service ", Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(BalanceErrors.Capital.AmountInvalid, refused.Error);
        Assert.Equal(Currency.XOF, accepted.Value.Currency);
        Assert.Equal("Mise en service", accepted.Value.Reason);
    }
}
