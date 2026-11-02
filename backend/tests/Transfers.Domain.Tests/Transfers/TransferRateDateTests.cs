using Hawala.SharedKernel.Money;
using Transfers.Domain.Transfers;

namespace Transfers.Domain.Tests.Transfers;

/// <summary>Taux d'un jour précédent repris faute de taux du jour (2 novembre 2026) : sa date est gardée dans l'historique.</summary>
public class TransferRateDateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");
    private static readonly Guid Agent = Guid.Parse("0199f0a0-0000-7000-8000-00000000b001");
    private static readonly Guid NkcCash = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    private static Transfer NewTransfer(DateOnly? rateDate) => Transfer.Create(
        new NewTransfer(
            Nkc, Currency.MRO, Dkr, Currency.XOF,
            Money.Create(5_000_000m, Currency.XOF).Value, ExchangeRate.Create(3650m).Value, ExchangeRate.Create(3600m).Value,
            "Expéditeur fictif", "Bénéficiaire fictif", null,
            Now, [new PaymentLine(NkcCash, Money.Create(3_650_000m, Currency.MRO).Value)], RateDate: rateDate),
        "TR-NKC-20261102-000001", "hash", "key", Agent, Now).Value;

    [Fact]
    public void The_date_of_a_rate_taken_from_an_earlier_day_is_kept_in_the_created_line()
    {
        var transfer = NewTransfer(new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 11, 1), transfer.RateTakenFrom);
        var created = Assert.Single(transfer.Events);
        Assert.Equal(TransferEventType.Created, created.Type);
        Assert.Equal(new TransferFieldChange(Transfer.RateDateField, null, "2026-11-01"), Assert.Single(created.Changes));
    }

    [Fact]
    public void With_the_rate_of_the_day_nothing_is_recorded()
    {
        var transfer = NewTransfer(null);

        Assert.Null(transfer.RateTakenFrom);
        Assert.Empty(Assert.Single(transfer.Events).Changes);
    }
}
