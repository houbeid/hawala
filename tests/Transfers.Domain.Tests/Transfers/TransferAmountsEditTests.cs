using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Modification d'argent par le Superviseur (validée le 21 octobre 2026).</summary>
public class TransferAmountsEditTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Created.AddHours(2);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid NkcWallet = Guid.Parse("0199f0a0-0000-7000-8000-00000000c002");
    private static readonly Guid DkrCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c101");
    private static readonly Guid Debtor = Guid.Parse("0199f0a0-0000-7000-8000-00000000d001");

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static Money Xof(decimal amount) => Money.Create(amount, Currency.XOF).Value;

    private static ExchangeRate Rate(decimal value) => ExchangeRate.Create(value).Value;

    // 5 000 000 FCFA vendus à 3 650 = 3 650 000 MRO, achetés à 3 600.
    private static Transfer NewTransfer() => Transfer.Create(
        new NewTransfer(
            Nkc, Currency.MRO, Dkr, Currency.XOF,
            Xof(5_000_000m), Rate(3650m), Rate(3600m),
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Created, [new PaymentLine(NkcCash, Mro(3_650_000m))]),
        "TR-NKC-20261021-000001", "hash", "key", Agent, Created).Value;

    [Fact]
    public void Supervisor_changes_amount_rate_and_split_and_the_mro_is_recomputed()
    {
        var transfer = NewTransfer();

        // 4 000 000 FCFA à 3 640 = 2 912 000 MRO, réparti caisse + wallet.
        var result = transfer.EditAmounts(
            1, Xof(4_000_000m), Rate(3640m), Created,
            [new PaymentLine(NkcCash, Mro(2_000_000m)), new PaymentLine(NkcWallet, Mro(912_000m))],
            creditPartyId: null, "Montant mal saisi", Supervisor, Now);

        Assert.True(result.Value);
        Assert.Equal(4_000_000m, transfer.AmountXof);
        Assert.Equal(2_912_000m, transfer.AmountMro);
        Assert.Equal(3640m, transfer.SaleRate.MroPer5000Xof);
        Assert.Equal(3600m, transfer.PurchaseRate.MroPer5000Xof);
        Assert.Equal(2, transfer.Payments.Count(p => p.Stage == PaymentStage.Creation));
        Assert.Equal(2, transfer.Version);

        var edited = transfer.Events.Last();
        Assert.Equal(TransferEventType.Edited, edited.Type);
        Assert.Equal("Montant mal saisi", edited.Reason);
        Assert.Contains(new TransferFieldChange("AmountMro", "3650000", "2912000"), edited.Changes);
        Assert.Contains(edited.Changes, c => c.Field == "Payments");
        Assert.DoesNotContain(edited.Changes, c => c.Field == "PurchaseRate");
    }

    [Fact]
    public void Switching_to_credit_moves_the_unpaid_part_on_the_party()
    {
        var transfer = NewTransfer();

        transfer.EditAmounts(
            1, Xof(5_000_000m), Rate(3650m), Created, [new PaymentLine(NkcCash, Mro(1_000_000m))],
            Debtor, "Le client paiera le reste plus tard", Supervisor, Now);

        Assert.Equal(Debtor, transfer.CreditPartyId);
        Assert.Equal(2_650_000m, transfer.CreditAmount);
    }

    [Fact]
    public void Nothing_changed_writes_nothing()
    {
        var transfer = NewTransfer();

        var result = transfer.EditAmounts(
            1, Xof(5_000_000m), Rate(3650m), Created, [new PaymentLine(NkcCash, Mro(3_650_000m))],
            creditPartyId: null, "Vérification", Supervisor, Now);

        Assert.False(result.Value);
        Assert.Single(transfer.Events);
        Assert.Equal(1, transfer.Version);
    }

    [Fact]
    public void A_reason_is_required_and_the_usual_rules_apply()
    {
        var transfer = NewTransfer();
        PaymentLine[] full = [new PaymentLine(NkcCash, Mro(3_650_000m))];

        Assert.Equal(TransfersErrors.Transfer.ReasonRequired,
            transfer.EditAmounts(1, Xof(4_000_000m), Rate(3650m), Created, full, null, " ", Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Payment.TotalMismatch,
            transfer.EditAmounts(1, Xof(4_000_000m), Rate(3650m), Created, full, null, "Motif", Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Transfer.FutureDate,
            transfer.EditAmounts(1, Xof(5_000_000m), Rate(3650m), Now.AddMinutes(1), full, null, "Motif", Supervisor, Now).Error);
        Assert.Equal(TransfersErrors.Transfer.VersionConflict,
            transfer.EditAmounts(7, Xof(5_000_000m), Rate(3650m), Created, full, null, "Motif", Supervisor, Now).Error);
        Assert.Equal(3_650_000m, transfer.AmountMro);
    }

    [Fact]
    public void A_delivered_transfer_keeps_its_amounts()
    {
        var transfer = NewTransfer();
        transfer.Deliver([new PaymentLine(DkrCash, Xof(5_000_000m))], Now, null, Agent, Now);

        Assert.Equal(TransfersErrors.Transfer.NotEditable,
            transfer.EditAmounts(transfer.Version, Xof(4_000_000m), Rate(3650m), Created,
                [new PaymentLine(NkcCash, Mro(2_920_000m))], null, "Motif", Supervisor, Now).Error);
    }

    [Fact]
    public void The_entry_in_force_is_replaced_only_while_undelivered()
    {
        var transfer = NewTransfer();
        var first = Guid.Parse("0199f0a0-0000-7000-8000-00000000e001");
        var second = Guid.Parse("0199f0a0-0000-7000-8000-00000000e002");
        transfer.AttachCreationEntry(first);

        transfer.ReplaceCreationEntry(second);

        Assert.Equal(second, transfer.CreationEntryId);
        Assert.Throws<ArgumentException>(() => transfer.ReplaceCreationEntry(second));
    }
}
