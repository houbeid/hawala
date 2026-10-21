using Cash.Domain;
using Cash.Domain.Movements;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;

namespace Cash.Domain.Tests.Movements;

public class CashMovementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid Cash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");
    private static readonly Guid Party = Guid.Parse("0199f0a0-0000-7000-8000-00000000d001");

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static NewCashMovement Receipt(decimal amount = 12_500.50m) =>
        new(Nkc, CashMovementType.Receipt, Cash, Party, Mro(amount), "  Remboursement  ", Now.AddHours(-1));

    private static Result<CashMovement> Create(NewCashMovement request, string? key = "key-1") =>
        CashMovement.Create(request, key, Agent, Now);

    [Fact]
    public void Receipt_from_a_party_is_incoming_and_keeps_decimals_in_mro()
    {
        var movement = Create(Receipt()).Value;

        Assert.Equal(CashMovementType.Receipt, movement.Type);
        Assert.True(movement.IsIncoming);
        Assert.Equal(12_500.50m, movement.Amount);
        Assert.Equal(Currency.MRO, movement.Currency);
        Assert.Equal(Party, movement.PartyId);
        Assert.Equal("Remboursement", movement.Note);
        Assert.Equal(Agent, movement.CreatedBy);
        Assert.Equal("key-1", movement.IdempotencyKey);
    }

    [Theory]
    [InlineData(CashMovementType.Receipt, true)]
    [InlineData(CashMovementType.OwnerContribution, true)]
    [InlineData(CashMovementType.Payment, false)]
    [InlineData(CashMovementType.OwnerWithdrawal, false)]
    public void The_type_sets_the_direction(CashMovementType type, bool incoming)
    {
        Assert.Equal(incoming, CashMovement.IsIncomingType(type));
    }

    [Fact]
    public void Receipt_and_payment_need_a_party()
    {
        Assert.Equal(CashErrors.Movement.PartyRequired, Create(Receipt() with { PartyId = null }).Error);
        Assert.Equal(CashErrors.Movement.PartyRequired,
            Create(Receipt() with { Type = CashMovementType.Payment, PartyId = null }).Error);
    }

    [Fact]
    public void Owner_contribution_and_withdrawal_take_no_party()
    {
        Assert.Equal(CashErrors.Movement.PartyNotAllowed,
            Create(Receipt() with { Type = CashMovementType.OwnerContribution }).Error);
        Assert.True(Create(Receipt() with { Type = CashMovementType.OwnerWithdrawal, PartyId = null }).IsSuccess);
    }

    [Fact]
    public void Amount_must_be_positive()
    {
        Assert.Equal(CashErrors.Movement.AmountInvalid, Create(Receipt(0m)).Error);
    }

    [Fact]
    public void Operation_date_cannot_be_in_the_future()
    {
        Assert.Equal(CashErrors.Movement.FutureDate, Create(Receipt() with { OperationAt = Now.AddMinutes(1) }).Error);
    }

    [Fact]
    public void Too_long_note_is_refused_and_empty_note_is_null()
    {
        Assert.Equal(CashErrors.Movement.NoteTooLong, Create(Receipt() with { Note = new string('n', 501) }).Error);
        Assert.Null(Create(Receipt() with { Note = "  " }).Value.Note);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Idempotency_key_is_required(string? key)
    {
        Assert.Equal(CashErrors.Movement.IdempotencyKeyRequired, Create(Receipt(), key).Error);
    }

    [Fact]
    public void Entry_is_attached_once()
    {
        var movement = Create(Receipt()).Value;
        var entry = Guid.Parse("0199f0a0-0000-7000-8000-00000000e001");

        movement.AttachEntry(entry);

        Assert.Equal(entry, movement.EntryId);
        Assert.Throws<InvalidOperationException>(() => movement.AttachEntry(entry));
    }

    // --- Virement interne --------------------------------------------------

    private static readonly Guid Bankily = Guid.Parse("0199f0a0-0000-7000-8000-00000000c002");

    private static NewCashMovement InternalTransfer() =>
        new(Nkc, CashMovementType.InternalTransfer, Cash, null, Mro(50_000m), null, Now, Bankily);

    [Fact]
    public void Internal_transfer_goes_from_one_account_to_another_and_is_neither_in_nor_out()
    {
        var movement = Create(InternalTransfer()).Value;

        Assert.True(movement.IsInternal);
        Assert.False(movement.IsIncoming);
        Assert.Equal(Cash, movement.TreasuryAccountId);
        Assert.Equal(Bankily, movement.DestinationTreasuryAccountId);
        Assert.Null(movement.PartyId);
    }

    [Fact]
    public void Internal_transfer_needs_another_destination_and_no_party()
    {
        Assert.Equal(CashErrors.Movement.DestinationRequired,
            Create(InternalTransfer() with { DestinationTreasuryAccountId = null }).Error);
        Assert.Equal(CashErrors.Movement.SameAccount,
            Create(InternalTransfer() with { DestinationTreasuryAccountId = Cash }).Error);
        Assert.Equal(CashErrors.Movement.PartyNotAllowed,
            Create(InternalTransfer() with { PartyId = Party }).Error);
    }

    [Fact]
    public void Only_an_internal_transfer_has_a_destination()
    {
        Assert.Equal(CashErrors.Movement.DestinationNotAllowed,
            Create(Receipt() with { DestinationTreasuryAccountId = Bankily }).Error);
    }

    // --- Annulation --------------------------------------------------------

    private static readonly Guid Supervisor = Guid.Parse("0199f0a0-0000-7000-8000-00000000b002");

    [Fact]
    public void Cancelling_keeps_the_reason_and_locks_the_movement()
    {
        var movement = Create(Receipt()).Value;

        Assert.True(movement.Cancel("  Saisie en double  ", Supervisor, Now).IsSuccess);

        Assert.Equal(CashMovementStatus.Cancelled, movement.Status);
        Assert.Equal("Saisie en double", movement.CancellationReason);
        Assert.Equal(Supervisor, movement.CancelledBy);
        Assert.Equal(CashErrors.Movement.AlreadyCancelled, movement.Cancel("Encore", Supervisor, Now).Error);
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        var movement = Create(Receipt()).Value;

        Assert.Equal(CashErrors.Movement.ReasonRequired, movement.Cancel(" ", Supervisor, Now).Error);
        Assert.Equal(CashErrors.Movement.ReasonTooLong, movement.Cancel(new string('m', 501), Supervisor, Now).Error);
        Assert.Equal(CashMovementStatus.Active, movement.Status);
    }

    [Fact]
    public void Cancellation_entry_is_attached_only_after_cancelling()
    {
        var movement = Create(Receipt()).Value;
        var entry = Guid.Parse("0199f0a0-0000-7000-8000-00000000e009");

        Assert.Throws<InvalidOperationException>(() => movement.AttachCancellationEntry(entry));
        movement.Cancel("Motif", Supervisor, Now);
        movement.AttachCancellationEntry(entry);
        Assert.Equal(entry, movement.CancellationEntryId);
    }
}
