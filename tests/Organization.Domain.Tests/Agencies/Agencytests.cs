using Hawala.SharedKernel.Money;
using Organization.Domain;
using Organization.Domain.Agencies;

namespace Organization.Domain.Tests.Agencies;

public class AgencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 8, 0, 0, TimeSpan.Zero);
    private static readonly AgencyColor Green = AgencyColor.Create("#1E8E3E").Value;
    private static readonly AgencyColor Blue = AgencyColor.Create("#1565C0").Value;

    private static Agency Nouakchott() =>
        Agency.Create("NKC", "Nouakchott", Country.Mauritania, Green, "Africa/Nouakchott", Now).Value;

    // --- Création ----------------------------------------------------------

    [Fact]
    public void Valid_agency_is_created_with_trimmed_values()
    {
        var result = Agency.Create(" NKC ", "  Nouakchott  ", Country.Mauritania, Green, " Africa/Nouakchott ", Now);

        Assert.True(result.IsSuccess);
        var agency = result.Value;
        Assert.NotEqual(Guid.Empty, agency.Id);
        Assert.Equal("NKC", agency.Code);
        Assert.Equal("Nouakchott", agency.Name);
        Assert.Equal(Country.Mauritania, agency.Country);
        Assert.Equal(Green, agency.Color);
        Assert.Equal("Africa/Nouakchott", agency.TimeZoneId);
    }

    [Theory]
    [InlineData(Country.Mauritania, Currency.MRO)]
    [InlineData(Country.Senegal, Currency.XOF)]
    public void Currency_follows_the_country(Country country, Currency expected)
    {
        var agency = Agency.Create("AAA", "Agence", country, Green, "Africa/Dakar", Now).Value;

        Assert.Equal(expected, agency.Currency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NK")]      // trop court
    [InlineData("NKCX")]    // trop long
    [InlineData("nkc")]     // minuscules
    [InlineData("NK1")]     // chiffre
    [InlineData("NÉC")]     // lettre accentuée
    public void Invalid_code_is_refused(string? code)
    {
        var result = Agency.Create(code, "Nouakchott", Country.Mauritania, Green, "Africa/Nouakchott", Now);

        Assert.Equal(OrganizationErrors.Agency.CodeInvalid, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_name_is_refused(string? name)
    {
        var result = Agency.Create("NKC", name, Country.Mauritania, Green, "Africa/Nouakchott", Now);

        Assert.Equal(OrganizationErrors.Agency.NameEmpty, result.Error);
    }

    [Fact]
    public void Name_is_limited_to_50_characters()
    {
        var ok = Agency.Create("NKC", new string('a', 50), Country.Mauritania, Green, "Africa/Nouakchott", Now);
        var tooLong = Agency.Create("NKC", new string('a', 51), Country.Mauritania, Green, "Africa/Nouakchott", Now);

        Assert.True(ok.IsSuccess);
        Assert.Equal(OrganizationErrors.Agency.NameTooLong, tooLong.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Missing_time_zone_is_refused(string? timeZoneId)
    {
        var result = Agency.Create("NKC", "Nouakchott", Country.Mauritania, Green, timeZoneId, Now);

        Assert.Equal(OrganizationErrors.Agency.TimeZoneEmpty, result.Error);
    }

    // --- Couleur -----------------------------------------------------------

    [Fact]
    public void Supervisor_can_change_the_color()
    {
        var agency = Nouakchott();

        agency.ChangeColor(Blue);

        Assert.Equal(Blue, agency.Color);
    }

    [Fact]
    public void Changing_the_color_keeps_the_code()
    {
        var agency = Nouakchott();

        agency.ChangeColor(Blue);

        Assert.Equal("NKC", agency.Code);
    }
}