using System.Net;
using Hawala.SharedKernel.Money;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Parties.Application.Features.Parties.SetOpeningBalance;
using Parties.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Solde initial d'un tiers par agence : écriture d'ouverture DE LA DIFFÉRENCE contre « Soldes d'ouverture ».
/// Chaque test crée son propre tiers ; il ne lit que les soldes et les écritures de ce tiers.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SetOpeningBalanceTests(ApiFixture fixture)
{
    [Fact]
    public async Task Changing_the_opening_balance_posts_only_the_difference()
    {
        var (factory, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        using var first = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 10_000m });
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(10_000m, await BalanceAsync(factory, partyId, nouakchott));

        using var second = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 4_000.25m });
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(4_000.25m, await BalanceAsync(factory, partyId, nouakchott));

        // Même valeur : aucune nouvelle écriture.
        using var same = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 4_000.25m });
        Assert.Equal(HttpStatusCode.NoContent, same.StatusCode);

        var amounts = await OpeningEntryAmountsAsync(factory, partyId);
        Assert.Equal([10_000m, -5_999.75m], amounts);
    }

    [Fact]
    public async Task Negative_opening_balance_makes_the_party_green_in_that_agency_only()
    {
        var (factory, client, token, partyId, nouakchott, dakar) = await ArrangeAsync();

        using var response = await client.SetOpeningBalanceAsync(token, partyId, dakar, new { amount = -2_500m });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(-2_500m, await BalanceAsync(factory, partyId, dakar));
        Assert.Equal(0m, await BalanceAsync(factory, partyId, nouakchott));

        // La liste le montre en vert (« له ») à Dakar.
        using var read = await client.GetPartyAsync(token, partyId, $"agencyId={dakar}");
        var balance = (await read.ReadJsonAsync()).GetProperty("balances").EnumerateArray().Single();
        Assert.Equal(-2_500m, balance.GetProperty("balance").GetDecimal());
        Assert.Equal("XOF", balance.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Opening_entry_is_dated_on_the_chosen_day()
    {
        var (factory, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        using var response = await client.SetOpeningBalanceAsync(
            token, partyId, nouakchott, new { amount = 750m, operationDate = "2026-09-30" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(750m, await BalanceAsync(factory, partyId, nouakchott, asOf: new DateOnly(2026, 9, 30)));
        Assert.Equal(0m, await BalanceAsync(factory, partyId, nouakchott, asOf: new DateOnly(2026, 9, 29)));
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Amount_must_follow_the_currency_of_the_agency()
    {
        var (factory, client, token, partyId, nouakchott, dakar) = await ArrangeAsync();

        using var xofWithDecimals = await client.SetOpeningBalanceAsync(token, partyId, dakar, new { amount = 2_500.50m });
        using var mroTooPrecise = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 10.123m });
        using var missing = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { });

        Assert.Equal("Money.Amount.XofNotInteger", await xofWithDecimals.ReadErrorCodeAsync());
        Assert.Equal("Money.Amount.TooManyDecimals", await mroTooPrecise.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.AmountRequired", await missing.ReadErrorCodeAsync());
        Assert.Empty(await OpeningEntryAmountsAsync(factory, partyId));
    }

    [Fact]
    public async Task Unknown_party_or_agency_is_not_found()
    {
        var (_, client, token, partyId, nouakchott, _) = await ArrangeAsync();

        using var unknownParty = await client.SetOpeningBalanceAsync(token, Guid.NewGuid(), nouakchott, new { amount = 1m });
        using var unknownAgency = await client.SetOpeningBalanceAsync(token, partyId, Guid.NewGuid(), new { amount = 1m });

        Assert.Equal("Parties.Party.NotFound", await unknownParty.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.AgencyAccountNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task The_party_card_gives_the_current_opening_balance_apart_from_the_balance()
    {
        var (factory, client, token, partyId, nouakchott, _) = await ArrangeAsync();
        using var opened = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 10_000m });
        Assert.Equal(HttpStatusCode.NoContent, opened.StatusCode);

        // Une autre écriture sur le tiers (ajustement) change le solde, pas le solde initial.
        using var adjusted = await client.CreateBalanceAdjustmentAsync(
            token, new { agencyId = nouakchott, target = "Party", targetId = partyId, amount = 2_500m, reason = "Test" });
        Assert.True(adjusted.IsSuccessStatusCode, await adjusted.Content.ReadAsStringAsync());

        using var read = await client.GetPartyAsync(token, partyId, $"agencyId={nouakchott}");
        var balance = (await read.ReadJsonAsync()).GetProperty("balances").EnumerateArray().Single();
        Assert.Equal(12_500m, balance.GetProperty("balance").GetDecimal());
        Assert.Equal(10_000m, balance.GetProperty("openingBalance").GetDecimal());
    }

    [Fact]
    public async Task Agent_cannot_set_an_opening_balance()
    {
        var (factory, client, _, partyId, nouakchott, _) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.SetOpeningBalanceAsync(agentToken, partyId, nouakchott, new { amount = 1_000m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0m, await BalanceAsync(factory, partyId, nouakchott));
    }

    // --- Outils ------------------------------------------------------------

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token, Guid PartyId, Guid Nouakchott, Guid Dakar)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var created = await client.CreatePartyAsync(token, new { name = "Solde initial", type = "Client" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var partyId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return (factory, client, token, partyId, nouakchott, dakar);
    }

    private static async Task<decimal> BalanceAsync(HawalaApiFactory factory, Guid partyId, Guid agencyId, DateOnly? asOf = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
            .Parties.AsNoTracking().SingleAsync(p => p.Id == partyId);
        var ledgerAccount = party.AgencyAccounts.Single(a => a.AgencyId == agencyId).LedgerAccountId;

        var balances = await scope.ServiceProvider.GetRequiredService<ILedger>()
            .GetBalancesAsync([ledgerAccount], asOf, CancellationToken.None);
        return balances.Value.Single().Balance.Amount;
    }

    /// <summary>Montants signés (débit − crédit) que les écritures d'ouverture ont portés sur le compte du tiers, dans l'ordre.</summary>
    private static async Task<List<decimal>> OpeningEntryAmountsAsync(HawalaApiFactory factory, Guid partyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var ledgerDb = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var partyAccounts = await ledgerDb.Accounts.AsNoTracking()
            .Where(a => a.OwnerId == partyId).Select(a => a.Id).ToListAsync();

        var entries = await ledgerDb.JournalEntries.AsNoTracking()
            .Include(e => e.Lines)
            .Where(e => e.SourceId == partyId && e.EntryType == SetOpeningBalanceCommandHandler.EntryType)
            .OrderBy(e => e.RecordedAt)
            .ToListAsync();

        return [.. entries.Select(e => e.Lines.Where(l => partyAccounts.Contains(l.AccountId)).Sum(l => l.SignedAmount))];
    }
}
