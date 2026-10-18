using System.Net;
using Hawala.SharedKernel.Messaging;
using Identity.Application.Features.Users.BlockUser;
using Identity.Domain;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Users;

[Collection(ApiCollection.Name)]
public sealed class BlockUserTests(ApiFixture fixture)
{
    // --- Blocage réussi ----------------------------------------------------

    [Fact]
    public async Task Blocking_an_agent_cuts_all_its_sessions_at_once()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, agentCookie) = await client.LoginOkAsync(agent.Email, agent.Password);
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.BlockUserAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Jeton d'accès refusé tout de suite, refresh token révoqué.
        using var me = await client.MeAsync(agentToken);
        using var refresh = await client.RefreshAsync(agentCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, refresh.StatusCode);

        // Le bon mot de passe ne suffit plus.
        using var login = await client.LoginAsync(agent.Email, agent.Password);
        Assert.Equal(HttpStatusCode.Forbidden, login.StatusCode);
        Assert.Equal("Identity.Auth.AccountBlocked", await login.ReadErrorCodeAsync());

        using var read = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal("Blocked", (await read.ReadJsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Supervisor_can_block_another_supervisor()
    {
        var factory = await fixture.GetFactoryAsync();
        var other = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.BlockUserAsync(supervisorToken, other.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Supervisor_cannot_block_own_account()
    {
        var factory = await fixture.GetFactoryAsync();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var response = await client.BlockUserAsync(token, supervisor.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Identity.Account.CannotBlockSelf", await response.ReadErrorCodeAsync());

        // Toujours actif : son jeton fonctionne encore.
        using var me = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Blocking_twice_is_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);
        using var first = await client.BlockUserAsync(supervisorToken, agent.Id);

        using var second = await client.BlockUserAsync(supervisorToken, agent.Id);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Identity.Account.AlreadyBlocked", await second.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var supervisorToken = await LoginAsSupervisorAsync(factory, client);

        using var response = await client.BlockUserAsync(supervisorToken, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Identity.Account.NotFound", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_block_accounts()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var colleague = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var response = await client.BlockUserAsync(agentToken, colleague.Id);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Dernier Superviseur actif (concurrence réelle sur PostgreSQL) -----

    /// <summary>
    /// Par l'API, ce cas n'arrive qu'en course : A bloque B pendant que B bloque A (on ne peut pas se bloquer
    /// soi-même). En HTTP le test serait instable (si les requêtes ne se chevauchent pas, B reçoit 401),
    /// d'où l'appel direct du vrai handler, avec sa transaction, dans deux scopes en parallèle.
    /// Limite : si les deux appels ne se chevauchent pas, le test passe sans avoir éprouvé le verrou ;
    /// il détecte une absence de verrou de façon probable, pas certaine.
    /// </summary>
    [Fact]
    public async Task Two_supervisors_blocking_each_other_at_once_leave_one_active()
    {
        var factory = await fixture.GetFactoryAsync();
        var a = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var b = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);

        // Ne garder que A et B actifs ; les autres sont remis en état à la fin (LoginTests utilise
        // le Superviseur créé au démarrage).
        var others = (await TestAccounts.GetActiveSupervisorIdsAsync(factory))
            .Where(id => id != a.Id && id != b.Id)
            .ToList();
        foreach (var id in others)
        {
            await TestAccounts.BlockAsync(factory, id);
        }

        try
        {
            using var start = new ManualResetEventSlim(false);
            var aBlocksB = Task.Run(() => BlockAsync(factory, actorId: a.Id, userId: b.Id, start));
            var bBlocksA = Task.Run(() => BlockAsync(factory, actorId: b.Id, userId: a.Id, start));
            start.Set();
            var results = await Task.WhenAll(aBlocksB, bBlocksA);

            Assert.Single(results, r => r.IsSuccess);
            Assert.Single(results, r => r.IsFailure && r.Error == IdentityErrors.Account.LastActiveSupervisor);

            var stillActive = await TestAccounts.GetActiveSupervisorIdsAsync(factory);
            Assert.Single(stillActive);
            Assert.Contains(stillActive.Single(), new[] { a.Id, b.Id });
        }
        finally
        {
            foreach (var id in others)
            {
                await TestAccounts.UnblockAsync(factory, id);
            }
        }
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<Hawala.SharedKernel.Results.Result> BlockAsync(
        HawalaApiFactory factory, Guid actorId, Guid userId, ManualResetEventSlim start)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<BlockUserCommand>>();

        start.Wait();
        return await handler.Handle(new BlockUserCommand(actorId, userId), CancellationToken.None);
    }

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }
}
