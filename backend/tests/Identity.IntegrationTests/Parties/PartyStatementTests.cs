using System.Globalization;
using System.Net;
using System.Text.Json;
using Hawala.SharedKernel.Money;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parties.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Relevé d'un tiers dans une agence. Jeu de données : solde initial 1 000 MRO au 30 septembre, puis en octobre
/// +500 (il nous doit davantage) le 5 et −300 (il nous rembourse) le 10, à Nouakchott.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PartyStatementTests(ApiFixture fixture)
{
    [Fact]
    public async Task Statement_of_a_period_shows_the_opening_balance_each_line_and_the_closing_balance()
    {
        var (_, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        var statement = await StatementOkAsync(client, token, partyId, $"agencyId={nouakchott}&from=2026-10-01&to=2026-10-31");

        Assert.Equal("MRO", statement.GetProperty("currency").GetString());
        Assert.Equal("2026-10-01", statement.GetProperty("from").GetString());
        Assert.Equal(1_000m, statement.GetProperty("openingBalance").GetDecimal());

        var lines = statement.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(500m, lines[0].GetProperty("debit").GetDecimal());
        Assert.Equal(1_500m, lines[0].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(300m, lines[1].GetProperty("credit").GetDecimal());
        Assert.Equal(1_200m, lines[1].GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(1_200m, statement.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Opening_balance_entry_appears_in_the_statement_of_its_day()
    {
        var (_, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        var september = await StatementOkAsync(client, token, partyId, $"agencyId={nouakchott}&from=2026-09-01&to=2026-09-30");

        Assert.Equal(0m, september.GetProperty("openingBalance").GetDecimal());
        var line = Assert.Single(september.GetProperty("lines").EnumerateArray());
        Assert.StartsWith("Solde initial", line.GetProperty("label").GetString(), StringComparison.Ordinal);
        Assert.Equal("Parties.OpeningBalance", line.GetProperty("entryType").GetString());
        Assert.Equal(1_000m, september.GetProperty("closingBalance").GetDecimal());
    }

    [Fact]
    public async Task Without_dates_the_statement_covers_the_current_month()
    {
        var (factory, client, token, partyId, nouakchott, _) = await ArrangeAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await PostAsync(factory, partyId, nouakchott, today, EntrySide.Debit, 42m);

        var statement = await StatementOkAsync(client, token, partyId, $"agencyId={nouakchott}");

        Assert.Equal(new DateOnly(today.Year, today.Month, 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), statement.GetProperty("from").GetString());
        Assert.Equal(today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), statement.GetProperty("to").GetString());
        Assert.Contains(
            statement.GetProperty("lines").EnumerateArray(),
            l => l.GetProperty("debit").GetDecimal() == 42m);
    }

    [Fact]
    public async Task Agent_reads_the_statement_of_its_own_agency_only()
    {
        var (factory, client, _, partyId, nouakchott, dakar) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        // Sans agence choisie : la sienne.
        var own = await StatementOkAsync(client, agentToken, partyId, "from=2026-10-01&to=2026-10-31");
        using var other = await client.GetPartyStatementAsync(agentToken, partyId, $"agencyId={dakar}");

        Assert.Equal(nouakchott, own.GetProperty("agencyId").GetGuid());
        Assert.Equal(1_200m, own.GetProperty("closingBalance").GetDecimal());
        Assert.Equal("Parties.Party.AgencyAccountNotFound", await other.ReadErrorCodeAsync());
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Cashier_without_view_balance_cannot_read_a_statement()
    {
        var (factory, client, _, partyId, nouakchott, _) = await ArrangeAsync();
        var cashier = await TestAccounts.CreateAsync(factory, RoleTemplate.Cashier, agencyCode: "NKC");
        var (cashierToken, _) = await client.LoginOkAsync(cashier.Email, cashier.Password);

        using var response = await client.GetPartyStatementAsync(cashierToken, partyId, $"agencyId={nouakchott}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Parties.Statement.NotVisible", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Supervisor_must_choose_an_agency_and_bad_requests_are_refused()
    {
        var (_, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        using var noAgency = await client.GetPartyStatementAsync(token, partyId);
        using var inverted = await client.GetPartyStatementAsync(token, partyId, $"agencyId={nouakchott}&from=2026-10-31&to=2026-10-01");
        using var unknownParty = await client.GetPartyStatementAsync(token, Guid.NewGuid(), $"agencyId={nouakchott}");
        using var unknownAgency = await client.GetPartyStatementAsync(token, partyId, $"agencyId={Guid.NewGuid()}");

        Assert.Equal("Parties.Statement.AgencyRequired", await noAgency.ReadErrorCodeAsync());
        Assert.Equal("Ledger.Statement.PeriodInvalid", await inverted.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NotFound", await unknownParty.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.AgencyAccountNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token, Guid PartyId, Guid Nouakchott, Guid Dakar)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var created = await client.CreatePartyAsync(token, new { name = "Relevé", type = "Client" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var partyId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        using var opening = await client.SetOpeningBalanceAsync(
            token, partyId, nouakchott, new { amount = 1_000m, operationDate = "2026-09-30" });
        Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);
        await PostAsync(factory, partyId, nouakchott, new DateOnly(2026, 10, 5), EntrySide.Debit, 500m);
        await PostAsync(factory, partyId, nouakchott, new DateOnly(2026, 10, 10), EntrySide.Credit, 300m);

        return (factory, client, token, partyId, nouakchott, dakar);
    }

    private static async Task<JsonElement> StatementOkAsync(HttpClient client, string token, Guid partyId, string queryString)
    {
        using var response = await client.GetPartyStatementAsync(token, partyId, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    /// <summary>Écriture directe au grand livre sur le compte du tiers (contrepartie : Écarts et ajustements).</summary>
    private static async Task PostAsync(HawalaApiFactory factory, Guid partyId, Guid agencyId, DateOnly date, EntrySide partySide, decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
            .Parties.AsNoTracking().SingleAsync(p => p.Id == partyId);
        var partyAccount = party.AgencyAccounts.Single(a => a.AgencyId == agencyId).LedgerAccountId;
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        var counterpart = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.Adjustments, agencyId, Currency.MRO, CancellationToken.None);
        var money = Money.Create(amount, Currency.MRO).Value;

        var posted = await ledger.PostAsync(
            new PostEntryRequest(date, "Tests.PartyMovement", "Tests", null, Guid.NewGuid(), "Mouvement de test",
            [
                new PostEntryLine(partyAccount, partySide, money),
                new PostEntryLine(counterpart.Value, partySide == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit, money),
            ]),
            CancellationToken.None);
        Assert.True(posted.IsSuccess);
    }
}
