using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Annulation (règle validée) : non livré seulement, motif obligatoire, verrouillé ensuite.</summary>
public class TransferCancellationTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddHours(2);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid DkrCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c101");

    private static Transfer NewTransfer() => Transfer.Create(
        new NewTransfer(
            Nkc, Currency.MRO, Dkr, Currency.XOF,
            Money.Create(5_000_000m, Currency.XOF).Value, ExchangeRate.Create(3650m).Value, ExchangeRate.Create(3600m).Value,
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Created, [new PaymentLine(NkcCash, Money.Create(3_650_000m, Currency.MRO).Value)]),
        "TR-NKC-20261021-000001", "hash", "key", Agent, Created).Value;

    [Fact]
    public void Cancelling_keeps_the_reason_the_author_and_the_dates()
    {
        var transfer = NewTransfer();

        var result = transfer.Cancel("  Client a renoncé  ", Now.AddMinutes(-10), Supervisor, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Cancelled, transfer.Status);
        Assert.Equal("Client a renoncé", transfer.CancellationReason);
        Assert.Equal(Now.AddMinutes(-10), transfer.CancellationOperationAt);
        Assert.Equal(Now, transfer.CancelledAt);
        Assert.Equal(Supervisor, transfer.CancelledBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Reason_is_required(string? reason)
    {
        Assert.Equal(TransfersErrors.Cancellation.ReasonRequired, NewTransfer().Cancel(reason, Now, Supervisor, Now).Error);
    }

    [Fact]
    public void Too_long_reason_is_refused_not_truncated()
    {
        Assert.Equal(TransfersErrors.Cancellation.ReasonTooLong,
            NewTransfer().Cancel(new string('m', 501), Now, Supervisor, Now).Error);
    }

    [Fact]
    public void Date_is_neither_in_the_future_nor_before_the_transfer()
    {
        var transfer = NewTransfer();

        Assert.Equal(TransfersErrors.Transfer.FutureDate, transfer.Cancel("Motif", Now.AddMinutes(1), Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Cancellation.DateBeforeTransfer,
            transfer.Cancel("Motif", Created.AddMinutes(-1), Supervisor, Now).Error);
        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
    }

    [Fact]
    public void A_delivered_transfer_cannot_be_cancelled_and_a_cancelled_one_cannot_be_delivered()
    {
        var delivered = NewTransfer();
        delivered.Deliver([new PaymentLine(DkrCash, Money.Create(5_000_000m, Currency.XOF).Value)], Now, null, Agent, Now);
        var cancelled = NewTransfer();
        cancelled.Cancel("Motif", Now, Supervisor, Now);

        Assert.Equal(TransfersErrors.Cancellation.Delivered, delivered.Cancel("Motif", Now, Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Cancellation.AlreadyCancelled, cancelled.Cancel("Motif", Now, Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Delivery.Cancelled, cancelled.EnsureDeliverable().Error);
    }

    [Fact]
    public void A_transfer_locked_by_wrong_codes_can_still_be_cancelled()
    {
        var transfer = NewTransfer();
        for (var i = 0; i < Transfer.MaxCodeAttempts; i++)
        {
            transfer.RegisterWrongCode(Agent, Now);
        }

        Assert.True(transfer.Cancel("Code perdu, client remboursé", Now, Supervisor, Now).IsSuccess);
    }

    [Fact]
    public void Cancellation_entry_is_attached_once_and_only_after_cancelling()
    {
        var transfer = NewTransfer();
        var entry = Guid.Parse("0199f0a0-0000-7000-8000-00000000e003");

        Assert.Throws<InvalidOperationException>(() => transfer.AttachCancellationEntry(entry));

        transfer.Cancel("Motif", Now, Supervisor, Now);
        transfer.AttachCancellationEntry(entry);

        Assert.Equal(entry, transfer.CancellationEntryId);
        Assert.Throws<InvalidOperationException>(() => transfer.AttachCancellationEntry(entry));
    }
}
