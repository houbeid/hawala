using Cash.Domain;
using Cash.Domain.PartyTransfers;
using Hawala.SharedKernel.Money;

namespace Cash.Domain.Tests.PartyTransfers;

/// <summary>Transfert de solde entre tiers (décisions du patron, 2 novembre 2026) : deux tiers différents, montant positif.</summary>
public class PartyBalanceTransferTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid A = Guid.Parse("0199f0a0-0000-7000-8000-00000000d001");
    private static readonly Guid B = Guid.Parse("0199f0a0-0000-7000-8000-00000000d002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    [Fact]
    public void A_transfer_keeps_both_parties_the_amount_and_a_trimmed_note()
    {
        var result = PartyBalanceTransfer.Create(Nkc, A, B, Mro(400_000m), "  Demandé par A  ", Now.AddHours(-1), "key", Agent, Now);

        Assert.True(result.IsSuccess);
        var transfer = result.Value;
        Assert.Equal(A, transfer.FromPartyId);
        Assert.Equal(B, transfer.ToPartyId);
        Assert.Equal(400_000m, transfer.Amount);
        Assert.Equal(Currency.MRO, transfer.Currency);
        Assert.Equal("Demandé par A", transfer.Note);
        Assert.Equal(Now.AddHours(-1), transfer.OperationAt);
    }

    [Fact]
    public void The_note_is_optional()
    {
        Assert.Null(PartyBalanceTransfer.Create(Nkc, A, B, Mro(1m), "   ", Now, "key", Agent, Now).Value.Note);
    }

    [Fact]
    public void Two_different_parties_are_required()
    {
        Assert.Equal(CashErrors.PartyTransfer.SameParty,
            PartyBalanceTransfer.Create(Nkc, A, A, Mro(1m), null, Now, "key", Agent, Now).Error);
        Assert.Equal(CashErrors.PartyTransfer.PartyRequired,
            PartyBalanceTransfer.Create(Nkc, null, B, Mro(1m), null, Now, "key", Agent, Now).Error);
        Assert.Equal(CashErrors.PartyTransfer.PartyRequired,
            PartyBalanceTransfer.Create(Nkc, A, Guid.Empty, Mro(1m), null, Now, "key", Agent, Now).Error);
    }

    [Fact]
    public void Zero_a_future_date_and_a_long_note_are_refused()
    {
        Assert.Equal(CashErrors.Movement.AmountInvalid,
            PartyBalanceTransfer.Create(Nkc, A, B, Mro(0m), null, Now, "key", Agent, Now).Error);
        Assert.Equal(CashErrors.Movement.FutureDate,
            PartyBalanceTransfer.Create(Nkc, A, B, Mro(1m), null, Now.AddMinutes(1), "key", Agent, Now).Error);
        Assert.Equal(CashErrors.Movement.NoteTooLong,
            PartyBalanceTransfer.Create(Nkc, A, B, Mro(1m), new string('n', 501), Now, "key", Agent, Now).Error);
    }
}
