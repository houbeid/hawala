using Cash.Domain;
using Cash.Domain.Adjustments;
using Hawala.SharedKernel.Money;

namespace Cash.Domain.Tests.Adjustments;

public class BalanceAdjustmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");
    private static readonly Guid Account = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    [Theory]
    [InlineData(1_500.25)]
    [InlineData(-200)]
    public void A_signed_difference_with_a_reason_is_kept(decimal amount)
    {
        var adjustment = BalanceAdjustment.Create(
            Nkc, AdjustmentTarget.TreasuryAccount, Account, amount, Currency.MRO, "  Écart constaté  ", Now, Supervisor, Now).Value;

        Assert.Equal(amount, adjustment.Amount);
        Assert.Equal(Math.Abs(amount), adjustment.AbsoluteMoney.Amount);
        Assert.Equal("Écart constaté", adjustment.Reason);
    }

    [Fact]
    public void Zero_or_too_precise_amounts_are_refused()
    {
        Assert.Equal(CashErrors.Adjustment.AmountInvalid,
            BalanceAdjustment.Create(Nkc, AdjustmentTarget.Party, Account, 0m, Currency.MRO, "Motif", Now, Supervisor, Now).Error);
        Assert.Equal(CashErrors.Adjustment.AmountInvalid,
            BalanceAdjustment.Create(Nkc, AdjustmentTarget.Party, Account, 10.5m, Currency.XOF, "Motif", Now, Supervisor, Now).Error);
    }

    [Fact]
    public void Reason_is_required_and_the_date_is_not_in_the_future()
    {
        Assert.Equal(CashErrors.Adjustment.ReasonRequired,
            BalanceAdjustment.Create(Nkc, AdjustmentTarget.Party, Account, 10m, Currency.MRO, " ", Now, Supervisor, Now).Error);
        Assert.Equal(CashErrors.Movement.FutureDate,
            BalanceAdjustment.Create(Nkc, AdjustmentTarget.Party, Account, 10m, Currency.MRO, "Motif", Now.AddMinutes(1), Supervisor, Now).Error);
    }
}
