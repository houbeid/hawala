using Hawala.SharedKernel.Money;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Domain.Tests.Accounts;

/// <summary>Plafonds par compte (phase 6, règles validées le 22 octobre 2026).</summary>
public class AccountLimitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000e001");
    private static readonly Guid Dakar = Guid.Parse("0199f0a0-0000-7000-8000-00000000e002");

    private static UserAccount Account(RoleTemplate role = RoleTemplate.Agent) =>
        UserAccount.CreateWithTemporaryPassword(
            Email.Create($"plafond-{Guid.NewGuid():N}@example.com").Value,
            phone: null,
            "Compte Test",
            role,
            role == RoleTemplate.Supervisor ? null : Nouakchott,
            Now).Value;

    [Fact]
    public void A_new_account_has_no_limit()
    {
        Assert.Equal(AccountLimits.None, Account().Limits);
    }

    [Fact]
    public void Limits_are_kept_without_touching_the_sessions()
    {
        var account = Account();
        var version = account.SecurityVersion;

        var changed = account.SetLimits(new AccountLimits(2_000_000m, 10_000_000m, 1_000_000.50m), Currency.MRO);

        Assert.True(changed.Value);
        Assert.Equal(new AccountLimits(2_000_000m, 10_000_000m, 1_000_000.50m), account.Limits);
        Assert.Equal(version, account.SecurityVersion);
    }

    [Fact]
    public void Same_limits_change_nothing()
    {
        var account = Account();
        account.SetLimits(new AccountLimits(1_000m, null, null), Currency.MRO);

        Assert.False(account.SetLimits(new AccountLimits(1_000m, null, null), Currency.MRO).Value);
    }

    [Fact]
    public void Zero_is_allowed_and_null_removes_a_limit()
    {
        var account = Account();
        account.SetLimits(new AccountLimits(1_000m, 2_000m, 0m), Currency.MRO);

        account.SetLimits(new AccountLimits(null, 2_000m, 0m), Currency.MRO);

        Assert.Equal(new AccountLimits(null, 2_000m, 0m), account.Limits);
    }

    [Fact]
    public void A_negative_limit_is_refused()
    {
        var result = Account().SetLimits(new AccountLimits(null, -1m, null), Currency.MRO);

        Assert.Equal("Identity.Limits.Negative", result.Error.Code);
    }

    [Theory]
    [InlineData(100.5, "XOF")]
    [InlineData(100.125, "MRO")]
    public void A_limit_follows_the_precision_of_the_currency(double amount, string currency)
    {
        var result = Account().SetLimits(new AccountLimits((decimal)amount, null, null), Enum.Parse<Currency>(currency));

        Assert.Equal("Identity.Limits.TooPrecise", result.Error.Code);
    }

    [Fact]
    public void A_supervisor_never_has_a_limit()
    {
        var result = Account(RoleTemplate.Supervisor).SetLimits(new AccountLimits(1_000m, null, null), Currency.MRO);

        Assert.Equal("Identity.Limits.SupervisorHasNone", result.Error.Code);
    }

    [Fact]
    public void Moving_to_another_agency_clears_the_limits()
    {
        var account = Account();
        account.SetLimits(new AccountLimits(1_000m, 2_000m, 3_000m), Currency.MRO);

        account.ChangeAgency(Dakar);

        Assert.Equal(AccountLimits.None, account.Limits);
    }

    [Fact]
    public void Changing_the_role_clears_the_limits()
    {
        var account = Account();
        account.SetLimits(new AccountLimits(1_000m, 2_000m, 3_000m), Currency.MRO);

        account.ChangeRole(RoleTemplate.Cashier, Nouakchott, activeSupervisorCount: 2);

        Assert.Equal(AccountLimits.None, account.Limits);
    }

    [Fact]
    public void Same_agency_keeps_the_limits()
    {
        var account = Account();
        account.SetLimits(new AccountLimits(1_000m, null, null), Currency.MRO);

        account.ChangeAgency(Nouakchott);

        Assert.Equal(1_000m, account.Limits.PerTransfer);
    }
}
