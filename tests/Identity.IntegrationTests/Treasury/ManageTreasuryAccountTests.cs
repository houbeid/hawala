using System.Net;
using Hawala.SharedKernel.Money;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Domain;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Treasury;

/// <summary>
/// Renommer, désactiver (solde nul seulement) et réactiver un compte de trésorerie, avec l'effet sur son compte
/// au grand livre. La base est partagée : chaque test crée ses propres comptes, avec un nom unique.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ManageTreasuryAccountTests(ApiFixture fixture)
{
    // --- Renommer ----------------------------------------------------------

    [Fact]
    public async Task Supervisor_renames_an_account()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, "Caisse", "NKC", factory);
        var newName = UniqueName("Guichet");

        using var response = await client.RenameTreasuryAccountAsync(token, id, $"  {newName}  ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var read = await client.GetTreasuryAccountAsync(token, id);
        Assert.Equal(newName, (await read.ReadJsonAsync()).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Keeping_its_own_name_with_another_case_is_accepted()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var name = UniqueName("Bankily");
        var id = await CreateAsync(client, token, name, "NKC", factory, exactName: true);

        using var response = await client.RenameTreasuryAccountAsync(token, id, name.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Name_of_another_account_of_the_agency_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var takenName = UniqueName("Sedad");
        await CreateAsync(client, token, takenName, "DKR", factory, exactName: true);
        var id = await CreateAsync(client, token, "Wave", "DKR", factory);

        using var taken = await client.RenameTreasuryAccountAsync(token, id, takenName.ToLowerInvariant());
        using var empty = await client.RenameTreasuryAccountAsync(token, id, " ");

        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("Treasury.Account.NameTaken", await taken.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.NameEmpty", await empty.ReadErrorCodeAsync());
    }

    // --- Désactiver / réactiver --------------------------------------------

    [Fact]
    public async Task Account_with_zero_balance_is_deactivated_and_its_ledger_account_closed_then_both_reactivated()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, "Ancienne caisse", "NKC", factory);

        using var deactivate = await client.DeactivateTreasuryAccountAsync(token, id);

        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        var (treasuryActive, ledgerActive) = await ReadActiveFlagsAsync(factory, id);
        Assert.False(treasuryActive);
        Assert.False(ledgerActive);

        // Le grand livre refuse désormais toute écriture sur ce compte.
        var refused = await FundAsync(factory, id, 100m);
        Assert.Equal(LedgerErrors.Account.Inactive, refused);

        using var reactivate = await client.ReactivateTreasuryAccountAsync(token, id);

        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        (treasuryActive, ledgerActive) = await ReadActiveFlagsAsync(factory, id);
        Assert.True(treasuryActive);
        Assert.True(ledgerActive);
        Assert.Null(await FundAsync(factory, id, 100m));
    }

    [Fact]
    public async Task Account_with_money_left_cannot_be_deactivated()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, "Caisse pleine", "NKC", factory);
        Assert.Null(await FundAsync(factory, id, 2_500m));

        using var response = await client.DeactivateTreasuryAccountAsync(token, id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Treasury.Account.BalanceNotZero", await response.ReadErrorCodeAsync());
        var (treasuryActive, ledgerActive) = await ReadActiveFlagsAsync(factory, id);
        Assert.True(treasuryActive);
        Assert.True(ledgerActive);
    }

    [Fact]
    public async Task Deactivating_twice_or_reactivating_an_active_account_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, "Caisse", "DKR", factory);

        using var reactivateActive = await client.ReactivateTreasuryAccountAsync(token, id);
        using var first = await client.DeactivateTreasuryAccountAsync(token, id);
        using var second = await client.DeactivateTreasuryAccountAsync(token, id);

        Assert.Equal("Treasury.Account.AlreadyActive", await reactivateActive.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal("Treasury.Account.AlreadyInactive", await second.ReadErrorCodeAsync());
    }

    // --- Qui a le droit ----------------------------------------------------

    [Fact]
    public async Task Agent_cannot_rename_deactivate_or_reactivate()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, supervisorToken, "Caisse", "NKC", factory);
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var rename = await client.RenameTreasuryAccountAsync(agentToken, id, UniqueName("Pirate"));
        using var deactivate = await client.DeactivateTreasuryAccountAsync(agentToken, id);
        using var reactivate = await client.ReactivateTreasuryAccountAsync(agentToken, id);

        Assert.Equal(HttpStatusCode.Forbidden, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reactivate.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);

        using var rename = await client.RenameTreasuryAccountAsync(token, Guid.NewGuid(), "Nom");
        using var deactivate = await client.DeactivateTreasuryAccountAsync(token, Guid.NewGuid());
        using var reactivate = await client.ReactivateTreasuryAccountAsync(token, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, rename.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reactivate.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 50)];

    private static async Task<Guid> CreateAsync(
        HttpClient client, string token, string name, string agencyCode, HawalaApiFactory factory, bool exactName = false)
    {
        using var response = await client.CreateTreasuryAccountAsync(token, new
        {
            name = exactName ? name : UniqueName(name),
            type = "Cash",
            agencyId = await GetAgencyIdAsync(factory, agencyCode),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<(bool Treasury, bool Ledger)> ReadActiveFlagsAsync(HawalaApiFactory factory, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>()
            .Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id);
        var ledgerAccount = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.AsNoTracking().SingleAsync(a => a.Id == account.LedgerAccountId);
        return (account.IsActive, ledgerAccount.IsActive);
    }

    /// <summary>Approvisionnement direct au grand livre ; renvoie l'erreur du grand livre, ou null si l'écriture est passée.</summary>
    private static async Task<Hawala.SharedKernel.Results.Error?> FundAsync(HawalaApiFactory factory, Guid id, decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>()
            .Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == id);
        var ledger = scope.ServiceProvider.GetRequiredService<ILedger>();
        var money = Money.Create(amount, account.Currency).Value;
        var owner = await ledger.GetOrCreateSystemAccountAsync(
            SystemAccountRole.OwnerContributions, account.AgencyId, account.Currency, CancellationToken.None);

        var posted = await ledger.PostAsync(
            new PostEntryRequest(
                new DateOnly(2026, 10, 19), "Tests.Funding", "Tests", null, Guid.NewGuid(), "Approvisionnement de test",
                [
                    new PostEntryLine(account.LedgerAccountId, EntrySide.Debit, money),
                    new PostEntryLine(owner.Value, EntrySide.Credit, money),
                ]),
            CancellationToken.None);

        return posted.IsSuccess ? null : posted.Error;
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
