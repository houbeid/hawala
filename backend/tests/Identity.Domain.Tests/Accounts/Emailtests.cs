using Identity.Domain;
using Identity.Domain.Accounts;

namespace Identity.Domain.Tests.Accounts;

public class EmailTests
{
    [Theory]
    [InlineData("agent@example.com", "agent@example.com")]
    [InlineData("  Agent@Example.COM  ", "agent@example.com")]
    [InlineData("caisse.nkc+test@agence.example.mr", "caisse.nkc+test@agence.example.mr")]
    public void Valid_email_is_normalized(string input, string expected)
    {
        var result = Email.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_email_is_refused(string? input)
    {
        var result = Email.Create(input);

        Assert.Equal(IdentityErrors.Email.Empty, result.Error);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("@example.com")]
    [InlineData("agent@")]
    [InlineData("agent@example")]
    [InlineData("agent@@example.com")]
    [InlineData("agent@ex@ample.com")]
    [InlineData("agent name@example.com")]
    [InlineData("agent@example..com")]
    [InlineData("agent@-example.com")]
    [InlineData("agent@example.c")]
    public void Malformed_email_is_refused(string input)
    {
        var result = Email.Create(input);

        Assert.Equal(IdentityErrors.Email.Invalid, result.Error);
    }

    [Fact]
    public void Too_long_email_is_refused()
    {
        var input = new string('a', 64) + "@" + new string('b', 190) + ".com";

        var result = Email.Create(input);

        Assert.Equal(IdentityErrors.Email.TooLong, result.Error);
    }

    [Fact]
    public void Same_address_with_different_case_is_the_same_email()
    {
        var a = Email.Create("Agent@Example.com").Value;
        var b = Email.Create("agent@example.COM").Value;

        Assert.Equal(a, b);
    }
}