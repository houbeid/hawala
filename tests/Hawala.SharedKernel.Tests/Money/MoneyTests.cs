using Hawala.SharedKernel.Money;
using MoneyAmount = Hawala.SharedKernel.Money.Money;

namespace Hawala.SharedKernel.Tests.Money;

public class MoneyTests
{
    // --- FCFA : toujours entier ----------------------------------------------

    [Fact]
    public void Whole_xof_amount_is_accepted()
    {
        var result = MoneyAmount.Create(5_000_000m, Currency.XOF);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000_000m, result.Value.Amount);
        Assert.Equal(Currency.XOF, result.Value.Currency);
    }

    [Theory]
    [InlineData("1000.5")]
    [InlineData("0.01")]
    public void Xof_amount_with_decimals_is_refused(string amount)
    {
        var result = MoneyAmount.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency.XOF);

        Assert.Equal(MoneyErrors.Amount.XofNotInteger, result.Error);
    }

    [Fact]
    public void Xof_amount_written_with_zero_decimals_is_still_whole()
    {
        Assert.True(MoneyAmount.Create(1000.00m, Currency.XOF).IsSuccess);
    }

    // --- MRO : au plus 2 décimales -------------------------------------------

    [Theory]
    [InlineData("3650000")]
    [InlineData("123.4")]
    [InlineData("123.45")]
    public void Mro_amount_with_up_to_two_decimals_is_accepted(string amount)
    {
        Assert.True(MoneyAmount.Create(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), Currency.MRO).IsSuccess);
    }

    [Fact]
    public void Mro_amount_with_three_decimals_is_refused_not_rounded()
    {
        var result = MoneyAmount.Create(123.456m, Currency.MRO);

        Assert.Equal(MoneyErrors.Amount.TooManyDecimals, result.Error);
    }

    // --- Saisie en MRU : × 10 exact, au plus 3 décimales ---------------------

    [Theory]
    [InlineData("12.5", "125")]
    [InlineData("12.345", "123.45")]
    [InlineData("1000", "10000")]
    public void Mru_amount_is_converted_to_mro_exactly(string mru, string expectedMro)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var result = MoneyAmount.FromMru(decimal.Parse(mru, culture));

        Assert.True(result.IsSuccess);
        Assert.Equal(Currency.MRO, result.Value.Currency);
        Assert.Equal(decimal.Parse(expectedMro, culture), result.Value.Amount);
    }

    [Fact]
    public void Mru_amount_with_four_decimals_is_refused()
    {
        var result = MoneyAmount.FromMru(12.3456m);

        Assert.Equal(MoneyErrors.Mru.TooManyDecimals, result.Error);
    }

    // --- Calculs -------------------------------------------------------------

    [Fact]
    public void Amounts_of_the_same_currency_add_and_subtract()
    {
        var a = Mro(1000.25m);
        var b = Mro(250.50m);

        Assert.Equal(Mro(1250.75m), a + b);
        Assert.Equal(Mro(749.75m), a - b);
        Assert.Equal(Mro(-1000.25m), -a);
    }

    [Fact]
    public void Adding_mro_and_xof_is_a_programming_error()
    {
        var mro = Mro(1000m);
        var xof = MoneyAmount.Create(1000m, Currency.XOF).Value;

        Assert.Throws<InvalidOperationException>(() => mro + xof);
        Assert.Throws<InvalidOperationException>(() => mro - xof);
    }

    [Fact]
    public void Negative_amount_is_allowed_for_a_balance()
    {
        var balance = Mro(100m) - Mro(250m);

        Assert.True(balance.IsNegative);
        Assert.Equal(-150m, balance.Amount);
        Assert.True(MoneyAmount.Create(-150m, Currency.MRO).IsSuccess);
    }

    [Fact]
    public void Zero_is_zero_in_its_currency()
    {
        var zero = MoneyAmount.Zero(Currency.XOF);

        Assert.True(zero.IsZero);
        Assert.Equal(Currency.XOF, zero.Currency);
    }

    [Fact]
    public void Equality_is_by_value_whatever_the_number_of_written_decimals()
    {
        Assert.Equal(Mro(10.5m), Mro(10.50m));
        Assert.NotEqual(Mro(10m), MoneyAmount.Create(10m, Currency.XOF).Value);
    }

    private static MoneyAmount Mro(decimal amount) => MoneyAmount.Create(amount, Currency.MRO).Value;
}
