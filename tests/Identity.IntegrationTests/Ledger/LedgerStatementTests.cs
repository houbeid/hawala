using Hawala.SharedKernel.Money;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Ledger;

/// <summary>Relevé d'un compte du grand livre sur un vrai PostgreSQL : solde de départ, lignes ordonnées, solde après chaque ligne.</summary>
[Collection(ApiCollection.Name)]
public sealed class LedgerStatementTests(ApiFixture fixture)
{
    private static readonly Guid Author = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    [Fact]
    public async Task Statement_starts_from_the_balance_before_the_period_and_runs_line_by_line()
    {
        var factory = await fixture.GetFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        var agency = Guid.NewGuid();
        var cash = (await ledger.OpenAccountAsync(
            LedgerAccountKind.Treasury, Guid.NewGuid(), "Caisse", Currency.MRO, agency, CancellationToken.None)).Value;
        var owner = (await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OwnerContributions, agency, Currency.MRO, CancellationToken.None)).Value;

        await PostAsync(ledger, new DateOnly(2026, 9, 30), cash, owner, 1_000m, "Avant la période");
        await PostAsync(ledger, new DateOnly(2026, 10, 5), cash, owner, 500m, "Approvisionnement");
        var outflow = await PostAsync(ledger, new DateOnly(2026, 10, 10), owner, cash, 300m, "Retrait");
        await PostAsync(ledger, new DateOnly(2026, 11, 2), cash, owner, 9_999m, "Après la période");
        var reversal = await ledger.ReverseAsync(outflow, new DateOnly(2026, 10, 12), Author, "Retrait saisi deux fois", CancellationToken.None);
        Assert.True(reversal.IsSuccess);

        var result = await ledger.GetStatementAsync(cash, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var statement = result.Value;
        Assert.Equal(Currency.MRO, statement.Currency);
        Assert.Equal(1_000m, statement.OpeningBalance);
        Assert.Equal(["Approvisionnement", "Retrait", "Contre-passation : Retrait"], statement.Lines.Select(l => l.Label));
        Assert.Equal([500m, 0m, 300m], statement.Lines.Select(l => l.Debit));
        Assert.Equal([0m, 300m, 0m], statement.Lines.Select(l => l.Credit));
        Assert.Equal([1_500m, 1_200m, 1_500m], statement.Lines.Select(l => l.BalanceAfter));
        Assert.Equal(1_500m, statement.ClosingBalance);

        var reversed = statement.Lines[2];
        Assert.True(reversed.IsReversal);
        Assert.Equal("Retrait saisi deux fois", reversed.Reason);
    }

    [Fact]
    public async Task Inverted_period_or_unknown_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();

        var inverted = await ledger.GetStatementAsync(Guid.NewGuid(), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), CancellationToken.None);
        var unknown = await ledger.GetStatementAsync(Guid.NewGuid(), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), CancellationToken.None);

        Assert.Equal(LedgerErrors.Statement.PeriodInvalid, inverted.Error);
        Assert.Equal(LedgerErrors.Account.NotFound, unknown.Error);
    }

    private static async Task<Guid> PostAsync(ILedger ledger, DateOnly date, Guid debit, Guid credit, decimal amount, string label)
    {
        var money = Money.Create(amount, Currency.MRO).Value;
        var result = await ledger.PostAsync(
            new PostEntryRequest(date, "Tests.Statement", "Tests", null, Author, label,
            [
                new PostEntryLine(debit, EntrySide.Debit, money),
                new PostEntryLine(credit, EntrySide.Credit, money),
            ]),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
