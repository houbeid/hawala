using System.Net;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Money;
using Hawala.SharedKernel.Results;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Contracts;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Treasury.Application.Features.Accounts.CreateTreasuryAccount;
using Treasury.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Treasury;

/// <summary>
/// Création d'un compte de trésorerie : il est créé avec son compte au grand livre, dans une seule transaction.
/// La base est partagée : chaque test donne un nom unique à ses comptes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CreateTreasuryAccountTests(ApiFixture fixture)
{
    // --- Création réussie --------------------------------------------------

    [Theory]
    [InlineData("NKC", "Cash", Currency.MRO)]
    [InlineData("NKC", "Wallet", Currency.MRO)]
    [InlineData("DKR", "Bank", Currency.XOF)]
    public async Task Supervisor_creates_an_account_with_its_ledger_account_in_the_agency_currency(
        string agencyCode, string type, Currency expectedCurrency)
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var agencyId = await GetAgencyIdAsync(factory, agencyCode);
        var name = UniqueName("Compte");

        using var response = await client.CreateTreasuryAccountAsync(supervisorToken, new { name, type = type.ToLowerInvariant(), agencyId });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/treasury-accounts/{id}", response.Headers.Location?.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var account = await scope.ServiceProvider.GetRequiredService<TreasuryDbContext>()
            .Accounts.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        Assert.Equal(name, account.Name);
        Assert.Equal(type, account.Type.ToString());
        Assert.Equal(agencyId, account.AgencyId);
        Assert.Equal(expectedCurrency, account.Currency);
        Assert.True(account.IsActive);

        // Son compte au grand livre : même agence, même devise, solde nul.
        var ledgerAccount = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.SingleAsync(a => a.Id == account.LedgerAccountId);
        Assert.Equal(LedgerAccountKind.Treasury, ledgerAccount.Kind);
        Assert.Equal(id, ledgerAccount.OwnerId);
        Assert.Equal(agencyId, ledgerAccount.AgencyId);
        Assert.Equal(expectedCurrency, ledgerAccount.Currency);

        var balance = await scope.ServiceProvider.GetRequiredService<ILedger>()
            .GetBalancesAsync([account.LedgerAccountId], asOf: null, CancellationToken.None);
        Assert.True(balance.Value.Single().Balance.IsZero);
    }

    [Fact]
    public async Task Same_name_is_allowed_in_the_other_agency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var name = UniqueName("Bankily");

        using var nouakchott = await client.CreateTreasuryAccountAsync(
            supervisorToken, new { name, type = "Wallet", agencyId = await GetAgencyIdAsync(factory, "NKC") });
        using var dakar = await client.CreateTreasuryAccountAsync(
            supervisorToken, new { name, type = "Wallet", agencyId = await GetAgencyIdAsync(factory, "DKR") });

        Assert.Equal(HttpStatusCode.Created, nouakchott.StatusCode);
        Assert.Equal(HttpStatusCode.Created, dakar.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Same_name_in_the_same_agency_is_refused_whatever_the_case()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var agencyId = await GetAgencyIdAsync(factory, "NKC");
        var name = UniqueName("Caisse");
        using var first = await client.CreateTreasuryAccountAsync(supervisorToken, new { name, type = "Cash", agencyId });

        using var second = await client.CreateTreasuryAccountAsync(
            supervisorToken, new { name = $"  {name.ToUpperInvariant()}  ", type = "Cash", agencyId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Treasury.Account.NameTaken", await second.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_fields_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        var agencyId = await GetAgencyIdAsync(factory, "NKC");

        using var unknownType = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = UniqueName("X"), type = "Coffre", agencyId });
        using var numericType = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = UniqueName("X"), type = "1", agencyId });
        using var noAgency = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = UniqueName("X"), type = "Cash" });
        using var unknownAgency = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = UniqueName("X"), type = "Cash", agencyId = Guid.NewGuid() });
        using var noName = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = " ", type = "Cash", agencyId });
        using var longName = await client.CreateTreasuryAccountAsync(supervisorToken, new { name = new string('a', 51), type = "Cash", agencyId });

        Assert.Equal("Treasury.Account.TypeInvalid", await unknownType.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.TypeInvalid", await numericType.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.AgencyRequired", await noAgency.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.NameEmpty", await noName.ReadErrorCodeAsync());
        Assert.Equal("Treasury.Account.NameTooLong", await longName.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_create_a_treasury_account()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.CreateTreasuryAccountAsync(
            agentToken, new { name = UniqueName("Caisse"), type = "Cash", agencyId = agent.AgencyId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_create_a_treasury_account()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.CreateTreasuryAccountAsync(accessToken: null, new { name = "Caisse", type = "Cash" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Une transaction sur deux schémas ----------------------------------

    /// <summary>
    /// Le vrai handler de création s'exécute (compte au grand livre ouvert, compte de trésorerie enregistré),
    /// puis la commande échoue : rien ne doit rester, ni dans « treasury » ni dans « ledger ».
    /// </summary>
    [Fact]
    public async Task Failure_after_creation_leaves_nothing_in_either_schema()
    {
        var factory = await fixture.GetFactoryAsync();
        var agencyId = await GetAgencyIdAsync(factory, "NKC");
        var name = UniqueName("Annulée");
        Guid createdId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var failing = new FailAfterCreationHandler(services.GetRequiredService<CreateTreasuryAccountCommandHandler>());
            var handler = new SharedTransactionCommandHandler<CreateTreasuryAccountCommand, Guid>(
                failing,
                services.GetRequiredService<SharedConnection>(),
                services.GetServices<ISharedTransactionParticipant>());

            var result = await handler.Handle(new CreateTreasuryAccountCommand(name, "Cash", agencyId), CancellationToken.None);

            Assert.True(result.IsFailure);
            createdId = failing.CreatedId;
            Assert.NotEqual(Guid.Empty, createdId);
        }

        await using var check = factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<TreasuryDbContext>()
            .Accounts.IgnoreQueryFilters().AnyAsync(a => a.Id == createdId));
        Assert.False(await check.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.AnyAsync(a => a.OwnerId == createdId));
    }

    /// <summary>Exécute la vraie création, puis échoue : simule une étape suivante qui refuserait la commande.</summary>
    private sealed class FailAfterCreationHandler(CreateTreasuryAccountCommandHandler inner)
        : ICommandHandler<CreateTreasuryAccountCommand, Guid>
    {
        public Guid CreatedId { get; private set; }

        public async Task<Result<Guid>> Handle(CreateTreasuryAccountCommand command, CancellationToken cancellationToken)
        {
            var created = await inner.Handle(command, cancellationToken);
            Assert.True(created.IsSuccess);
            CreatedId = created.Value;

            return Error.Failure("Tests.Simulated", "Échec simulé après la création des deux comptes.");
        }
    }

    // --- Outils ------------------------------------------------------------

    private static string UniqueName(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 50)];

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
