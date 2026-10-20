using Rates.Domain;
using Rates.Domain.DailyRates;

namespace Rates.Domain.Tests.DailyRates;

public class DailyRateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 20);
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    [Fact]
    public void Daily_rate_keeps_both_rates_and_who_set_them()
    {
        var result = DailyRate.Create(Today, 3600m, 3650.50m, Supervisor, Now);

        Assert.True(result.IsSuccess);
        var rate = result.Value;
        Assert.Equal(Today, rate.Date);
        Assert.Equal(3600m, rate.PurchaseRate.MroPer5000Xof);
        Assert.Equal(3650.50m, rate.SaleRate.MroPer5000Xof);
        Assert.Equal(Supervisor, rate.UpdatedBy);
        Assert.Equal(Now, rate.UpdatedAt);
    }

    [Fact]
    public void Rates_can_be_changed_during_the_day()
    {
        var rate = DailyRate.Create(Today, 3600m, 3650m, Supervisor, Now).Value;
        var later = Now.AddHours(3);

        var changed = rate.Change(3610m, 3660m, Supervisor, later);

        Assert.True(changed.IsSuccess);
        Assert.Equal(3610m, rate.PurchaseRate.MroPer5000Xof);
        Assert.Equal(3660m, rate.SaleRate.MroPer5000Xof);
        Assert.Equal(later, rate.UpdatedAt);
    }

    [Theory]
    [InlineData("0", "3650", "Rates.Rate.PurchaseInvalid")]
    [InlineData("3600.123", "3650", "Rates.Rate.PurchaseInvalid")]
    [InlineData("3600", "-1", "Rates.Rate.SaleInvalid")]
    [InlineData("3600", "3650.555", "Rates.Rate.SaleInvalid")]
    public void Invalid_rate_is_refused_not_rounded(string purchase, string sale, string expectedCode)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var result = DailyRate.Create(Today, decimal.Parse(purchase, culture), decimal.Parse(sale, culture), Supervisor, Now);

        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Invalid_change_keeps_the_previous_rates()
    {
        var rate = DailyRate.Create(Today, 3600m, 3650m, Supervisor, Now).Value;

        var changed = rate.Change(3600m, 0m, Supervisor, Now);

        Assert.Equal(RatesErrors.Rate.SaleInvalid, changed.Error);
        Assert.Equal(3650m, rate.SaleRate.MroPer5000Xof);
    }

    [Fact]
    public void Author_is_required()
    {
        Assert.Equal(RatesErrors.Rate.AuthorRequired, DailyRate.Create(Today, 3600m, 3650m, Guid.Empty, Now).Error);
    }
}
