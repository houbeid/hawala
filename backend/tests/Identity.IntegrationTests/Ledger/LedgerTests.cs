using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Identity.IntegrationTests.Ledger;

/// <summary>
/// Grand livre sur un vrai PostgreSQL : écritures, soldes, contre-passation, et garanties imposées par la base
/// (immuabilité, équilibre par devise et deux lignes au COMMIT), vérifiées en SQL direct pour contourner le code.
/// La base est partagée : chaque test travaille sur ses propres agences (ids aléatoires), donc ses propres
/// comptes système, et ne lit jamais le solde d'un compte qu'un autre test utilise.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class LedgerTests(ApiFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 10, 18);
    private static readonly Guid Author = Guid.Parse("0199f0a0-0000-7000-8000-00000000c001");

    // --- Écritures et soldes -----------------------------------------------

    [Fact]
    public async Task Posting_an_entry_moves_the_balances_debit_minus_credit()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var client = await OpenAsync(factory, LedgerAccountKind.Party, "Client fictif", Currency.MRO, agency);

        // Le client prend 25 000 MRO à crédit : il nous doit (rouge), la caisse baisse.
        await PostOkAsync(factory, Today, Line(client, EntrySide.Debit, Mro(25_000.50m)), Line(cash, EntrySide.Credit, Mro(25_000.50m)));

        var balances = await BalancesAsync(factory, [cash, client]);
        Assert.Equal(Mro(-25_000.50m), balances[cash]);
        Assert.Equal(Mro(25_000.50m), balances[client]);
    }

    [Fact]
    public async Task Transfer_entry_balances_each_currency_through_the_exchange_position()
    {
        var factory = await fixture.GetFactoryAsync();
        var nouakchott = Guid.NewGuid();
        var dakar = Guid.NewGuid();
        var cashNkc = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse NKC", Currency.MRO, nouakchott);
        var positionNkc = await SystemAsync(factory, SystemAccountRole.ExchangePosition, nouakchott, Currency.MRO);
        var positionDkr = await SystemAsync(factory, SystemAccountRole.ExchangePosition, dakar, Currency.XOF);
        var toDeliverDkr = await SystemAsync(factory, SystemAccountRole.TransfersToDeliver, dakar, Currency.XOF);

        // 5 000 000 FCFA vendus à 3 650 : 3 650 000 MRO encaissés à Nouakchott, à livrer à Dakar.
        await PostOkAsync(
            factory,
            Today,
            Line(cashNkc, EntrySide.Debit, Mro(3_650_000m)),
            Line(positionNkc, EntrySide.Credit, Mro(3_650_000m)),
            Line(positionDkr, EntrySide.Debit, Xof(5_000_000m)),
            Line(toDeliverDkr, EntrySide.Credit, Xof(5_000_000m)));

        var balances = await BalancesAsync(factory, [cashNkc, positionNkc, positionDkr, toDeliverDkr]);
        Assert.Equal(Mro(3_650_000m), balances[cashNkc]);
        Assert.Equal(Mro(-3_650_000m), balances[positionNkc]);
        Assert.Equal(Xof(5_000_000m), balances[positionDkr]);
        Assert.Equal(Xof(-5_000_000m), balances[toDeliverDkr]);
    }

    [Fact]
    public async Task Balance_as_of_a_date_ignores_later_operations()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var owner = await SystemAsync(factory, SystemAccountRole.OwnerContributions, agency, Currency.MRO);

        // Saisie antidatée (règle validée) puis opération du jour.
        await PostOkAsync(factory, Today.AddDays(-3), Line(cash, EntrySide.Debit, Mro(1_000m)), Line(owner, EntrySide.Credit, Mro(1_000m)));
        await PostOkAsync(factory, Today, Line(cash, EntrySide.Debit, Mro(500m)), Line(owner, EntrySide.Credit, Mro(500m)));

        Assert.Equal(Mro(1_000m), (await BalancesAsync(factory, [cash], asOf: Today.AddDays(-1)))[cash]);
        Assert.Equal(Mro(1_500m), (await BalancesAsync(factory, [cash], asOf: Today))[cash]);
        Assert.Equal(Mro(0m), (await BalancesAsync(factory, [cash], asOf: Today.AddDays(-10)))[cash]);
    }

    [Fact]
    public async Task Account_without_entries_has_a_zero_balance_and_unknown_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var account = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse vide", Currency.XOF, Guid.NewGuid());

        Assert.Equal(Xof(0m), (await BalancesAsync(factory, [account]))[account]);

        var unknown = await WithLedgerAsync(factory, ledger =>
            ledger.GetBalancesAsync([account, Guid.NewGuid()], asOf: null, CancellationToken.None));
        Assert.Equal(LedgerErrors.Account.NotFound, unknown.Error);
    }

    [Fact]
    public async Task Unbalanced_entry_is_refused_by_the_code()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);

        var result = await PostAsync(factory, Today, Line(cash, EntrySide.Debit, Mro(100m)), Line(bank, EntrySide.Credit, Mro(99m)));

        Assert.Equal(LedgerErrors.Entry.Unbalanced, result.Error);
        Assert.Equal(Mro(0m), (await BalancesAsync(factory, [cash]))[cash]);
    }

    [Fact]
    public async Task Entry_on_an_unknown_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, Guid.NewGuid());

        var result = await PostAsync(
            factory, Today, Line(cash, EntrySide.Debit, Mro(100m)), Line(Guid.NewGuid(), EntrySide.Credit, Mro(100m)));

        Assert.Equal(LedgerErrors.Account.NotFound, result.Error);
    }

    // --- Comptes système ---------------------------------------------------

    [Fact]
    public async Task System_account_is_created_once_per_agency_and_role()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();

        var first = await SystemAsync(factory, SystemAccountRole.OpeningBalances, agency, Currency.MRO);
        var second = await SystemAsync(factory, SystemAccountRole.OpeningBalances, agency, Currency.MRO);
        var otherRole = await SystemAsync(factory, SystemAccountRole.Adjustments, agency, Currency.MRO);

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherRole);

        var otherCurrency = await WithLedgerAsync(factory, ledger =>
            ledger.GetOrCreateSystemAccountAsync(SystemAccountRole.OpeningBalances, agency, Currency.XOF, CancellationToken.None));
        Assert.Equal(LedgerErrors.Line.CurrencyMismatch, otherCurrency.Error);
    }

    // --- Contre-passation --------------------------------------------------

    [Fact]
    public async Task Reversal_brings_every_balance_back_and_happens_only_once()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var client = await OpenAsync(factory, LedgerAccountKind.Party, "Client fictif", Currency.MRO, agency);
        var entry = await PostOkAsync(factory, Today, Line(client, EntrySide.Debit, Mro(7_000m)), Line(cash, EntrySide.Credit, Mro(7_000m)));

        var reversal = await ReverseAsync(factory, entry, "Saisie sur le mauvais client");

        Assert.True(reversal.IsSuccess);
        var balances = await BalancesAsync(factory, [cash, client]);
        Assert.Equal(Mro(0m), balances[cash]);
        Assert.Equal(Mro(0m), balances[client]);

        Assert.Equal(LedgerErrors.Reversal.AlreadyReversed, (await ReverseAsync(factory, entry, "Encore")).Error);
        Assert.Equal(LedgerErrors.Reversal.CannotReverseAReversal, (await ReverseAsync(factory, reversal.Value, "Rétablir")).Error);
    }

    [Fact]
    public async Task Reversal_requires_a_reason_and_an_existing_entry()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);
        var entry = await PostOkAsync(factory, Today, Line(cash, EntrySide.Debit, Mro(100m)), Line(bank, EntrySide.Credit, Mro(100m)));

        Assert.Equal(LedgerErrors.Reversal.ReasonRequired, (await ReverseAsync(factory, entry, "  ")).Error);
        Assert.Equal(LedgerErrors.Entry.NotFound, (await ReverseAsync(factory, Guid.NewGuid(), "Motif")).Error);
    }

    // --- Garanties de la base (SQL direct, le code est volontairement contourné) ---

    [Fact]
    public async Task Database_refuses_to_update_or_delete_an_entry_or_a_line()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);
        var entry = await PostOkAsync(factory, Today, Line(cash, EntrySide.Debit, Mro(100m)), Line(bank, EntrySide.Credit, Mro(100m)));

        await AssertSqlRefusedAsync(factory, "23001", """UPDATE ledger.journal_entries SET "Label" = 'modifié' WHERE "Id" = @entry""", entry);
        await AssertSqlRefusedAsync(factory, "23001", """DELETE FROM ledger.journal_entries WHERE "Id" = @entry""", entry);
        await AssertSqlRefusedAsync(factory, "23001", """UPDATE ledger.journal_lines SET "Amount" = 1 WHERE "EntryId" = @entry""", entry);
        await AssertSqlRefusedAsync(factory, "23001", """DELETE FROM ledger.journal_lines WHERE "EntryId" = @entry""", entry);
        await AssertSqlRefusedAsync(factory, "23001", "TRUNCATE ledger.journal_lines CASCADE", entry);

        // Rien n'a bougé.
        Assert.Equal(Mro(100m), (await BalancesAsync(factory, [cash]))[cash]);
    }

    [Fact]
    public async Task Database_refuses_an_unbalanced_entry_at_commit()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);

        var error = await InsertRawEntryAsync(factory, [(cash, "Debit", 100m), (bank, "Credit", 90m)]);

        Assert.Equal("23514", error?.SqlState);
        Assert.Contains("déséquilibrée", error!.MessageText, StringComparison.Ordinal);
        Assert.Equal(Mro(0m), (await BalancesAsync(factory, [cash]))[cash]);
    }

    [Fact]
    public async Task Database_refuses_an_entry_without_lines_at_commit()
    {
        var factory = await fixture.GetFactoryAsync();

        var error = await InsertRawEntryAsync(factory, []);

        Assert.Equal("23514", error?.SqlState);
        Assert.Contains("au moins deux lignes", error!.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_accepts_a_balanced_entry_inserted_by_hand()
    {
        // Contre-épreuve : les déclencheurs ne refusent pas tout, seulement ce qui est faux.
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);

        var error = await InsertRawEntryAsync(factory, [(cash, "Debit", 100m), (bank, "Credit", 100m)]);

        Assert.Null(error);
        Assert.Equal(Mro(100m), (await BalancesAsync(factory, [cash]))[cash]);
    }

    [Fact]
    public async Task Database_refuses_an_unknown_side()
    {
        var factory = await fixture.GetFactoryAsync();
        var agency = Guid.NewGuid();
        var cash = await OpenAsync(factory, LedgerAccountKind.Treasury, "Caisse", Currency.MRO, agency);
        var bank = await OpenAsync(factory, LedgerAccountKind.Treasury, "Banque", Currency.MRO, agency);

        var error = await InsertRawEntryAsync(factory, [(cash, "debit", 100m), (bank, "Credit", 100m)]);

        Assert.Equal("ck_journal_lines_side", error?.ConstraintName);
    }

    // --- Outils ------------------------------------------------------------

    private static Money Mro(decimal amount) => Money.Create(amount, Currency.MRO).Value;

    private static Money Xof(decimal amount) => Money.Create(amount, Currency.XOF).Value;

    private static PostEntryLine Line(Guid account, EntrySide side, Money amount) => new(account, side, amount);

    /// <summary>Chaque appel dans son propre scope, comme une requête HTTP distincte.</summary>
    private static async Task<T> WithLedgerAsync<T>(HawalaApiFactory factory, Func<ILedger, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILedger>());
    }

    private static async Task<Guid> OpenAsync(
        HawalaApiFactory factory, LedgerAccountKind kind, string name, Currency currency, Guid agency)
    {
        var result = await WithLedgerAsync(factory, ledger =>
            ledger.OpenAccountAsync(kind, Guid.NewGuid(), name, currency, agency, CancellationToken.None));
        return result.Value;
    }

    private static async Task<Guid> SystemAsync(HawalaApiFactory factory, SystemAccountRole role, Guid agency, Currency currency)
    {
        var result = await WithLedgerAsync(factory, ledger =>
            ledger.GetOrCreateSystemAccountAsync(role, agency, currency, CancellationToken.None));
        return result.Value;
    }

    private static Task<Result<Guid>> PostAsync(HawalaApiFactory factory, DateOnly date, params PostEntryLine[] lines) =>
        WithLedgerAsync(factory, ledger => ledger.PostAsync(
            new PostEntryRequest(date, "Tests.Entry", "Tests", null, Author, "Écriture de test", lines),
            CancellationToken.None));

    private static async Task<Guid> PostOkAsync(HawalaApiFactory factory, DateOnly date, params PostEntryLine[] lines)
    {
        var result = await PostAsync(factory, date, lines);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    private static Task<Result<Guid>> ReverseAsync(HawalaApiFactory factory, Guid entry, string reason) =>
        WithLedgerAsync(factory, ledger => ledger.ReverseAsync(entry, Today, Author, reason, CancellationToken.None));

    private static async Task<Dictionary<Guid, Money>> BalancesAsync(
        HawalaApiFactory factory, Guid[] accounts, DateOnly? asOf = null)
    {
        var result = await WithLedgerAsync(factory, ledger =>
            ledger.GetBalancesAsync(accounts, asOf, CancellationToken.None));
        return result.Value.ToDictionary(b => b.AccountId, b => b.Balance);
    }

    private static async Task AssertSqlRefusedAsync(HawalaApiFactory factory, string sqlState, string sql, Guid entry)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("entry", entry);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal(sqlState, error.SqlState);
    }

    /// <summary>Insère une écriture et ses lignes en SQL, dans une transaction ; renvoie l'erreur PostgreSQL ou null.</summary>
    private static async Task<PostgresException?> InsertRawEntryAsync(
        HawalaApiFactory factory, (Guid Account, string Side, decimal Amount)[] lines)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var entryId = Guid.NewGuid();

        try
        {
            await using (var insertEntry = new NpgsqlCommand(
                """
                INSERT INTO ledger.journal_entries
                    ("Id", "OperationDate", "RecordedAt", "EntryType", "SourceModule", "AuthorId", "Label")
                VALUES (@id, @date, now(), 'Tests.Raw', 'Tests', @author, 'Insertion SQL directe')
                """,
                connection,
                transaction))
            {
                insertEntry.Parameters.AddWithValue("id", entryId);
                insertEntry.Parameters.AddWithValue("date", Today);
                insertEntry.Parameters.AddWithValue("author", Author);
                await insertEntry.ExecuteNonQueryAsync();
            }

            foreach (var (account, side, amount) in lines)
            {
                await using var insertLine = new NpgsqlCommand(
                    """
                    INSERT INTO ledger.journal_lines ("Id", "AccountId", "Side", "Amount", "Currency", "EntryId")
                    VALUES (@id, @account, @side, @amount, 'MRO', @entry)
                    """,
                    connection,
                    transaction);
                insertLine.Parameters.AddWithValue("id", Guid.NewGuid());
                insertLine.Parameters.AddWithValue("account", account);
                insertLine.Parameters.AddWithValue("side", side);
                insertLine.Parameters.AddWithValue("amount", amount);
                insertLine.Parameters.AddWithValue("entry", entryId);
                await insertLine.ExecuteNonQueryAsync();
            }

            // Les contrôles différés (équilibre, deux lignes) se déclenchent ici, au COMMIT.
            await transaction.CommitAsync();
            return null;
        }
        catch (PostgresException error)
        {
            return error;
        }
    }
}
