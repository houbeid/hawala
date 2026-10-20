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
/// Liste et fiche des tiers, comme l'écran « Tiers » de l'app actuelle : soldes par agence (avec parties.view_balance),
/// totaux rouge / vert / net par devise, filtres. La base est partagée : chaque test met un mot unique dans le nom
/// de ses tiers et filtre dessus.
/// Jeu de données de chaque test : A rouge de 1 000,50 MRO à Nouakchott ; B vert de 5 000 FCFA à Dakar ; C à zéro.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListPartiesTests(ApiFixture fixture)
{
    // --- Superviseur : soldes et totaux ------------------------------------

    [Fact]
    public async Task Supervisor_sees_the_balance_of_each_agency_and_totals_per_currency()
    {
        var (factory, client, token, data) = await ArrangeAsync();

        var list = await ListAsync(client, token, $"search={data.Tag}");

        Assert.Equal(3, list.GetProperty("count").GetInt32());
        Assert.True(list.GetProperty("totalCount").GetInt32() >= 3);

        var a = Item(list, data.A);
        Assert.Equal(1_000.50m, BalanceOf(a, data.Nouakchott));
        Assert.Equal(0m, BalanceOf(a, data.Dakar));
        Assert.Equal("41", a.GetProperty("accountCode").GetString());
        Assert.Equal(-5_000m, BalanceOf(Item(list, data.B), data.Dakar));

        // Jamais d'addition MRO + FCFA : un total par devise.
        var mro = Totals(list, "MRO");
        Assert.Equal(1_000.50m, mro.GetProperty("red").GetDecimal());
        Assert.Equal(0m, mro.GetProperty("green").GetDecimal());
        Assert.Equal(1_000.50m, mro.GetProperty("net").GetDecimal());

        var xof = Totals(list, "XOF");
        Assert.Equal(0m, xof.GetProperty("red").GetDecimal());
        Assert.Equal(-5_000m, xof.GetProperty("green").GetDecimal());
        Assert.Equal(-5_000m, xof.GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task Choosing_an_agency_shows_only_its_balances()
    {
        var (_, client, token, data) = await ArrangeAsync();

        var list = await ListAsync(client, token, $"search={data.Tag}&agencyId={data.Nouakchott}");

        var balances = Item(list, data.A).GetProperty("balances").EnumerateArray().ToList();
        Assert.Equal(data.Nouakchott, Assert.Single(balances).GetProperty("agencyId").GetGuid());
        Assert.Single(list.GetProperty("totals").EnumerateArray());
        Assert.Equal("MRO", Totals(list, "MRO").GetProperty("currency").GetString());
    }

    // --- Filtre « solde » --------------------------------------------------

    [Fact]
    public async Task Balance_filter_applies_to_the_chosen_agency()
    {
        var (_, client, token, data) = await ArrangeAsync();

        var red = await ListAsync(client, token, $"search={data.Tag}&agencyId={data.Nouakchott}&balance=red");
        var green = await ListAsync(client, token, $"search={data.Tag}&agencyId={data.Dakar}&balance=Green");
        var noRedInDakar = await ListAsync(client, token, $"search={data.Tag}&agencyId={data.Dakar}&balance=red");

        // « عليه » (rouge) : A seulement ; « له » (vert) : B seulement ; C (zéro) n'est ni l'un ni l'autre.
        Assert.Equal([data.A], Ids(red));
        Assert.Equal([data.B], Ids(green));
        Assert.Empty(Ids(noRedInDakar));

        // Les totaux suivent les filtres : seulement A, rouge.
        Assert.Equal(1_000.50m, Totals(red, "MRO").GetProperty("net").GetDecimal());
    }

    [Fact]
    public async Task Supervisor_must_choose_an_agency_to_filter_by_balance()
    {
        var (_, client, token, data) = await ArrangeAsync();

        using var response = await client.ListPartiesAsync(token, $"search={data.Tag}&balance=red");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Parties.List.AgencyRequiredForBalanceFilter", await response.ReadErrorCodeAsync());
    }

    // --- Agent : soldes de son agence seulement ----------------------------

    [Fact]
    public async Task Agent_sees_only_the_balances_of_its_agency_and_filters_on_them()
    {
        var (factory, client, _, data) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        var list = await ListAsync(client, agentToken, $"search={data.Tag}");
        var red = await ListAsync(client, agentToken, $"search={data.Tag}&balance=red");

        // Droits par défaut (20 octobre 2026) : l'agent voit les soldes, mais seulement ceux de Nouakchott.
        Assert.All(
            list.GetProperty("items").EnumerateArray(),
            p => Assert.Equal(data.Nouakchott, Assert.Single(p.GetProperty("balances").EnumerateArray()).GetProperty("agencyId").GetGuid()));
        Assert.Equal(1_000.50m, BalanceOf(Item(list, data.A), data.Nouakchott));
        Assert.Equal("MRO", Assert.Single(list.GetProperty("totals").EnumerateArray()).GetProperty("currency").GetString());

        // Filtre « solde » sans choisir d'agence : c'est la sienne.
        Assert.Equal([data.A], Ids(red));
    }

    // --- Caissier : fiches sans soldes -------------------------------------

    [Fact]
    public async Task Cashier_sees_the_parties_but_no_balance_and_cannot_filter_by_balance()
    {
        var (factory, client, _, data) = await ArrangeAsync();
        var cashier = await TestAccounts.CreateAsync(factory, RoleTemplate.Cashier, agencyCode: "NKC");
        var (cashierToken, _) = await client.LoginOkAsync(cashier.Email, cashier.Password);

        var list = await ListAsync(client, cashierToken, $"search={data.Tag}");
        using var filtered = await client.ListPartiesAsync(cashierToken, $"search={data.Tag}&balance=red");

        Assert.Equal(3, list.GetProperty("count").GetInt32());
        Assert.All(
            list.GetProperty("items").EnumerateArray(),
            p => Assert.Equal(JsonValueKind.Null, p.GetProperty("balances").ValueKind));
        Assert.Equal(JsonValueKind.Null, list.GetProperty("totals").ValueKind);

        // Filtrer par solde révélerait qui est rouge : refusé.
        Assert.Equal(HttpStatusCode.Forbidden, filtered.StatusCode);
        Assert.Equal("Parties.List.BalanceNotVisible", await filtered.ReadErrorCodeAsync());
    }

    // --- Autres filtres ----------------------------------------------------

    [Fact]
    public async Task Type_search_by_phone_and_balance_sheet_filters_apply()
    {
        var (_, client, token, data) = await ArrangeAsync();

        var suppliers = await ListAsync(client, token, $"search={data.Tag}&type=supplier");
        var byPhone = await ListAsync(client, token, $"search={Uri.EscapeDataString(data.SpacedPhoneOfC)}");
        var inSheet = await ListAsync(client, token, $"search={data.Tag}&balanceSheet=in");
        var outSheet = await ListAsync(client, token, $"search={data.Tag}&balanceSheet=out");

        Assert.Equal([data.B], Ids(suppliers));
        Assert.Contains(data.C, Ids(byPhone));
        Assert.Equal(3, inSheet.GetProperty("count").GetInt32());
        Assert.Equal(0, outSheet.GetProperty("count").GetInt32());
    }

    [Theory]
    [InlineData("type=Banque", "Parties.Party.TypeInvalid")]
    [InlineData("balanceSheet=peutetre", "Parties.List.BalanceSheetFilterInvalid")]
    [InlineData("balance=orange", "Parties.List.BalanceFilterInvalid")]
    [InlineData("balance=nonzero", "Parties.List.BalanceFilterInvalid")]
    public async Task Unknown_filter_value_is_refused(string queryString, string expectedCode)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ListPartiesAsync(token, queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await response.ReadErrorCodeAsync());
    }

    // --- Fiche -------------------------------------------------------------

    [Fact]
    public async Task Party_card_shows_both_agencies_to_the_supervisor_one_to_an_agent_none_to_a_cashier()
    {
        var (factory, client, token, data) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var cashier = await TestAccounts.CreateAsync(factory, RoleTemplate.Cashier, agencyCode: "DKR");
        var (cashierToken, _) = await client.LoginOkAsync(cashier.Email, cashier.Password);

        using var supervisorRead = await client.GetPartyAsync(token, data.A);
        using var agentRead = await client.GetPartyAsync(agentToken, data.A);
        using var cashierRead = await client.GetPartyAsync(cashierToken, data.A);
        using var unknown = await client.GetPartyAsync(token, Guid.NewGuid());

        var forSupervisor = await supervisorRead.ReadJsonAsync();
        Assert.Equal(1_000.50m, BalanceOf(forSupervisor, data.Nouakchott));
        Assert.Equal(2, forSupervisor.GetProperty("balances").GetArrayLength());

        // Agent de Dakar : seulement le solde de Dakar (0 pour A).
        var forAgent = await agentRead.ReadJsonAsync();
        Assert.Equal(data.Dakar, Assert.Single(forAgent.GetProperty("balances").EnumerateArray()).GetProperty("agencyId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, cashierRead.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await cashierRead.ReadJsonAsync()).GetProperty("balances").ValueKind);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("Parties.Party.NotFound", await unknown.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Anonymous_cannot_list_parties()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.ListPartiesAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private sealed record Data(string Tag, Guid A, Guid B, Guid C, string SpacedPhoneOfC, Guid Nouakchott, Guid Dakar);

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token, Data Data)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var tag = Guid.NewGuid().ToString("N")[..10];
        var localNumber = "70" + Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);

        var a = await CreateAsync(client, token, $"A {tag}", "Client", phone: null);
        var b = await CreateAsync(client, token, $"B {tag}", "Supplier", phone: null);
        var c = await CreateAsync(client, token, $"C {tag}", "Staff", phone: $"+221{localNumber}");

        await using var scope = factory.Services.CreateAsyncScope();
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        // A nous doit 1 000,50 MRO à Nouakchott (rouge) ; on doit 5 000 FCFA à B à Dakar (vert).
        await PostAsync(factory, a, nouakchott, EntrySide.Debit, Money.Create(1_000.50m, Currency.MRO).Value);
        await PostAsync(factory, b, dakar, EntrySide.Credit, Money.Create(5_000m, Currency.XOF).Value);

        var spaced = $"{localNumber[..2]} {localNumber[2..5]} {localNumber[5..]}";
        return (factory, client, token, new Data(tag, a, b, c, spaced, nouakchott, dakar));
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string token, string name, string type, string? phone)
    {
        using var response = await client.CreatePartyAsync(token, new { name, type, phone });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Écriture directe au grand livre sur le compte du tiers dans une agence (contrepartie : Écarts et ajustements).</summary>
    private static async Task PostAsync(HawalaApiFactory factory, Guid partyId, Guid agencyId, EntrySide partySide, Money amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
            .Parties.AsNoTracking().SingleAsync(p => p.Id == partyId);
        var partyAccount = party.AgencyAccounts.Single(a => a.AgencyId == agencyId).LedgerAccountId;
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        var counterpart = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.Adjustments, agencyId, amount.Currency, CancellationToken.None);
        var otherSide = partySide == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit;

        var posted = await ledger.PostAsync(
            new PostEntryRequest(
                new DateOnly(2026, 10, 19), "Tests.PartyBalance", "Tests", null, Guid.NewGuid(), "Solde de test",
                [
                    new PostEntryLine(partyAccount, partySide, amount),
                    new PostEntryLine(counterpart.Value, otherSide, amount),
                ]),
            CancellationToken.None);
        Assert.True(posted.IsSuccess);
    }

    private static async Task<JsonElement> ListAsync(HttpClient client, string token, string queryString)
    {
        using var response = await client.ListPartiesAsync(token, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static JsonElement Item(JsonElement list, Guid id) =>
        list.GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);

    private static List<Guid> Ids(JsonElement list) =>
        [.. list.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];

    private static decimal BalanceOf(JsonElement party, Guid agencyId) =>
        party.GetProperty("balances").EnumerateArray()
            .Single(b => b.GetProperty("agencyId").GetGuid() == agencyId)
            .GetProperty("balance").GetDecimal();

    private static JsonElement Totals(JsonElement list, string currency) =>
        list.GetProperty("totals").EnumerateArray().Single(t => t.GetProperty("currency").GetString() == currency);

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }
}
