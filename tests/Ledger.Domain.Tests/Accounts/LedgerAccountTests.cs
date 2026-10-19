using Hawala.SharedKernel.Money;
using Ledger.Contracts;
using Ledger.Domain;
using Ledger.Domain.Accounts;

namespace Ledger.Domain.Tests.Accounts;

public class LedgerAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);

    // Identifiants fictifs : le grand livre ne fait que les porter.
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");
    private static readonly Guid Owner = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");

    // --- Ouverture d'un compte de caisse ou de tiers -----------------------

    [Theory]
    [InlineData(LedgerAccountKind.Treasury)]
    [InlineData(LedgerAccountKind.Party)]
    public void Treasury_or_party_account_is_opened_active(LedgerAccountKind kind)
    {
        var result = LedgerAccount.Open(kind, Owner, "  Caisse principale  ", Currency.MRO, Nouakchott, Now);

        Assert.True(result.IsSuccess);
        var account = result.Value;
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal(kind, account.Kind);
        Assert.Null(account.SystemRole);
        Assert.Equal(Owner, account.OwnerId);
        Assert.Equal("Caisse principale", account.Name);
        Assert.Equal(Currency.MRO, account.Currency);
        Assert.Equal(Nouakchott, account.AgencyId);
        Assert.True(account.IsActive);
        Assert.Equal(Now, account.CreatedAt);
    }

    [Fact]
    public void System_account_cannot_be_opened_like_a_treasury_account()
    {
        var result = LedgerAccount.Open(LedgerAccountKind.System, Owner, "Position", Currency.MRO, Nouakchott, Now);

        Assert.Equal(LedgerErrors.Account.KindInvalid, result.Error);
    }

    [Fact]
    public void Owner_agency_and_name_are_required()
    {
        Assert.Equal(
            LedgerErrors.Account.OwnerRequired,
            LedgerAccount.Open(LedgerAccountKind.Party, Guid.Empty, "Client", Currency.MRO, Nouakchott, Now).Error);
        Assert.Equal(
            LedgerErrors.Account.AgencyRequired,
            LedgerAccount.Open(LedgerAccountKind.Party, Owner, "Client", Currency.MRO, Guid.Empty, Now).Error);
        Assert.Equal(
            LedgerErrors.Account.NameEmpty,
            LedgerAccount.Open(LedgerAccountKind.Party, Owner, "   ", Currency.MRO, Nouakchott, Now).Error);
        Assert.Equal(
            LedgerErrors.Account.NameTooLong,
            LedgerAccount.Open(
                LedgerAccountKind.Party, Owner, new string('a', LedgerAccount.NameMaxLength + 1), Currency.MRO, Nouakchott, Now).Error);
    }

    // --- Comptes système ---------------------------------------------------

    [Theory]
    [InlineData(SystemAccountRole.ExchangePosition, "Position de change")]
    [InlineData(SystemAccountRole.TransfersToDeliver, "Transferts à livrer")]
    [InlineData(SystemAccountRole.OwnerContributions, "Apports du patron")]
    [InlineData(SystemAccountRole.Adjustments, "Écarts et ajustements")]
    [InlineData(SystemAccountRole.OpeningBalances, "Soldes d'ouverture")]
    public void System_account_is_named_after_its_role(SystemAccountRole role, string expectedName)
    {
        var account = LedgerAccount.CreateSystem(role, Nouakchott, Currency.MRO, Now).Value;

        Assert.Equal(LedgerAccountKind.System, account.Kind);
        Assert.Equal(role, account.SystemRole);
        Assert.Null(account.OwnerId);
        Assert.Equal(expectedName, account.Name);
    }

    [Fact]
    public void System_account_requires_an_agency()
    {
        var result = LedgerAccount.CreateSystem(SystemAccountRole.ExchangePosition, Guid.Empty, Currency.XOF, Now);

        Assert.Equal(LedgerErrors.Account.AgencyRequired, result.Error);
    }

    // --- Recevoir une écriture ---------------------------------------------

    [Fact]
    public void Active_account_receives_an_amount_in_its_currency()
    {
        var account = MroAccount();

        Assert.True(account.EnsureCanReceive(Money.Create(1000m, Currency.MRO).Value).IsSuccess);
    }

    [Fact]
    public void Account_refuses_another_currency()
    {
        var account = MroAccount();

        var result = account.EnsureCanReceive(Money.Create(1000m, Currency.XOF).Value);

        Assert.Equal(LedgerErrors.Line.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void Closed_account_receives_nothing_more()
    {
        var account = MroAccount();

        account.Close();
        account.Close();

        Assert.False(account.IsActive);
        Assert.Equal(
            LedgerErrors.Account.Inactive,
            account.EnsureCanReceive(Money.Create(1000m, Currency.MRO).Value).Error);
    }

    [Fact]
    public void Reopened_account_receives_again()
    {
        var account = MroAccount();
        account.Close();

        account.Reopen();

        Assert.True(account.IsActive);
        Assert.True(account.EnsureCanReceive(Money.Create(1000m, Currency.MRO).Value).IsSuccess);
    }

    private static LedgerAccount MroAccount() =>
        LedgerAccount.Open(LedgerAccountKind.Treasury, Owner, "Caisse", Currency.MRO, Nouakchott, Now).Value;
}
