using System.Net;
using System.Text.Json;
using Hawala.SharedKernel.Money;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Treasury;

/// <summary>
/// Lecture des comptes de trésorerie : périmètre par agence (un agent ne voit que la sienne, le Superviseur
/// les deux) et soldes montrés seulement avec cash.budget.read. La base est partagée : chaque test cherche
/// ses propres comptes par id, sans compter la liste entière.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ListTreasuryAccountsTests(ApiFixture fixture)
{
    // --- Périmètre par agence ----------------------------------------------

    [Fact]
    public async Task Agent_sees_only_the_accounts_of_its_agency_and_no_balance()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var ownId = await CreateAsync(client, supervisorToken, "Caisse", "Cash", nouakchott);
        var otherId = await CreateAsync(client, supervisorToken, "Caisse", "Cash", dakar);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        var accounts = await ListAsync(client, agentToken);

        Assert.Contains(accounts, a => a.GetProperty("id").GetGuid() == ownId);
        Assert.DoesNotContain(accounts, a => a.GetProperty("id").GetGuid() == otherId);
        Assert.All(accounts, a => Assert.Equal(nouakchott, a.GetProperty("agencyId").GetGuid()));

        // Règle validée : un agent voit les comptes mais pas leurs soldes (pas de cash.budget.read).
        Assert.All(accounts, a => Assert.Equal(JsonValueKind.Null, a.GetProperty("balance").ValueKind));
    }

    [Fact]
    public async Task Agent_asking_for_the_other_agency_gets_nothing()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var otherId = await CreateAsync(client, supervisorToken, "Wave", "Wallet", dakar);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        // Le filtre de l'appelant ne contourne pas le périmètre : liste vide, et 404 sur la fiche.
        var list = await ListAsync(client, agentToken, $"agencyId={dakar}");
        using var read = await client.GetTreasuryAccountAsync(agentToken, otherId);

        Assert.Empty(list);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal("Treasury.Account.NotFound", await read.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_reads_an_account_of_its_own_agency_without_balance()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var id = await CreateAsync(client, supervisorToken, "Banque", "Bank", dakar);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.GetTreasuryAccountAsync(agentToken, id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var account = await response.ReadJsonAsync();
        Assert.Equal("Bank", account.GetProperty("type").GetString());
        Assert.Equal("XOF", account.GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Null, account.GetProperty("balance").ValueKind);
    }

    // --- Superviseur et soldes ---------------------------------------------

    [Fact]
    public async Task Supervisor_sees_both_agencies_with_balances_read_from_the_ledger()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var nouakchott = await GetAgencyIdAsync(factory, "NKC");
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var cashNkc = await CreateAsync(client, supervisorToken, "Caisse", "Cash", nouakchott);
        var bankDkr = await CreateAsync(client, supervisorToken, "Banque", "Bank", dakar);

        // 1 500,50 MRO apportés par le patron dans la caisse de Nouakchott.
        await FundAsync(factory, cashNkc, nouakchott, Money.Create(1_500.50m, Currency.MRO).Value);

        var accounts = await ListAsync(client, supervisorToken);
        var cash = accounts.Single(a => a.GetProperty("id").GetGuid() == cashNkc);
        var bank = accounts.Single(a => a.GetProperty("id").GetGuid() == bankDkr);

        Assert.Equal(1_500.50m, cash.GetProperty("balance").GetDecimal());
        Assert.Equal("MRO", cash.GetProperty("currency").GetString());
        Assert.Equal(0m, bank.GetProperty("balance").GetDecimal());
        Assert.Equal("XOF", bank.GetProperty("currency").GetString());

        using var read = await client.GetTreasuryAccountAsync(supervisorToken, cashNkc);
        Assert.Equal(1_500.50m, (await read.ReadJsonAsync()).GetProperty("balance").GetDecimal());
    }

    // --- Filtres -----------------------------------------------------------

    [Fact]
    public async Task Type_and_agency_filters_apply()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var dakar = await GetAgencyIdAsync(factory, "DKR");
        var wallet = await CreateAsync(client, supervisorToken, "Orange Money", "Wallet", dakar);
        var cash = await CreateAsync(client, supervisorToken, "Caisse", "Cash", dakar);

        var wallets = await ListAsync(client, supervisorToken, $"agencyId={dakar}&type=wallet");

        Assert.Contains(wallets, a => a.GetProperty("id").GetGuid() == wallet);
        Assert.DoesNotContain(wallets, a => a.GetProperty("id").GetGuid() == cash);
        Assert.All(wallets, a =>
        {
            Assert.Equal("Wallet", a.GetProperty("type").GetString());
            Assert.Equal(dakar, a.GetProperty("agencyId").GetGuid());
        });
    }

    [Fact]
    public async Task Inactive_accounts_are_hidden_unless_asked()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, supervisorToken, "Ancienne caisse", "Cash", await GetAgencyIdAsync(factory, "NKC"));
        await DeactivateDirectlyAsync(factory, id);

        var byDefault = await ListAsync(client, supervisorToken);
        var withInactive = await ListAsync(client, supervisorToken, "includeInactive=true");

        Assert.DoesNotContain(byDefault, a => a.GetProperty("id").GetGuid() == id);
        var inactive = Assert.Single(withInactive, a => a.GetProperty("id").GetGuid() == id);
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Unknown_type_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.ListTreasuryAccountsAsync(supervisorToken, "type=Coffre");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Treasury.Account.TypeInvalid", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Unknown_account_is_not_found_and_anonymous_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var unknown = await client.GetTreasuryAccountAsync(supervisorToken, Guid.NewGuid());
        using var anonymous = await client.ListTreasuryAccountsAsync(accessToken: null);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<Guid> CreateAsync(HttpClient client, string token, string prefix, string type, Guid agencyId)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 50)], type, agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> ListAsync(HttpClient client, string token, string? queryString = null)
    {
        using var response = await client.ListTreasuryAccountsAsync(token, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.ReadJsonAsync()).EnumerateArray()];
    }

    /// <summary>Approvisionnement de caisse écrit directement au grand livre (le module Caisse viendra en phase 4).</summary>
    private static async Task FundAsync(HawalaApiFactory factory, Guid treasuryAccountId, Guid agencyId, Money amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>()
            .Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == treasuryAccountId);
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        var owner = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OwnerContributions, agencyId, amount.Currency, CancellationToken.None);

        var posted = await ledger.PostAsync(
            new PostEntryRequest(
                new DateOnly(2026, 10, 19), "Tests.Funding", "Tests", null, Guid.NewGuid(), "Approvisionnement de test",
                [
                    new PostEntryLine(account.LedgerAccountId, EntrySide.Debit, amount),
                    new PostEntryLine(owner.Value, EntrySide.Credit, amount),
                ]),
            CancellationToken.None);
        Assert.True(posted.IsSuccess);
    }

    /// <summary>Désactive directement en base (la fonctionnalité « désactiver » viendra au tour suivant).</summary>
    private static async Task DeactivateDirectlyAsync(HawalaApiFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TreasuryDbContext>();
        var account = await db.Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        Assert.True(account.Deactivate(Money.Zero(account.Currency)).IsSuccess);
        await db.SaveChangesAsync();
    }

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }

    private static async Task<Guid> GetAgencyIdAsync(HawalaApiFactory factory, string code)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, code);
    }
}
