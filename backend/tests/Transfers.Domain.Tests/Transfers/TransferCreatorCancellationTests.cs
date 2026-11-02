using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>
/// Annulation par l'auteur depuis l'écran du code (décision du patron, 2 novembre 2026) : son propre transfert,
/// intact et récent, motif facultatif.
/// </summary>
public class TransferCreatorCancellationTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid OtherAgent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b003");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    private static Transfer NewTransfer() => Transfer.Create(
        new NewTransfer(
            Nkc, Currency.MRO, Dkr, Currency.XOF,
            Money.Create(5_000_000m, Currency.XOF).Value, ExchangeRate.Create(3650m).Value, ExchangeRate.Create(3600m).Value,
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Created, [new PaymentLine(NkcCash, Money.Create(3_650_000m, Currency.MRO).Value)]),
        "TR-NKC-20261102-000001", "hash", "key", Agent, Created).Value;

    [Fact]
    public void The_author_cancels_right_after_creation_with_a_default_reason()
    {
        var transfer = NewTransfer();

        var result = transfer.CancelByCreator(null, Agent, Created.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Cancelled, transfer.Status);
        Assert.Equal(Transfer.DefaultCreatorCancellationReason, transfer.CancellationReason);
        Assert.Equal(Created.AddMinutes(2), transfer.CancellationOperationAt);
        Assert.Equal(Agent, transfer.CancelledBy);
    }

    [Fact]
    public void A_reason_given_by_the_author_is_kept()
    {
        var transfer = NewTransfer();

        Assert.True(transfer.CancelByCreator("  Mauvais montant  ", Agent, Created.AddMinutes(1)).IsSuccess);
        Assert.Equal("Mauvais montant", transfer.CancellationReason);
    }

    [Fact]
    public void Another_account_cannot_cancel_it_this_way()
    {
        var transfer = NewTransfer();

        Assert.Equal(TransfersErrors.Cancellation.NotCreator, transfer.CancelByCreator(null, OtherAgent, Created.AddMinutes(1)).Error);
        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
    }

    [Fact]
    public void Too_late_once_the_window_is_over()
    {
        var transfer = NewTransfer();
        var late = Created + Transfer.CreatorCancellationWindow + TimeSpan.FromSeconds(1);

        Assert.Equal(TransfersErrors.Cancellation.CreatorTooLate, transfer.CancelByCreator(null, Agent, late).Error);
        Assert.True(transfer.CancelByCreator(null, Agent, Created + Transfer.CreatorCancellationWindow).IsSuccess);
    }

    [Fact]
    public void Too_late_once_anything_else_happened_to_the_transfer()
    {
        var withWrongCode = NewTransfer();
        withWrongCode.RegisterWrongCode(OtherAgent, Created.AddMinutes(1));
        var withNewCode = NewTransfer();
        Assert.True(withNewCode.ReplaceCode("other-hash", Agent, Created.AddMinutes(1)).IsSuccess);

        Assert.Equal(TransfersErrors.Cancellation.CreatorTooLate,
            withWrongCode.CancelByCreator(null, Agent, Created.AddMinutes(2)).Error);
        Assert.Equal(TransfersErrors.Cancellation.CreatorTooLate,
            withNewCode.CancelByCreator(null, Agent, Created.AddMinutes(2)).Error);
    }

    [Fact]
    public void An_already_cancelled_transfer_is_not_cancelled_twice()
    {
        var transfer = NewTransfer();
        Assert.True(transfer.CancelByCreator(null, Agent, Created.AddMinutes(1)).IsSuccess);

        Assert.Equal(TransfersErrors.Cancellation.AlreadyCancelled,
            transfer.CancelByCreator(null, Agent, Created.AddMinutes(2)).Error);
    }
}
