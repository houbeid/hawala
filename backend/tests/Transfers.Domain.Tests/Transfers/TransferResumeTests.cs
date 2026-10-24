using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Reprise d'un transfert non livré de l'ancienne app (mise en service, décisions du 24 octobre 2026).</summary>
public class TransferResumeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Original = new(2026, 10, 18, 14, 30, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

    private static ResumedTransfer Request(decimal mro = 36_501m, DateTimeOffset? at = null, string? beneficiary = "Bénéficiaire fictif") => new(
        Nkc, Currency.MRO, Dkr, Currency.XOF,
        Money.Create(50_000m, Currency.XOF).Value, Money.Create(mro, Currency.MRO).Value, ExchangeRate.Create(3650m).Value,
        "Expéditeur fictif", beneficiary, null, at ?? Original);

    [Fact]
    public void A_resumed_transfer_keeps_the_old_amounts_and_date_without_payment()
    {
        var transfer = Transfer.Resume(Request(), "TR-OLD-000043", "hash", "resume:TR-OLD-000043", Supervisor, Now).Value;

        Assert.Equal(TransferStatus.NotDelivered, transfer.Status);
        // Le MRO de l'ancienne app est gardé tel quel (pas recalculé : 50 000 × 3 650 / 5 000 = 36 500).
        Assert.Equal(36_501m, transfer.AmountMro);
        Assert.Equal(Original, transfer.OperationAt);
        Assert.Empty(transfer.Payments);
        Assert.Null(transfer.CreditPartyId);
        Assert.True(transfer.IsResumed);
        Assert.Equal(TransferEventType.Resumed, Assert.Single(transfer.Events).Type);
        Assert.Equal(50_000m, transfer.AmountToDeliver.Amount);
    }

    [Fact]
    public void A_future_date_or_a_missing_beneficiary_is_refused()
    {
        Assert.Equal(
            TransfersErrors.Transfer.FutureDate,
            Transfer.Resume(Request(at: Now.AddDays(1)), "TR-1", "h", "k", Supervisor, Now).Error);
        Assert.Equal(
            TransfersErrors.Transfer.BeneficiaryRequired,
            Transfer.Resume(Request(beneficiary: " "), "TR-1", "h", "k", Supervisor, Now).Error);
    }

    [Fact]
    public void Its_money_cannot_be_edited()
    {
        var transfer = Transfer.Resume(Request(), "TR-OLD-1", "hash", "resume:TR-OLD-1", Supervisor, Now).Value;

        var edited = transfer.EditAmounts(
            1, Money.Create(60_000m, Currency.XOF).Value, ExchangeRate.Create(3650m).Value, Original, [], null, "Correction", Supervisor, Now);

        Assert.Equal(TransfersErrors.Transfer.ResumedAmountsLocked, edited.Error);
    }
}
