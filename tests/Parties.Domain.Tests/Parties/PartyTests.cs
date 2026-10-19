using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Domain.Tests.Parties;

public class PartyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    // Identifiants fictifs : les Tiers ne font que les porter.
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");
    private static readonly Guid Dakar = Guid.Parse("0199f0a0-0000-7000-8000-00000000a002");

    // --- Création ----------------------------------------------------------

    [Fact]
    public void Party_is_created_in_the_balance_sheet_not_archived_without_agency_accounts_yet()
    {
        var phone = PartyPhone.Create("+222 22 12 34 56").Value;

        var result = Party.Create(
            "  Client Fictif  ", PartyType.Client, phone, "  1234567890  ", " Tevragh Zeina ", " Bon client ", Now);

        Assert.True(result.IsSuccess);
        var party = result.Value;
        Assert.NotEqual(Guid.Empty, party.Id);
        Assert.Equal("Client Fictif", party.Name);
        Assert.Equal(PartyType.Client, party.Type);
        Assert.Equal("41", party.AccountCode);
        Assert.Equal("+22222123456", party.Phone!.Value);
        Assert.Equal("1234567890", party.Nni);
        Assert.Equal("Tevragh Zeina", party.Address);
        Assert.Equal("Bon client", party.Note);
        Assert.True(party.InBalanceSheet);
        Assert.False(party.IsArchived);
        Assert.Empty(party.AgencyAccounts);
        Assert.Equal(Now, party.CreatedAt);
    }

    [Fact]
    public void Only_name_and_type_are_required()
    {
        var party = Party.Create("Société Fictive", PartyType.Supplier, null, "  ", "", null, Now).Value;

        Assert.Null(party.Phone);
        Assert.Null(party.Nni);
        Assert.Null(party.Address);
        Assert.Null(party.Note);
    }

    [Theory]
    [InlineData(PartyType.Client, "41")]
    [InlineData(PartyType.Supplier, "40")]
    [InlineData(PartyType.Staff, "42")]
    [InlineData(PartyType.Other, null)]
    public void Account_code_comes_from_the_type(PartyType type, string? expected)
    {
        Assert.Equal(expected, Party.Create("Tiers", type, null, null, null, null, Now).Value.AccountCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Name_is_required(string? name)
    {
        Assert.Equal(
            PartiesErrors.Party.NameEmpty,
            Party.Create(name, PartyType.Client, null, null, null, null, Now).Error);
    }

    [Fact]
    public void Too_long_fields_are_refused()
    {
        Assert.Equal(
            PartiesErrors.Party.NameTooLong,
            Party.Create(new string('a', 31), PartyType.Client, null, null, null, null, Now).Error);
        Assert.Equal(
            PartiesErrors.Party.NniTooLong,
            Party.Create("Tiers", PartyType.Client, null, new string('1', 21), null, null, Now).Error);
        Assert.Equal(
            PartiesErrors.Party.AddressTooLong,
            Party.Create("Tiers", PartyType.Client, null, null, new string('a', 201), null, Now).Error);
        Assert.Equal(
            PartiesErrors.Party.NoteTooLong,
            Party.Create("Tiers", PartyType.Client, null, null, null, new string('a', 501), Now).Error);
    }

    [Fact]
    public void Unknown_type_is_refused()
    {
        Assert.Equal(
            PartiesErrors.Party.TypeInvalid,
            Party.Create("Tiers", (PartyType)9, null, null, null, null, Now).Error);
    }

    // --- Téléphone ---------------------------------------------------------

    [Theory]
    [InlineData("00221 77 123 45 67", "+221771234567")]
    [InlineData("+222-22-12-34-56", "+22222123456")]
    public void Phone_is_normalized_like_account_phones(string input, string expected)
    {
        Assert.Equal(expected, PartyPhone.Create(input).Value.Value);
    }

    [Theory]
    [InlineData("77 123 45 67")]
    [InlineData("+221 77 abc")]
    public void Phone_without_country_code_or_with_letters_is_refused_with_a_parties_code(string input)
    {
        Assert.Equal(PartiesErrors.Phone.Invalid, PartyPhone.Create(input).Error);
    }

    // --- Modification de la fiche ------------------------------------------

    [Fact]
    public void Update_replaces_the_whole_card()
    {
        var party = Party.Create(
            "Ancien Nom", PartyType.Client, PartyPhone.Create("+22222123456").Value, "111", "Ksar", "Note", Now).Value;
        var newPhone = PartyPhone.Create("+221771234567").Value;

        var result = party.Update("  Nouveau Nom  ", PartyType.Supplier, newPhone, " 222 ", " Médina ", null, inBalanceSheet: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nouveau Nom", party.Name);
        Assert.Equal(PartyType.Supplier, party.Type);
        Assert.Equal("40", party.AccountCode);
        Assert.Equal(newPhone, party.Phone);
        Assert.Equal("222", party.Nni);
        Assert.Equal("Médina", party.Address);
        Assert.Null(party.Note);
        Assert.False(party.InBalanceSheet);
    }

    [Fact]
    public void Update_with_an_invalid_field_changes_nothing()
    {
        var party = Party.Create("Nom", PartyType.Client, null, "111", null, null, Now).Value;

        var result = party.Update(" ", PartyType.Staff, null, null, null, null, inBalanceSheet: false);

        Assert.Equal(PartiesErrors.Party.NameEmpty, result.Error);
        Assert.Equal("Nom", party.Name);
        Assert.Equal(PartyType.Client, party.Type);
        Assert.Equal("111", party.Nni);
        Assert.True(party.InBalanceSheet);
    }

    // --- Solde initial par agence ------------------------------------------

    [Fact]
    public void Opening_balance_change_returns_only_the_difference_to_post()
    {
        var party = WithBothAgencies();

        Assert.Equal(10_000m, party.ChangeOpeningBalance(Nouakchott, 10_000m).Value);
        Assert.Equal(-6_000m, party.ChangeOpeningBalance(Nouakchott, 4_000m).Value);
        Assert.Equal(0m, party.ChangeOpeningBalance(Nouakchott, 4_000m).Value);
        Assert.Equal(-2_500m, party.ChangeOpeningBalance(Dakar, -2_500m).Value);

        Assert.Equal(4_000m, party.AgencyAccounts.Single(a => a.AgencyId == Nouakchott).OpeningBalance);
        Assert.Equal(-2_500m, party.AgencyAccounts.Single(a => a.AgencyId == Dakar).OpeningBalance);
    }

    [Fact]
    public void Opening_balance_needs_an_account_in_that_agency()
    {
        var party = WithBothAgencies();

        Assert.Equal(PartiesErrors.Party.AgencyAccountNotFound, party.ChangeOpeningBalance(Guid.NewGuid(), 100m).Error);
    }

    // --- Plafond de crédit -------------------------------------------------

    [Fact]
    public void Credit_limit_is_set_per_agency_zero_allowed_and_null_removes_it()
    {
        var party = WithBothAgencies();

        Assert.True(party.SetCreditLimit(Nouakchott, 500_000m).IsSuccess);
        Assert.True(party.SetCreditLimit(Dakar, 0m).IsSuccess);

        Assert.Equal(500_000m, party.AgencyAccounts.Single(a => a.AgencyId == Nouakchott).CreditLimit);
        Assert.Equal(0m, party.AgencyAccounts.Single(a => a.AgencyId == Dakar).CreditLimit);

        Assert.True(party.SetCreditLimit(Nouakchott, null).IsSuccess);
        Assert.Null(party.AgencyAccounts.Single(a => a.AgencyId == Nouakchott).CreditLimit);
    }

    [Fact]
    public void Negative_credit_limit_or_unknown_agency_is_refused()
    {
        var party = WithBothAgencies();

        Assert.Equal(PartiesErrors.Party.CreditLimitNegative, party.SetCreditLimit(Nouakchott, -1m).Error);
        Assert.Equal(PartiesErrors.Party.AgencyAccountNotFound, party.SetCreditLimit(Guid.NewGuid(), 100m).Error);
        Assert.All(party.AgencyAccounts, a => Assert.Null(a.CreditLimit));
    }

    // --- Comptes par agence ------------------------------------------------

    [Fact]
    public void Each_agency_gets_one_ledger_account()
    {
        var party = Party.Create("Tiers", PartyType.Client, null, null, null, null, Now).Value;
        var ledgerNkc = Guid.NewGuid();
        var ledgerDkr = Guid.NewGuid();

        party.AttachAgencyAccount(Nouakchott, ledgerNkc);
        party.AttachAgencyAccount(Dakar, ledgerDkr);

        Assert.Equal(2, party.AgencyAccounts.Count);
        Assert.Equal(ledgerNkc, party.AgencyAccounts.Single(a => a.AgencyId == Nouakchott).LedgerAccountId);
        Assert.All(party.AgencyAccounts, a => Assert.Null(a.CreditLimit));
        Assert.Throws<InvalidOperationException>(() => party.AttachAgencyAccount(Nouakchott, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => party.AttachAgencyAccount(Guid.Empty, Guid.NewGuid()));
    }

    private static Party WithBothAgencies()
    {
        var party = Party.Create("Tiers", PartyType.Client, null, null, null, null, Now).Value;
        party.AttachAgencyAccount(Nouakchott, Guid.NewGuid());
        party.AttachAgencyAccount(Dakar, Guid.NewGuid());
        return party;
    }
}
