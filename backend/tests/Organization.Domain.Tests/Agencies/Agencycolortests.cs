using Organization.Domain;
using Organization.Domain.Agencies;

namespace Organization.Domain.Tests.Agencies;

public class AgencyColorTests
{
    [Theory]
    [InlineData("#1e8e3e", "#1E8E3E")]
    [InlineData("  #1565C0  ", "#1565C0")]
    [InlineData("#000000", "#000000")]
    public void Valid_color_is_normalized_to_uppercase(string input, string expected)
    {
        var result = AgencyColor.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1E8E3E")]      // sans #
    [InlineData("#1E8E3")]      // 5 chiffres
    [InlineData("#1E8E3E0")]    // 7 chiffres
    [InlineData("#1E8G3E")]     // G n'est pas hexadécimal
    [InlineData("# E8E3E")]     // espace à l'intérieur
    [InlineData("vert")]
    public void Invalid_color_is_refused(string? input)
    {
        var result = AgencyColor.Create(input);

        Assert.Equal(OrganizationErrors.Color.Invalid, result.Error);
    }
}