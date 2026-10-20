using Hawala.SharedKernel.Money;
using Ledger.Contracts;
using Ledger.Domain;
using Ledger.Domain.Accounts;
using Ledger.Domain.Entries;

namespace Ledger.Domain.Tests.Entries;

public class JournalEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly Guid Author = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");
    private static readonly Guid Dakar = Guid.Parse("0199f0a0-0000-7000-8000-00000000a002");

    private readonly LedgerAccount _cashNkc = Treasury("Caisse NKC", Currency.MRO, Nouakchott);
    private readonly LedgerAccount _bankNkc = Treasury("Bankily NKC", Currency.MRO, Nouakchott);
    private readonly LedgerAccount _positionNkc = SystemAccount(SystemAccountRole.ExchangePosition, Currency.MRO, Nouakchott);
    private readonly LedgerAccount _positionDkr = SystemAccount(SystemAccountRole.ExchangePosition, Currency.XOF, Dakar);
    private readonly LedgerAccount _toDeliverDkr = SystemAccount(SystemAccountRole.TransfersToDeliver, Currency.XOF, Dakar);

    // --- Écriture équilibrée -----------------------------------------------

    [Fact]
    public void Balanced_entry_in_one_currency_is_recorded()
    {
        var result = Post(
            Debit(_cashNkc, Mro(10_000m)),
            Credit(_bankNkc, Mro(10_000m)));

        Assert.True(result.IsSuccess);
        var entry = result.Value;
        Assert.Equal(Today, entry.OperationDate);
        Assert.Equal(Now, entry.RecordedAt);
        Assert.Equal("Cash.InternalTransfer", entry.EntryType);
        Assert.Equal("Cash", entry.SourceModule);
        Assert.Equal(Author, entry.AuthorId);
        Assert.Equal("Virement interne", entry.Label);
        Assert.False(entry.IsReversal);
        Assert.Equal(2, entry.Lines.Count);
        Assert.Equal(10_000m, entry.Lines.Single(l => l.AccountId == _cashNkc.Id).SignedAmount);
        Assert.Equal(-10_000m, entry.Lines.Single(l => l.AccountId == _bankNkc.Id).SignedAmount);
    }

    [Fact]
    public void Transfer_entry_in_two_currencies_is_balanced_currency_by_currency()
    {
        // Transfert Nouakchott → Dakar : 5 000 000 FCFA payés 3 650 000 MRO (architecture, moteur comptable).
        var result = Post(
            Debit(_cashNkc, Mro(3_650_000m)),
            Credit(_positionNkc, Mro(3_650_000m)),
            Debit(_positionDkr, Xof(5_000_000m)),
            Credit(_toDeliverDkr, Xof(5_000_000m)));

        Assert.True(result.IsSuccess);
        Assert.All(
            result.Value.Lines.GroupBy(l => l.Currency),
            group => Assert.Equal(0m, group.Sum(l => l.SignedAmount)));
    }

    [Fact]
    public void Payment_split_over_several_accounts_is_balanced()
    {
        var result = Post(
            Debit(_cashNkc, Mro(2_000_000m)),
            Debit(_bankNkc, Mro(1_650_000m)),
            Credit(_positionNkc, Mro(3_650_000m)));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Past_operation_date_is_accepted()
    {
        var lastWeek = Today.AddDays(-7);

        var result = JournalEntry.Post(
            lastWeek, "Cash.InternalTransfer", "Cash", null, Author, "Saisie en retard",
            [Debit(_cashNkc, Mro(500m)), Credit(_bankNkc, Mro(500m))], Now);

        Assert.Equal(lastWeek, result.Value.OperationDate);
        Assert.Equal(Now, result.Value.RecordedAt);
    }

    [Fact]
    public void Future_operation_date_is_refused_today_is_accepted()
    {
        EntryLineDraft[] lines = [Debit(_cashNkc, Mro(100m)), Credit(_bankNkc, Mro(100m))];

        var tomorrow = JournalEntry.Post(Today.AddDays(1), "Cash.In", "Cash", null, Author, "Demain", lines, Now);
        var today = JournalEntry.Post(Today, "Cash.In", "Cash", null, Author, "Aujourd'hui", lines, Now);

        Assert.Equal(LedgerErrors.Entry.FutureDate, tomorrow.Error);
        Assert.True(today.IsSuccess);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public void Unbalanced_entry_is_refused()
    {
        var result = Post(Debit(_cashNkc, Mro(10_000m)), Credit(_bankNkc, Mro(9_999m)));

        Assert.Equal(LedgerErrors.Entry.Unbalanced, result.Error);
    }

    [Fact]
    public void Each_currency_must_balance_on_its_own()
    {
        // Les totaux « se compensent » si on additionne MRO et FCFA : interdit, chaque devise compte à part.
        var result = Post(
            Debit(_cashNkc, Mro(5_000m)),
            Credit(_positionNkc, Mro(4_000m)),
            Debit(_positionDkr, Xof(4_000m)),
            Credit(_toDeliverDkr, Xof(5_000m)));

        Assert.Equal(LedgerErrors.Entry.Unbalanced, result.Error);
    }

    [Fact]
    public void Entry_needs_at_least_two_lines()
    {
        Assert.Equal(LedgerErrors.Entry.TooFewLines, Post(Debit(_cashNkc, Mro(100m))).Error);
        Assert.Equal(LedgerErrors.Entry.TooFewLines, Post().Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-100")]
    public void Line_amount_must_be_strictly_positive(string amount)
    {
        var value = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        var result = Post(Debit(_cashNkc, Mro(value)), Credit(_bankNkc, Mro(value)));

        Assert.Equal(LedgerErrors.Line.AmountNotPositive, result.Error);
    }

    [Fact]
    public void Line_in_another_currency_than_its_account_is_refused()
    {
        var result = Post(Debit(_cashNkc, Xof(1000m)), Credit(_toDeliverDkr, Xof(1000m)));

        Assert.Equal(LedgerErrors.Line.CurrencyMismatch, result.Error);
    }

    [Fact]
    public void Closed_account_is_refused()
    {
        _bankNkc.Close();

        var result = Post(Debit(_cashNkc, Mro(100m)), Credit(_bankNkc, Mro(100m)));

        Assert.Equal(LedgerErrors.Account.Inactive, result.Error);
    }

    [Fact]
    public void Header_fields_are_required()
    {
        EntryLineDraft[] lines = [Debit(_cashNkc, Mro(100m)), Credit(_bankNkc, Mro(100m))];

        Assert.Equal(
            LedgerErrors.Entry.TypeEmpty,
            JournalEntry.Post(Today, " ", "Cash", null, Author, "Libellé", lines, Now).Error);
        Assert.Equal(
            LedgerErrors.Entry.SourceEmpty,
            JournalEntry.Post(Today, "Cash.In", null, null, Author, "Libellé", lines, Now).Error);
        Assert.Equal(
            LedgerErrors.Entry.AuthorRequired,
            JournalEntry.Post(Today, "Cash.In", "Cash", null, Guid.Empty, "Libellé", lines, Now).Error);
        Assert.Equal(
            LedgerErrors.Entry.LabelEmpty,
            JournalEntry.Post(Today, "Cash.In", "Cash", null, Author, "", lines, Now).Error);
        Assert.Equal(
            LedgerErrors.Entry.LabelEmpty,
            JournalEntry.Post(
                Today, "Cash.In", "Cash", null, Author, new string('a', JournalEntry.LabelMaxLength + 1), lines, Now).Error);
    }

    // --- Contre-passation --------------------------------------------------

    [Fact]
    public void Reversal_inverts_every_line_and_points_to_the_original()
    {
        var original = Post(
            Debit(_cashNkc, Mro(3_650_000m)),
            Credit(_positionNkc, Mro(3_650_000m)),
            Debit(_positionDkr, Xof(5_000_000m)),
            Credit(_toDeliverDkr, Xof(5_000_000m))).Value;
        var later = Now.AddHours(2);

        var result = original.Reverse(Today, Author, "  Transfert annulé à la demande du client  ", later);

        Assert.True(result.IsSuccess);
        var reversal = result.Value;
        Assert.NotEqual(original.Id, reversal.Id);
        Assert.True(reversal.IsReversal);
        Assert.Equal(original.Id, reversal.ReversesEntryId);
        Assert.Equal(original.EntryType, reversal.EntryType);
        Assert.Equal("Contre-passation : Virement interne", reversal.Label);
        Assert.Equal("Transfert annulé à la demande du client", reversal.Reason);
        Assert.Equal(later, reversal.RecordedAt);

        // Chaque compte revient à zéro quand on additionne l'originale et la contre-passation.
        Assert.All(
            original.Lines.Concat(reversal.Lines).GroupBy(l => l.AccountId),
            group => Assert.Equal(0m, group.Sum(l => l.SignedAmount)));

        // L'originale n'a pas bougé.
        Assert.False(original.IsReversal);
        Assert.Null(original.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Reversal_requires_a_reason(string? reason)
    {
        var original = SimpleEntry();

        Assert.Equal(LedgerErrors.Reversal.ReasonRequired, original.Reverse(Today, Author, reason, Now).Error);
    }

    [Fact]
    public void Too_long_reason_is_refused_not_cut()
    {
        var original = SimpleEntry();

        var result = original.Reverse(Today, Author, new string('m', JournalEntry.ReasonMaxLength + 1), Now);

        Assert.Equal(LedgerErrors.Reversal.ReasonTooLong, result.Error);
    }

    [Fact]
    public void Reversal_cannot_be_reversed()
    {
        var reversal = SimpleEntry().Reverse(Today, Author, "Erreur de saisie", Now).Value;

        var result = reversal.Reverse(Today, Author, "Annuler l'annulation", Now);

        Assert.Equal(LedgerErrors.Reversal.CannotReverseAReversal, result.Error);
    }

    [Fact]
    public void Reversal_requires_an_author()
    {
        Assert.Equal(
            LedgerErrors.Entry.AuthorRequired,
            SimpleEntry().Reverse(Today, Guid.Empty, "Erreur", Now).Error);
    }

    [Fact]
    public void Reversal_label_always_fits_its_column()
    {
        var longLabel = new string('a', JournalEntry.LabelMaxLength);
        var original = JournalEntry.Post(
            Today, "Cash.In", "Cash", null, Author, longLabel,
            [Debit(_cashNkc, Mro(100m)), Credit(_bankNkc, Mro(100m))], Now).Value;

        var reversal = original.Reverse(Today, Author, "Erreur", Now).Value;

        Assert.Equal(JournalEntry.StoredLabelMaxLength, reversal.Label.Length);
        Assert.EndsWith(longLabel, reversal.Label, StringComparison.Ordinal);
    }

    // --- Outils ------------------------------------------------------------

    private JournalEntry SimpleEntry() => Post(Debit(_cashNkc, Mro(100m)), Credit(_bankNkc, Mro(100m))).Value;

    private static Hawala.SharedKernel.Results.Result<JournalEntry> Post(params EntryLineDraft[] lines) =>
        JournalEntry.Post(Today, "Cash.InternalTransfer", "Cash", null, Author, "Virement interne", lines, Now);

    private static EntryLineDraft Debit(LedgerAccount account, Money amount) => new(account, EntrySide.Debit, amount);

    private static EntryLineDraft Credit(LedgerAccount account, Money amount) => new(account, EntrySide.Credit, amount);

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static Money Xof(decimal amount) => Money.Create(amount, Currency.XOF).Value;

    private static LedgerAccount Treasury(string name, Currency currency, Guid agency) =>
        LedgerAccount.Open(LedgerAccountKind.Treasury, Guid.NewGuid(), name, currency, agency, Now).Value;

    private static LedgerAccount SystemAccount(SystemAccountRole role, Currency currency, Guid agency) =>
        LedgerAccount.CreateSystem(role, agency, currency, Now).Value;
}
