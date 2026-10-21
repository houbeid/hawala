using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Actions sur le code (validées le 21 octobre 2026) : débloquer, livrer sans code, nouveau code.</summary>
public class TransferCodeActionsTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddHours(2);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid DkrCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c101");

    private static readonly PaymentLine[] FullDelivery = [new PaymentLine(DkrCash, Money.Create(5_000_000m, Currency.XOF).Value)];

    private static Transfer NewTransfer(bool locked = false)
    {
        var transfer = Transfer.Create(
            new NewTransfer(
                Nkc, Currency.MRO, Dkr, Currency.XOF,
                Money.Create(5_000_000m, Currency.XOF).Value, ExchangeRate.Create(3650m).Value, ExchangeRate.Create(3600m).Value,
                "Expéditeur fictif", "Bénéficiaire fictif", null,
                Created, [new PaymentLine(NkcCash, Money.Create(3_650_000m, Currency.MRO).Value)]),
            "TR-NKC-20261021-000001", "old-hash", "key", Agent, Created).Value;

        if (locked)
        {
            for (var i = 0; i < Transfer.MaxCodeAttempts; i++)
            {
                transfer.RegisterWrongCode(Agent, Now);
            }
        }

        return transfer;
    }

    // --- Débloquer ---------------------------------------------------------

    [Fact]
    public void Unlocking_gives_five_attempts_again()
    {
        var transfer = NewTransfer(locked: true);

        var result = transfer.UnlockCode(reason: null, Supervisor, Now);

        Assert.True(result.IsSuccess);
        Assert.False(transfer.IsCodeLocked);
        Assert.Equal(Transfer.MaxCodeAttempts, transfer.RemainingCodeAttempts);
        Assert.Equal(Supervisor, transfer.CodeUnlockedBy);
        Assert.Equal(Now, transfer.CodeUnlockedAt);
        Assert.Null(transfer.CodeUnlockReason);
    }

    [Fact]
    public void Only_a_locked_open_transfer_can_be_unlocked()
    {
        var notLocked = NewTransfer();
        var cancelled = NewTransfer(locked: true);
        cancelled.Cancel("Motif", Now, Supervisor, Now);

        Assert.Equal(TransfersErrors.Code.NotLocked, notLocked.UnlockCode("Motif", Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Delivery.Cancelled, cancelled.UnlockCode("Motif", Supervisor, Now).Error);
    }

    // --- Livrer sans code --------------------------------------------------

    [Fact]
    public void Supervisor_delivers_a_locked_transfer_without_code_with_id_document_and_reason()
    {
        var transfer = NewTransfer(locked: true);

        var result = transfer.DeliverWithoutCode(FullDelivery, Now, null, " CNI 0000000 ", " Code perdu ", Supervisor, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(TransferStatus.Delivered, transfer.Status);
        Assert.True(transfer.DeliveredWithoutCode);
        Assert.Equal("CNI 0000000", transfer.BeneficiaryIdDocument);
        Assert.Equal("Code perdu", transfer.DeliveryWithoutCodeReason);
    }

    [Theory]
    [InlineData(null, "Code perdu", "Transfers.Code.IdDocumentRequired")]
    [InlineData("CNI 0000000", " ", "Transfers.Code.ReasonRequired")]
    public void Id_document_and_reason_are_required(string? idDocument, string? reason, string expected)
    {
        var transfer = NewTransfer();

        var result = transfer.DeliverWithoutCode(FullDelivery, Now, null, idDocument, reason, Supervisor, Now);

        Assert.Equal(expected, result.Error.Code);
        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
    }

    [Fact]
    public void Delivery_rules_still_apply_without_code()
    {
        var transfer = NewTransfer();
        PaymentLine[] tooLittle = [new PaymentLine(DkrCash, Money.Create(1m, Currency.XOF).Value)];

        Assert.Equal(TransfersErrors.Delivery.TotalMismatch,
            transfer.DeliverWithoutCode(tooLittle, Now, null, "CNI 0000000", "Code perdu", Supervisor, Now).Error);
        Assert.False(transfer.DeliveredWithoutCode);
    }

    // --- Nouveau code -------------------------------------------------------

    [Fact]
    public void A_new_code_replaces_the_old_one_without_resetting_the_attempts()
    {
        var transfer = NewTransfer();
        transfer.RegisterWrongCode(Agent, Now);

        var result = transfer.ReplaceCode("new-hash", Agent, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", transfer.WithdrawalCodeHash);
        Assert.Equal(Transfer.MaxCodeAttempts - 1, transfer.RemainingCodeAttempts);
        Assert.Equal(Agent, transfer.CodeReplacedBy);
    }

    [Fact]
    public void A_new_code_does_not_unlock_a_locked_transfer()
    {
        var transfer = NewTransfer(locked: true);

        Assert.Equal(TransfersErrors.Delivery.CodeLocked, transfer.ReplaceCode("new-hash", Agent, Now).Error);
        Assert.Equal("old-hash", transfer.WithdrawalCodeHash);
    }
}
