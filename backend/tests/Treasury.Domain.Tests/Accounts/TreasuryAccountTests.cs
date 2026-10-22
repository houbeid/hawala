using Hawala.SharedKernel.Money;
using Treasury.Domain;
using Treasury.Domain.Accounts;

namespace Treasury.Domain.Tests.Accounts;

public class TreasuryAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    // Identifiant fictif : la Trésorerie ne fait que porter l'agence.
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");

    // --- Création ----------------------------------------------------------

    [Theory]
    [InlineData(TreasuryAccountType.Cash)]
    [InlineData(TreasuryAccountType.Bank)]
    [InlineData(TreasuryAccountType.Wallet)]
    public void New_account_is_active_without_a_ledger_account_yet(TreasuryAccountType type)
    {
        var result = TreasuryAccount.Create("  Bankily principal  ", type, Nouakchott, Currency.MRO, Now);

        Assert.True(result.IsSuccess);
        var account = result.Value;
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Bankily principal", account.Name);
        Assert.Equal(type, account.Type);
        Assert.Equal(Nouakchott, account.AgencyId);
        Assert.Equal(Currency.MRO, account.Currency);
        Assert.Equal(Guid.Empty, account.LedgerAccountId);
        Assert.True(account.IsActive);
        Assert.Equal(Now, account.CreatedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        var result = TreasuryAccount.Create(name, TreasuryAccountType.Cash, Nouakchott, Currency.MRO, Now);

        Assert.Equal(TreasuryErrors.Account.NameEmpty, result.Error);
    }

    [Fact]
    public void Too_long_name_is_refused()
    {
        var result = TreasuryAccount.Create(
            new string('a', TreasuryAccount.NameMaxLength + 1), TreasuryAccountType.Cash, Nouakchott, Currency.MRO, Now);

        Assert.Equal(TreasuryErrors.Account.NameTooLong, result.Error);
    }

    [Fact]
    public void Agency_is_required()
    {
        var result = TreasuryAccount.Create("Caisse", TreasuryAccountType.Cash, Guid.Empty, Currency.MRO, Now);

        Assert.Equal(TreasuryErrors.Account.AgencyRequired, result.Error);
    }

    [Fact]
    public void Unknown_type_is_refused()
    {
        var result = TreasuryAccount.Create("Caisse", (TreasuryAccountType)42, Nouakchott, Currency.MRO, Now);

        Assert.Equal(TreasuryErrors.Account.TypeInvalid, result.Error);
    }

    // --- Compte au grand livre ---------------------------------------------

    [Fact]
    public void Ledger_account_is_attached_once()
    {
        var account = NewCash();
        var ledgerAccountId = Guid.NewGuid();

        account.AttachLedgerAccount(ledgerAccountId);

        Assert.Equal(ledgerAccountId, account.LedgerAccountId);
        Assert.Throws<InvalidOperationException>(() => account.AttachLedgerAccount(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => NewCash().AttachLedgerAccount(Guid.Empty));
    }

    // --- Renommer ----------------------------------------------------------

    [Fact]
    public void Account_can_be_renamed()
    {
        var account = NewCash();

        Assert.True(account.Rename("  Caisse guichet 2  ").IsSuccess);
        Assert.Equal("Caisse guichet 2", account.Name);
    }

    [Fact]
    public void Invalid_new_name_changes_nothing()
    {
        var account = NewCash();

        Assert.Equal(TreasuryErrors.Account.NameEmpty, account.Rename(" ").Error);
        Assert.Equal("Caisse principale", account.Name);
    }

    // --- Désactiver / réactiver --------------------------------------------

    [Fact]
    public void Account_with_zero_balance_can_be_deactivated_then_reactivated()
    {
        var account = NewCash();

        Assert.True(account.Deactivate(Money.Zero(Currency.MRO)).IsSuccess);
        Assert.False(account.IsActive);

        Assert.True(account.Reactivate().IsSuccess);
        Assert.True(account.IsActive);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("-500")]
    public void Account_with_money_left_cannot_be_deactivated(string balance)
    {
        var account = NewCash();
        var amount = Money.Create(decimal.Parse(balance, System.Globalization.CultureInfo.InvariantCulture), Currency.MRO).Value;

        Assert.Equal(TreasuryErrors.Account.BalanceNotZero, account.Deactivate(amount).Error);
        Assert.True(account.IsActive);
    }

    [Fact]
    public void Deactivating_twice_or_reactivating_an_active_account_is_refused()
    {
        var account = NewCash();

        Assert.Equal(TreasuryErrors.Account.AlreadyActive, account.Reactivate().Error);

        account.Deactivate(Money.Zero(Currency.MRO));
        Assert.Equal(TreasuryErrors.Account.AlreadyInactive, account.Deactivate(Money.Zero(Currency.MRO)).Error);
    }

    [Fact]
    public void Balance_in_another_currency_is_a_programming_error()
    {
        var account = NewCash();

        Assert.Throws<ArgumentException>(() => account.Deactivate(Money.Zero(Currency.XOF)));
    }

    private static TreasuryAccount NewCash() =>
        TreasuryAccount.Create("Caisse principale", TreasuryAccountType.Cash, Nouakchott, Currency.MRO, Now).Value;
}
