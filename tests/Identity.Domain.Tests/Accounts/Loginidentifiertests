using Identity.Domain;
using Identity.Domain.Accounts;

namespace Identity.Domain.Tests.Accounts;

public class LoginIdentifierTests
{
    [Fact]
    public void Input_with_at_sign_is_an_email()
    {
        var result = LoginIdentifier.Parse(" Agent@Example.com ");

        Assert.True(result.IsSuccess);
        Assert.Equal(LoginIdentifierKind.Email, result.Value.Kind);
        Assert.Equal("agent@example.com", result.Value.Value);
    }

    [Fact]
    public void Input_without_at_sign_is_a_phone()
    {
        var result = LoginIdentifier.Parse("+222 22 12 34 56");

        Assert.True(result.IsSuccess);
        Assert.Equal(LoginIdentifierKind.Phone, result.Value.Kind);
        Assert.Equal("+22222123456", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_input_is_refused(string? input)
    {
        var result = LoginIdentifier.Parse(input);

        Assert.Equal(IdentityErrors.Login.Empty, result.Error);
    }

    [Fact]
    public void Malformed_email_returns_email_error()
    {
        var result = LoginIdentifier.Parse("agent@");

        Assert.Equal(IdentityErrors.Email.Invalid, result.Error);
    }

    [Fact]
    public void Malformed_phone_returns_phone_error()
    {
        var result = LoginIdentifier.Parse("12345");

        Assert.Equal(IdentityErrors.Phone.Invalid, result.Error);
    }
}