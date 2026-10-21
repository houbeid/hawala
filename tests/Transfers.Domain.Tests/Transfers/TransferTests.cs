using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

public class TransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Cash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid Bankily = Guid.Parse("0199f0a0-0000-7000-8000-00000000c002");

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static Money Xof(decimal amount) => Money.Create(amount, Currency.XOF).Value;

    private static ExchangeRate Rate(decimal value) => ExchangeRate.Create(value).Value;

    // Nouakchott → Dakar : 5 000 000 FCFA vendus à 3 650 = 3 650 000 MRO payés à Nouakchott.
    private static NewTransfer NkcToDkr(params PaymentLine[] payments) => new(
        Nkc, Currency.MRO, Dkr, Currency.XOF,
        Xof(5_000_000m), Rate(3650m), Rate(3600m),
        "Expéditeur fictif 22200000", "Bénéficiaire fictif 770000000", null,
        Now.AddHours(-1),
        payments.Length == 0 ? [new PaymentLine(Cash, Mro(3_650_000m))] : payments);

    private static Result<Transfer> Create(NewTransfer request, string? key = "key-1") =>
        Transfer.Create(request, "TR-NKC-20261020-000001", "hash", key, Agent, Now);

    [Fact]
    public void Transfer_from_nouakchott_computes_mro_once_and_starts_not_delivered()
    {
        var result = Transfer.Create(NkcToDkr(), "TR-NKC-20261020-000001", "hash", " key-1 ", Agent, Now);

        Assert.True(result.IsSuccess);
        var transfer = result.Value;
        Assert.Equal(5_000_000m, transfer.AmountXof);
        Assert.Equal(3_650_000m, transfer.AmountMro);
        Assert.Equal(3650m, transfer.SaleRate.MroPer5000Xof);
        Assert.Equal(3600m, transfer.PurchaseRate.MroPer5000Xof);
        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
        Assert.Equal("key-1", transfer.IdempotencyKey);
        Assert.Equal(Mro(3_650_000m), transfer.PaidAmount);
        Assert.Equal(Xof(5_000_000m), transfer.AmountToDeliver);
        Assert.Equal(0, transfer.FailedCodeAttempts);
        Assert.Equal(Agent, transfer.CreatedBy);
        Assert.Equal(Now, transfer.CreatedAt);
        var payment = Assert.Single(transfer.Payments);
        Assert.Equal(PaymentStage.Creation, payment.Stage);
        Assert.Equal(Cash, payment.TreasuryAccountId);
        Assert.Equal(3_650_000m, payment.Amount);
        Assert.Equal(Currency.MRO, payment.Currency);
    }

    [Fact]
    public void Mro_amount_is_rounded_half_away_from_zero()
    {
        // 1 FCFA × 2 500 / 5000 = 0,5 → 1 (jamais l'arrondi vers le pair).
        var request = NkcToDkr(new PaymentLine(Cash, Mro(1m))) with { AmountXof = Xof(1m), SaleRate = Rate(2500m) };

        var result = Transfer.Create(request, "R", "hash", "k", Agent, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(1m, result.Value.AmountMro);
    }

    [Fact]
    public void Transfer_from_dakar_is_paid_in_fcfa_and_delivered_in_mro()
    {
        var request = new NewTransfer(
            Dkr, Currency.XOF, Nkc, Currency.MRO,
            Xof(1_000_000m), Rate(3604.77m), Rate(3580m),
            "Expéditeur fictif", "Bénéficiaire fictif", "Note fictive",
            Now, [new PaymentLine(Cash, Xof(600_000m)), new PaymentLine(Bankily, Xof(400_000m))]);

        var result = Transfer.Create(request, "R", "hash", "k", Agent, Now);

        Assert.True(result.IsSuccess);
        // 1 000 000 × 3 604,77 / 5000 = 720 954.
        Assert.Equal(720_954m, result.Value.AmountMro);
        Assert.Equal(Xof(1_000_000m), result.Value.PaidAmount);
        Assert.Equal(Mro(720_954m), result.Value.AmountToDeliver);
        Assert.Equal(2, result.Value.Payments.Count);
        Assert.Equal("Note fictive", result.Value.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Idempotency_key_is_required(string? key)
    {
        Assert.Equal(TransfersErrors.Transfer.IdempotencyKeyRequired, Create(NkcToDkr(), key).Error);
    }

    [Fact]
    public void Idempotency_key_longer_than_100_is_refused()
    {
        Assert.Equal(TransfersErrors.Transfer.IdempotencyKeyRequired, Create(NkcToDkr(), new string('k', 101)).Error);
    }

    [Fact]
    public void Same_agency_or_same_currency_is_not_a_corridor()
    {
        Assert.Equal(TransfersErrors.Transfer.CorridorInvalid, Create(NkcToDkr() with { DestinationAgencyId = Nkc }).Error);
        Assert.Equal(TransfersErrors.Transfer.CorridorInvalid, Create(NkcToDkr() with { DestinationCurrency = Currency.MRO }).Error);
    }

    [Fact]
    public void Amount_must_be_positive_fcfa()
    {
        Assert.Equal(TransfersErrors.Transfer.AmountInvalid, Create(NkcToDkr() with { AmountXof = Xof(0m) }).Error);
        Assert.Equal(TransfersErrors.Transfer.AmountInvalid, Create(NkcToDkr() with { AmountXof = Mro(5_000_000m) }).Error);
    }

    [Fact]
    public void Operation_date_cannot_be_in_the_future()
    {
        Assert.Equal(TransfersErrors.Transfer.FutureDate, Create(NkcToDkr() with { OperationAt = Now.AddMinutes(1) }).Error);
    }

    [Fact]
    public void Sender_and_beneficiary_are_required()
    {
        Assert.Equal(TransfersErrors.Transfer.SenderRequired, Create(NkcToDkr() with { SenderInfo = " " }).Error);
        Assert.Equal(TransfersErrors.Transfer.BeneficiaryRequired, Create(NkcToDkr() with { BeneficiaryInfo = null }).Error);
    }

    [Fact]
    public void Too_long_texts_are_refused_not_truncated()
    {
        Assert.Equal(TransfersErrors.Transfer.TextTooLong, Create(NkcToDkr() with { SenderInfo = new string('a', 201) }).Error);
        Assert.Equal(TransfersErrors.Transfer.TextTooLong, Create(NkcToDkr() with { Note = new string('a', 501) }).Error);
    }

    [Fact]
    public void Empty_note_is_stored_as_null()
    {
        Assert.Null(Create(NkcToDkr() with { Note = "   " }).Value.Note);
    }

    [Fact]
    public void At_least_one_payment_is_required()
    {
        Assert.Equal(TransfersErrors.Payment.Required, Create(NkcToDkr() with { Payments = [] }).Error);
    }

    [Fact]
    public void Payments_are_in_the_source_currency()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Xof(5_000_000m)));

        Assert.Equal(TransfersErrors.Payment.CurrencyMismatch, Create(request).Error);
    }

    [Fact]
    public void Payment_amounts_are_strictly_positive()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(3_650_000m)), new PaymentLine(Bankily, Mro(0m)));

        Assert.Equal(TransfersErrors.Payment.AmountNotPositive, Create(request).Error);
    }

    [Fact]
    public void A_payment_method_appears_once()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(1_000_000m)), new PaymentLine(Cash, Mro(2_650_000m)));

        Assert.Equal(TransfersErrors.Payment.AccountDuplicated, Create(request).Error);
    }

    [Fact]
    public void Payments_must_add_up_to_the_paid_amount()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(3_000_000m)), new PaymentLine(Bankily, Mro(600_000m)));

        Assert.Equal(TransfersErrors.Payment.TotalMismatch, Create(request).Error);
    }

    [Fact]
    public void Split_payment_with_decimals_in_mro_is_accepted()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(3_000_000.50m)), new PaymentLine(Bankily, Mro(649_999.50m)));

        Assert.True(Create(request).IsSuccess);
    }

    [Fact]
    public void Creation_entry_is_attached_only_once()
    {
        var transfer = Create(NkcToDkr()).Value;
        var entry = Guid.Parse("0199f0a0-0000-7000-8000-00000000e001");

        transfer.AttachCreationEntry(entry);

        Assert.Equal(entry, transfer.CreationEntryId);
        Assert.Throws<InvalidOperationException>(() => transfer.AttachCreationEntry(entry));
    }

    // --- Crédit -----------------------------------------------------------

    private static readonly Guid Debtor = Guid.Parse("0199f0a0-0000-7000-8000-00000000d001");

    [Fact]
    public void Partial_credit_puts_the_unpaid_part_on_the_chosen_party()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(1_000_000m))) with { CreditPartyId = Debtor };

        var transfer = Create(request).Value;

        Assert.Equal(Debtor, transfer.CreditPartyId);
        Assert.Equal(2_650_000m, transfer.CreditAmount);
        Assert.Equal(Mro(2_650_000m), transfer.CreditMoney);
    }

    [Fact]
    public void Full_credit_needs_no_payment()
    {
        var request = NkcToDkr() with { Payments = [], CreditPartyId = Debtor };

        var transfer = Create(request).Value;

        Assert.Equal(3_650_000m, transfer.CreditAmount);
        Assert.Empty(transfer.Payments);
    }

    [Fact]
    public void Credit_with_everything_paid_is_refused()
    {
        var request = NkcToDkr() with { CreditPartyId = Debtor };

        Assert.Equal(TransfersErrors.Credit.NotNeeded, Create(request).Error);
    }

    [Fact]
    public void Credit_never_allows_paying_more_than_the_amount()
    {
        var request = NkcToDkr(new PaymentLine(Cash, Mro(4_000_000m))) with { CreditPartyId = Debtor };

        Assert.Equal(TransfersErrors.Payment.TotalMismatch, Create(request).Error);
    }

    [Fact]
    public void Without_credit_there_is_no_credit_part()
    {
        var transfer = Create(NkcToDkr()).Value;

        Assert.Null(transfer.CreditPartyId);
        Assert.Equal(0m, transfer.CreditAmount);
        Assert.Null(transfer.CreditMoney);
    }

    // --- Bénéfice ---------------------------------------------------------

    [Fact]
    public void Profit_of_a_transfer_sent_from_nouakchott_is_the_commission()
    {
        // Exemple validé : 5 000 000 FCFA vendus à 3 650, achetés à 3 600 : 50 000 MRO.
        Assert.Equal(50_000m, Create(NkcToDkr()).Value.ProfitMro);
    }

    [Fact]
    public void Profit_of_a_transfer_sent_from_dakar_follows_the_reverse_formula()
    {
        // On reçoit 1 000 000 FCFA (valant 720 000 MRO au prix d'achat 3 600) et on paie 715 000 MRO (prix de vente 3 575).
        var request = new NewTransfer(
            Dkr, Currency.XOF, Nkc, Currency.MRO,
            Xof(1_000_000m), Rate(3575m), Rate(3600m),
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Now, [new PaymentLine(Cash, Xof(1_000_000m))]);

        Assert.Equal(5_000m, Create(request).Value.ProfitMro);
    }

    [Fact]
    public void Profit_keeps_full_precision()
    {
        // 1 FCFA : M = 1 (0,73 arrondi), au prix d'achat 0,72 → B = 0,28.
        var request = NkcToDkr(new PaymentLine(Cash, Mro(1m))) with { AmountXof = Xof(1m) };

        Assert.Equal(0.28m, Create(request).Value.ProfitMro);
    }

    [Fact]
    public void Amount_in_a_currency_reads_the_stored_amounts()
    {
        var transfer = Create(NkcToDkr()).Value;

        Assert.Equal(3_650_000m, transfer.AmountIn(Currency.MRO));
        Assert.Equal(5_000_000m, transfer.AmountIn(Currency.XOF));
    }
}
