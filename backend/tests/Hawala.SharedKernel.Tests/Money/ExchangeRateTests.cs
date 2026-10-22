using Hawala.SharedKernel.Money;
using MoneyAmount = Hawala.SharedKernel.Money.Money;

namespace Hawala.SharedKernel.Tests.Money;

public class ExchangeRateTests
{
    // --- Création du taux ----------------------------------------------------

    [Theory]
    [InlineData("3604.77")]
    [InlineData("3650")]
    [InlineData("3629.8")]
    public void Positive_rate_with_up_to_two_decimals_is_accepted(string rate)
    {
        var value = decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture);

        var result = ExchangeRate.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value.MroPer5000Xof);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3600")]
    [InlineData("3604.775")]
    public void Zero_negative_or_too_precise_rate_is_refused_not_rounded(string rate)
    {
        var result = ExchangeRate.Create(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(MoneyErrors.Rate.Invalid, result.Error);
    }

    // --- Montant MRO d'un transfert ------------------------------------------

    [Fact]
    public void Example_of_the_business_rules_gives_the_expected_amount()
    {
        // 5 000 000 FCFA vendus à 3 650 : le client paie 3 650 000 MRO.
        var mro = Rate(3650m).ToTransferMro(Xof(5_000_000m));

        Assert.Equal(Currency.MRO, mro.Currency);
        Assert.Equal(3_650_000m, mro.Amount);
    }

    [Fact]
    public void Exact_half_is_rounded_up_not_to_even()
    {
        // 1 000 020 × 3625 / 5000 = 725 014,5 exactement.
        // L'arrondi par défaut de .NET (vers le pair) donnerait 725 014 : la règle validée exige 725 015.
        Assert.Equal(725_014m, decimal.Round(725_014.5m));

        var mro = Rate(3625m).ToTransferMro(Xof(1_000_020m));

        Assert.Equal(725_015m, mro.Amount);
    }

    [Theory]
    [InlineData("1000", "3604.77", "720.954", "721")]     // 720,954 → 721
    [InlineData("1000", "3604.73", "720.946", "721")]     // 720,946 → 721
    [InlineData("1000", "3604.71", "720.942", "721")]     // 720,942 → 721
    [InlineData("1000", "3602.40", "720.48", "720")]      // 720,48  → 720
    public void Amount_is_rounded_once_to_the_nearest_unit(string xof, string rate, string exact, string expected)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var amount = decimal.Parse(xof, culture);
        var value = decimal.Parse(rate, culture);

        // Vérifie d'abord la valeur exacte annoncée, pour que le cas teste bien ce qu'il dit.
        Assert.Equal(decimal.Parse(exact, culture), amount * value / 5000m);

        var mro = Rate(value).ToTransferMro(Xof(amount));

        Assert.Equal(decimal.Parse(expected, culture), mro.Amount);
    }

    [Fact]
    public void Converting_an_mro_amount_is_a_programming_error()
    {
        var mro = MoneyAmount.Create(1000m, Currency.MRO).Value;

        Assert.Throws<ArgumentException>(() => Rate(3600m).ToTransferMro(mro));
    }

    private static ExchangeRate Rate(decimal value) => ExchangeRate.Create(value).Value;

    private static MoneyAmount Xof(decimal amount) => MoneyAmount.Create(amount, Currency.XOF).Value;
}
