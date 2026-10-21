using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Livraison (validée le 20 octobre 2026) : une seule, éventuellement répartie ; code faux compté par transfert, blocage au 5e.</summary>
public class TransferDeliveryTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddHours(2);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid DkrCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c101");
    private static readonly Guid DkrWave = Guid.Parse("0199f0a0-0000-7000-8000-00000000c102");

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static Money Xof(decimal amount) => Money.Create(amount, Currency.XOF).Value;

    // Nouakchott → Dakar : 5 000 000 FCFA à livrer à Dakar.
    private static Transfer NewTransfer() => Transfer.Create(
        new NewTransfer(
            Nkc, Currency.MRO, Dkr, Currency.XOF,
            Xof(5_000_000m), ExchangeRate.Create(3650m).Value, ExchangeRate.Create(3600m).Value,
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Created, [new PaymentLine(NkcCash, Mro(3_650_000m))]),
        "TR-NKC-20261020-000001", "hash", "key", Agent, Created).Value;

    [Fact]
    public void Split_delivery_marks_the_transfer_delivered()
    {
        var transfer = NewTransfer();

        var result = transfer.Deliver(
            [new PaymentLine(DkrCash, Xof(3_000_000m)), new PaymentLine(DkrWave, Xof(2_000_000m))],
            Now.AddMinutes(-5), " Remis au guichet ", Agent, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Delivered, transfer.Status);
        Assert.Equal(Now.AddMinutes(-5), transfer.DeliveryOperationAt);
        Assert.Equal(Now, transfer.DeliveredAt);
        Assert.Equal(Agent, transfer.DeliveredBy);
        Assert.Equal("Remis au guichet", transfer.DeliveryNote);
        var delivery = transfer.Payments.Where(p => p.Stage == PaymentStage.Delivery).ToList();
        Assert.Equal(2, delivery.Count);
        Assert.All(delivery, p => Assert.Equal(Currency.XOF, p.Currency));
    }

    [Fact]
    public void Only_one_delivery()
    {
        var transfer = NewTransfer();
        transfer.Deliver([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Agent, Now);

        var again = transfer.Deliver([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Agent, Now);

        Assert.Equal(TransfersErrors.Delivery.AlreadyDelivered, again.Error);
    }

    [Fact]
    public void Delivered_total_must_equal_the_amount_to_deliver_in_the_destination_currency()
    {
        var transfer = NewTransfer();

        Assert.Equal(TransfersErrors.Delivery.TotalMismatch,
            transfer.ValidateDelivery([new PaymentLine(DkrCash, Xof(4_999_999m))], Now, null, Now).Error);
        Assert.Equal(TransfersErrors.Payment.CurrencyMismatch,
            transfer.ValidateDelivery([new PaymentLine(DkrCash, Mro(3_650_000m))], Now, null, Now).Error);
        Assert.Equal(TransfersErrors.Payment.Required,
            transfer.ValidateDelivery([], Now, null, Now).Error);
        Assert.Equal(TransfersErrors.Payment.AccountDuplicated,
            transfer.ValidateDelivery(
                [new PaymentLine(DkrCash, Xof(2_500_000m)), new PaymentLine(DkrCash, Xof(2_500_000m))], Now, null, Now).Error);
    }

    [Fact]
    public void Delivery_date_is_neither_in_the_future_nor_before_the_transfer()
    {
        var transfer = NewTransfer();
        PaymentLine[] lines = [new PaymentLine(DkrCash, Xof(5_000_000m))];

        Assert.Equal(TransfersErrors.Transfer.FutureDate, transfer.ValidateDelivery(lines, Now.AddMinutes(1), null, Now).Error);
        Assert.Equal(TransfersErrors.Delivery.DateBeforeTransfer, transfer.ValidateDelivery(lines, Created.AddMinutes(-1), null, Now).Error);
        Assert.True(transfer.ValidateDelivery(lines, Created, null, Now).IsSuccess);
    }

    [Fact]
    public void Validation_changes_nothing()
    {
        var transfer = NewTransfer();

        transfer.ValidateDelivery([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Now);

        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
        Assert.DoesNotContain(transfer.Payments, p => p.Stage == PaymentStage.Delivery);
    }

    [Fact]
    public void Wrong_codes_count_down_then_lock_the_transfer_at_the_fifth()
    {
        var transfer = NewTransfer();

        Assert.Equal(TransfersErrors.Delivery.WrongCode, transfer.RegisterWrongCode());
        Assert.Equal(4, transfer.RemainingCodeAttempts);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(TransfersErrors.Delivery.WrongCode, transfer.RegisterWrongCode());
        }

        Assert.Equal(1, transfer.RemainingCodeAttempts);
        var fifth = transfer.RegisterWrongCode();

        Assert.Equal(TransfersErrors.Delivery.CodeLocked, fifth);
        Assert.True(transfer.IsCodeLocked);
        Assert.Equal(0, transfer.RemainingCodeAttempts);
        Assert.Equal(TransfersErrors.Delivery.CodeLocked, transfer.RegisterWrongCode());
        Assert.Equal(Transfer.MaxCodeAttempts, transfer.FailedCodeAttempts);
        Assert.Equal(TransfersErrors.Delivery.CodeLocked, transfer.EnsureDeliverable().Error);
        Assert.Equal(TransfersErrors.Delivery.CodeLocked,
            transfer.Deliver([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Agent, Now).Error);
    }

    [Fact]
    public void Delivery_entry_is_attached_once_and_only_after_delivery()
    {
        var transfer = NewTransfer();
        var entry = Guid.Parse("0199f0a0-0000-7000-8000-00000000e002");

        Assert.Throws<InvalidOperationException>(() => transfer.AttachDeliveryEntry(entry));

        transfer.Deliver([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Agent, Now);
        transfer.AttachDeliveryEntry(entry);

        Assert.Equal(entry, transfer.DeliveryEntryId);
        Assert.Throws<InvalidOperationException>(() => transfer.AttachDeliveryEntry(entry));
    }
}
