using Identity.Domain;
using Identity.Domain.Accounts;

namespace Identity.Domain.Tests.Accounts;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+22222123456", "+22222123456")]
    [InlineData("+222 22 12 34 56", "+22222123456")]
    [InlineData("+222-22.12(34)56", "+22222123456")]
    [InlineData("00221 77 123 45 67", "+221771234567")]
    [InlineData("  +221771234567  ", "+221771234567")]
    public void Valid_number_is_normalized_to_e164(string input, string expected)
    {
        var result = PhoneNumber.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_number_is_refused(string? input)
    {
        var result = PhoneNumber.Create(input);

        Assert.Equal(IdentityErrors.Phone.Empty, result.Error);
    }

    [Theory]
    [InlineData("22 12 34 56")]          // sans indicatif pays
    [InlineData("+0222 22 12 34 56")]    // indicatif commençant par 0
    [InlineData("+222 22 AB 34 56")]     // lettres
    [InlineData("+1234567")]             // trop court (7 chiffres)
    [InlineData("+1234567890123456")]    // trop long (16 chiffres)
    [InlineData("++22222123456")]        // double +
    [InlineData("+222 2212/3456")]       // caractère non autorisé
    public void Invalid_number_is_refused(string input)
    {
        var result = PhoneNumber.Create(input);

        Assert.Equal(IdentityErrors.Phone.Invalid, result.Error);
    }

    [Fact]
    public void Same_number_written_differently_is_the_same_phone()
    {
        var a = PhoneNumber.Create("+222 22 12 34 56").Value;
        var b = PhoneNumber.Create("0022222123456").Value;

        Assert.Equal(a, b);
    }
}