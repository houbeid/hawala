using Hawala.SharedKernel.Money;
using Transfers.Domain;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Historique, version et modification des textes (validés le 21 octobre 2026).</summary>
public class TransferHistoryTests
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
            "Expéditeur fictif", "Bénéficiaire fictif", "Note fictive",
            Created, [new PaymentLine(NkcCash, Money.Create(3_650_000m, Currency.MRO).Value)]),
        "TR-NKC-20261021-000001", "hash", "key", Agent, Created).Value;

    [Fact]
    public void Creation_writes_the_first_line_and_version_one()
    {
        var transfer = NewTransfer();

        var created = Assert.Single(transfer.Events);
        Assert.Equal(TransferEventType.Created, created.Type);
        Assert.Equal(Agent, created.AuthorId);
        Assert.Equal(Created, created.At);
        Assert.Empty(created.Changes);
        Assert.Equal(1, transfer.Version);
    }

    [Fact]
    public void Each_action_adds_a_line_and_raises_the_version()
    {
        var transfer = NewTransfer();

        transfer.RegisterWrongCode(Agent, Now);
        transfer.ReplaceCode("new-hash", Agent, Now);
        transfer.Deliver([new PaymentLine(DkrCash, Money.Create(5_000_000m, Currency.XOF).Value)], Now, null, Agent, Now);

        Assert.Equal(
            [TransferEventType.Created, TransferEventType.WrongCode, TransferEventType.CodeReplaced, TransferEventType.Delivered],
            transfer.Events.Select(e => e.Type));
        Assert.Equal(4, transfer.Version);
        Assert.Equal(new TransferFieldChange("FailedCodeAttempts", "0", "1"), Assert.Single(transfer.Events[1].Changes));
        Assert.Equal(new TransferFieldChange("Status", "NotDelivered", "Delivered"), Assert.Single(transfer.Events[3].Changes));
    }

    [Fact]
    public void Cancellation_and_unlock_keep_their_reason()
    {
        var locked = NewTransfer();
        for (var i = 0; i < Transfer.MaxCodeAttempts; i++)
        {
            locked.RegisterWrongCode(Agent, Now);
        }

        locked.UnlockCode("Bénéficiaire vérifié", Supervisor, Now);
        locked.Cancel("Client a renoncé", Now, Supervisor, Now);

        Assert.Equal("Bénéficiaire vérifié", locked.Events.Single(e => e.Type == TransferEventType.CodeUnlocked).Reason);
        Assert.Equal("Client a renoncé", locked.Events.Single(e => e.Type == TransferEventType.Cancelled).Reason);
    }

    [Fact]
    public void Delivery_without_code_keeps_the_reason_but_never_the_id_document()
    {
        var transfer = NewTransfer();

        transfer.DeliverWithoutCode(
            [new PaymentLine(DkrCash, Money.Create(5_000_000m, Currency.XOF).Value)], Now, null, "CNI 0000000", "Code perdu", Supervisor, Now);

        var line = transfer.Events.Last();
        Assert.Equal(TransferEventType.DeliveredWithoutCode, line.Type);
        Assert.Equal("Code perdu", line.Reason);
        Assert.DoesNotContain(line.Changes, c => c.Before == "CNI 0000000" || c.After == "CNI 0000000");
    }

    // --- Modifier les textes -----------------------------------------------

    [Fact]
    public void Editing_texts_records_only_what_changed()
    {
        var transfer = NewTransfer();

        var result = transfer.EditTexts(1, "Expéditeur fictif", " Autre bénéficiaire ", null, "Faute de frappe", Agent, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("Autre bénéficiaire", transfer.BeneficiaryInfo);
        Assert.Null(transfer.Note);
        Assert.Equal(2, transfer.Version);
        var edited = transfer.Events.Last();
        Assert.Equal(TransferEventType.Edited, edited.Type);
        Assert.Equal("Faute de frappe", edited.Reason);
        Assert.Equal(
            [
                new TransferFieldChange("BeneficiaryInfo", "Bénéficiaire fictif", "Autre bénéficiaire"),
                new TransferFieldChange("Note", "Note fictive", null),
            ],
            edited.Changes);
    }

    [Fact]
    public void Editing_nothing_writes_nothing()
    {
        var transfer = NewTransfer();

        Assert.True(transfer.EditTexts(1, "Expéditeur fictif", "Bénéficiaire fictif", "Note fictive", null, Agent, Now).IsSuccess);
        Assert.Single(transfer.Events);
        Assert.Equal(1, transfer.Version);
    }

    [Fact]
    public void A_stale_version_is_a_conflict()
    {
        var transfer = NewTransfer();
        transfer.RegisterWrongCode(Agent, Now);

        Assert.Equal(TransfersErrors.Transfer.VersionConflict,
            transfer.EditTexts(1, "Expéditeur fictif", "Autre", null, null, Agent, Now).Error);
    }

    [Fact]
    public void Only_an_undelivered_transfer_is_edited_and_texts_stay_valid()
    {
        var delivered = NewTransfer();
        delivered.Deliver([new PaymentLine(DkrCash, Money.Create(5_000_000m, Currency.XOF).Value)], Now, null, Agent, Now);
        var open = NewTransfer();

        Assert.Equal(TransfersErrors.Transfer.NotEditable,
            delivered.EditTexts(delivered.Version, "Expéditeur fictif", "Autre", null, null, Agent, Now).Error);
        Assert.Equal(TransfersErrors.Transfer.BeneficiaryRequired,
            open.EditTexts(1, "Expéditeur fictif", " ", null, null, Agent, Now).Error);
        Assert.Equal(TransfersErrors.Transfer.TextTooLong,
            open.EditTexts(1, new string('e', 201), "Bénéficiaire fictif", null, null, Agent, Now).Error);
        Assert.Equal(TransfersErrors.Transfer.ReasonTooLong,
            open.EditTexts(1, "Expéditeur fictif", "Autre", null, new string('m', 501), Agent, Now).Error);
    }
}
