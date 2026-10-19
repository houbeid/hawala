using System.Globalization;
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
using Parties.Application.Features.Parties.CreateParty;
using Parties.Domain.Parties;
using Parties.Infrastructure.Persistence;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Création d'un tiers : commun aux deux agences, avec un compte au grand livre par agence, dans une seule transaction.
/// La base est partagée : chaque test utilise des téléphones et NNI aléatoires (fictifs).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CreatePartyTests(ApiFixture fixture)
{
    // --- Création réussie --------------------------------------------------

    [Fact]
    public async Task Party_is_created_with_one_ledger_account_per_agency_in_its_currency()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var phone = NewPhone();

        using var response = await client.CreatePartyAsync(token, new
        {
            name = "  Client Fictif  ",
            type = "client",
            phone,
            nni = NewNni(),
            address = "Ksar",
            note = "Créé par un test",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/parties/{id}", response.Headers.Location?.ToString());

        await using var scope = factory.Services.CreateAsyncScope();
        var party = await scope.ServiceProvider.GetRequiredService<PartiesDbContext>()
            .Parties.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal("Client Fictif", party.Name);
        Assert.Equal(PartyType.Client, party.Type);
        Assert.Equal("41", party.AccountCode);
        Assert.True(party.InBalanceSheet);
        Assert.False(party.IsArchived);

        // Un compte au grand livre par agence : MRO à Nouakchott, FCFA à Dakar, solde nul.
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        Assert.Equal(2, party.AgencyAccounts.Count);

        var ledgerAccounts = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.AsNoTracking().Where(a => a.OwnerId == id).ToListAsync();
        Assert.Equal(2, ledgerAccounts.Count);
        Assert.All(ledgerAccounts, a => Assert.Equal(LedgerAccountKind.Party, a.Kind));
        Assert.Equal(Currency.MRO, ledgerAccounts.Single(a => a.AgencyId == nouakchott).Currency);
        Assert.Equal(Currency.XOF, ledgerAccounts.Single(a => a.AgencyId == dakar).Currency);
        Assert.All(party.AgencyAccounts, pa => Assert.Contains(ledgerAccounts, la => la.Id == pa.LedgerAccountId));

        var balances = await scope.ServiceProvider.GetRequiredService<ILedger>().GetBalancesAsync(
            [.. ledgerAccounts.Select(a => a.Id)], asOf: null, CancellationToken.None);
        Assert.All(balances.Value, b => Assert.True(b.Balance.IsZero));
    }

    [Fact]
    public async Task Agent_can_create_a_party()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.CreatePartyAsync(agentToken, new { name = "Client de Dakar", type = "Client" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Only_name_and_type_are_required_and_two_parties_can_share_a_name()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);

        using var first = await client.CreatePartyAsync(token, new { name = "Mohamed Fictif", type = "Supplier" });
        using var second = await client.CreatePartyAsync(token, new { name = "Mohamed Fictif", type = "Staff", phone = "", nni = " " });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Duplicate_phone_or_nni_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var phone = NewPhone();
        var nni = NewNni();
        using var existing = await client.CreatePartyAsync(token, new { name = "Premier", type = "Client", phone, nni });

        // Même numéro écrit autrement (00 au lieu de +, espaces) : c'est le même tiers.
        using var samePhone = await client.CreatePartyAsync(
            token, new { name = "Doublon", type = "Client", phone = "00" + phone[1..4] + " " + phone[4..] });
        using var sameNni = await client.CreatePartyAsync(token, new { name = "Doublon", type = "Client", nni });

        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, samePhone.StatusCode);
        Assert.Equal("Parties.Party.PhoneTaken", await samePhone.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.Conflict, sameNni.StatusCode);
        Assert.Equal("Parties.Party.NniTaken", await sameNni.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Invalid_fields_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);

        using var noName = await client.CreatePartyAsync(token, new { name = " ", type = "Client" });
        using var longName = await client.CreatePartyAsync(token, new { name = new string('a', 31), type = "Client" });
        using var badType = await client.CreatePartyAsync(token, new { name = "Tiers", type = "Banque" });
        using var badPhone = await client.CreatePartyAsync(token, new { name = "Tiers", type = "Client", phone = "77 123 45 67" });

        Assert.Equal("Parties.Party.NameEmpty", await noName.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NameTooLong", await longName.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.TypeInvalid", await badType.ReadErrorCodeAsync());
        Assert.Equal("Parties.Phone.Invalid", await badPhone.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Anonymous_cannot_create_a_party()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();

        using var response = await client.CreatePartyAsync(accessToken: null, new { name = "Tiers", type = "Client" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Une transaction sur deux schémas ----------------------------------

    /// <summary>
    /// Le vrai handler crée le tiers et ses deux comptes au grand livre, puis la commande échoue :
    /// rien ne doit rester, ni dans « parties » ni dans « ledger ».
    /// </summary>
    [Fact]
    public async Task Failure_after_creation_leaves_nothing_in_either_schema()
    {
        var factory = await fixture.GetFactoryAsync();
        Guid createdId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var failing = new FailAfterCreationHandler(services.GetRequiredService<CreatePartyCommandHandler>());
            var handler = new SharedTransactionCommandHandler<CreatePartyCommand, Guid>(
                failing,
                services.GetRequiredService<SharedConnection>(),
                services.GetServices<ISharedTransactionParticipant>());

            var result = await handler.Handle(
                new CreatePartyCommand("Tiers annulé", "Client", NewPhone(), null, null, null), CancellationToken.None);

            Assert.True(result.IsFailure);
            createdId = failing.CreatedId;
            Assert.NotEqual(Guid.Empty, createdId);
        }

        await using var check = factory.Services.CreateAsyncScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<PartiesDbContext>()
            .Parties.AnyAsync(p => p.Id == createdId));
        Assert.False(await check.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.AnyAsync(a => a.OwnerId == createdId));
    }

    private sealed class FailAfterCreationHandler(CreatePartyCommandHandler inner) : ICommandHandler<CreatePartyCommand, Guid>
    {
        public Guid CreatedId { get; private set; }

        public async Task<Result<Guid>> Handle(CreatePartyCommand command, CancellationToken cancellationToken)
        {
            var created = await inner.Handle(command, cancellationToken);
            Assert.True(created.IsSuccess);
            CreatedId = created.Value;

            return Error.Failure("Tests.Simulated", "Échec simulé après la création du tiers.");
        }
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Numéro sénégalais fictif aléatoire, en E.164.</summary>
    private static string NewPhone() =>
        "+22170" + Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);

    private static string NewNni() => "T" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }
}
