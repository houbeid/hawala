using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Identity.Application.Features.Sessions;
using Identity.Contracts;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Identity.IntegrationTests.Auth;

/// <summary>
/// Sessions et appareils (phase 6, règles validées le 22 octobre 2026) : chaque compte voit et ferme ses sessions ;
/// le Superviseur celles de tous ; un compte qui a users.manage, celles de son agence. Une session fermée est refusée
/// à la requête suivante (« sid » du jeton) et prévenue en direct ; dernière connexion dans la fiche du compte.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SessionsTests(ApiFixture fixture)
{
    private const string Phone = "Mozilla/5.0 (Linux; Android 14) Telephone de test";
    private const string Laptop = "Mozilla/5.0 (Windows NT 10.0) Ordinateur de test";

    [Fact]
    public async Task My_sessions_show_each_device_and_mark_this_one()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (phoneToken, _) = await LoginAsync(factory, agent, Phone);
        await LoginAsync(factory, agent, Laptop);
        using var client = factory.CreateApiClient();

        using var response = await client.SendGetAsync(phoneToken, "/api/v1/auth/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sessions = (await response.ReadJsonAsync()).EnumerateArray().ToList();
        Assert.Equal(2, sessions.Count);
        var current = Assert.Single(sessions, s => s.GetProperty("current").GetBoolean());
        Assert.Equal(Phone, current.GetProperty("device").GetString());
        Assert.Contains(sessions, s => s.GetProperty("device").GetString() == Laptop);
        Assert.All(sessions, s => Assert.True(s.GetProperty("startedAt").GetDateTimeOffset() <= s.GetProperty("lastUsedAt").GetDateTimeOffset()));
    }

    [Fact]
    public async Task Closing_another_of_my_sessions_cuts_it_right_away()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (phoneToken, _) = await LoginAsync(factory, agent, Phone);
        var (laptopToken, laptopCookie) = await LoginAsync(factory, agent, Laptop);
        using var client = factory.CreateApiClient();
        var laptopSession = await SessionIdAsync(client, phoneToken, Laptop);

        using var close = await client.PostJsonAsync(phoneToken, $"/api/v1/auth/sessions/{laptopSession}/revoke", new { });

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);

        // Le jeton d'accès de l'ordinateur est refusé tout de suite, sans attendre ses 15 minutes ; son refresh aussi.
        using var laptopMe = await client.MeAsync(laptopToken);
        using var laptopRefresh = await client.RefreshAsync(laptopCookie);
        using var phoneMe = await client.MeAsync(phoneToken);
        Assert.Equal(HttpStatusCode.Unauthorized, laptopMe.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, laptopRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.OK, phoneMe.StatusCode);
    }

    [Fact]
    public async Task Closing_this_session_signs_me_out()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (token, _) = await LoginAsync(factory, agent, Phone);
        using var client = factory.CreateApiClient();
        var session = await SessionIdAsync(client, token, Phone);

        using var close = await client.PostJsonAsync(token, $"/api/v1/auth/sessions/{session}/revoke", new { });

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.Contains(close.Headers.GetValues("Set-Cookie"), c => c.StartsWith($"{ApiClient.RefreshCookieName}=;", StringComparison.Ordinal));
        using var me = await client.MeAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task I_cannot_close_the_session_of_someone_else_from_my_list()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var other = await TestAccounts.CreateAsync(factory);
        var (token, _) = await LoginAsync(factory, agent, Phone);
        var (otherToken, _) = await LoginAsync(factory, other, Laptop);
        using var client = factory.CreateApiClient();
        var otherSession = await SessionIdAsync(client, otherToken, Laptop);

        using var close = await client.PostJsonAsync(token, $"/api/v1/auth/sessions/{otherSession}/revoke", new { });

        Assert.Equal(HttpStatusCode.NotFound, close.StatusCode);
        using var otherMe = await client.MeAsync(otherToken);
        Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
    }

    [Fact]
    public async Task The_supervisor_lists_and_closes_the_sessions_of_an_account_and_it_is_audited()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (agentToken, _) = await LoginAsync(factory, agent, Phone);
        var (supervisorToken, _) = await LoginAsync(factory, supervisor, Laptop);
        using var client = factory.CreateApiClient();

        using var list = await client.SendGetAsync(supervisorToken, $"/api/v1/users/{agent.Id}/sessions");
        var session = Assert.Single((await list.ReadJsonAsync()).EnumerateArray().ToList());
        Assert.False(session.GetProperty("current").GetBoolean());
        var sessionId = session.GetProperty("id").GetGuid();

        using var close = await client.PostJsonAsync(supervisorToken, $"/api/v1/users/{agent.Id}/sessions/{sessionId}/revoke", new { });

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        using var agentMe = await client.MeAsync(agentToken);
        Assert.Equal(HttpStatusCode.Unauthorized, agentMe.StatusCode);
        Assert.Equal(1, await CountAuditAsync(factory, agent.Id));
    }

    [Fact]
    public async Task Closing_my_own_session_is_not_audited()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (token, _) = await LoginAsync(factory, agent, Phone);
        using var client = factory.CreateApiClient();
        var session = await SessionIdAsync(client, token, Phone);

        using var close = await client.PostJsonAsync(token, $"/api/v1/auth/sessions/{session}/revoke", new { });

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.Equal(0, await CountAuditAsync(factory, agent.Id));
    }

    [Fact]
    public async Task An_agency_manager_closes_sessions_of_his_agency_only()
    {
        var factory = await fixture.GetFactoryAsync();
        var manager = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var colleague = await TestAccounts.CreateAsync(factory, agencyCode: "NKC");
        var dakar = await TestAccounts.CreateAsync(factory, agencyCode: "DKR");
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        string[] permissions = [.. RolePermissions.For(RoleTemplate.Agent), Permissions.UsersManage];
        using var grant = await client.PutJsonAsync(supervisorToken, $"/api/v1/users/{manager.Id}/permissions", new { permissions });
        var (managerToken, _) = await client.LoginOkAsync(manager.Email, manager.Password);
        var (colleagueToken, _) = await LoginAsync(factory, colleague, Phone);
        var (dakarToken, _) = await LoginAsync(factory, dakar, Phone);
        var colleagueSession = await SessionIdAsync(client, colleagueToken, Phone);
        var dakarSession = await SessionIdAsync(client, dakarToken, Phone);

        using var own = await client.PostJsonAsync(managerToken, $"/api/v1/users/{colleague.Id}/sessions/{colleagueSession}/revoke", new { });
        using var other = await client.PostJsonAsync(managerToken, $"/api/v1/users/{dakar.Id}/sessions/{dakarSession}/revoke", new { });
        using var otherList = await client.SendGetAsync(managerToken, $"/api/v1/users/{dakar.Id}/sessions");

        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherList.StatusCode);
        using var dakarMe = await client.MeAsync(dakarToken);
        Assert.Equal(HttpStatusCode.OK, dakarMe.StatusCode);
    }

    [Fact]
    public async Task Last_login_is_shown_on_the_account()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        using var client = factory.CreateApiClient();
        var (supervisorToken, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        using var before = await client.GetUserAsync(supervisorToken, agent.Id);
        Assert.Equal(JsonValueKind.Null, (await before.ReadJsonAsync()).GetProperty("lastLoginAt").ValueKind);

        var loginAt = DateTimeOffset.UtcNow;
        await LoginAsync(factory, agent, Phone);
        using var after = await client.GetUserAsync(supervisorToken, agent.Id);

        var lastLogin = (await after.ReadJsonAsync()).GetProperty("lastLoginAt").GetDateTimeOffset();
        Assert.True(lastLogin >= loginAt.AddSeconds(-5));
    }

    [Fact]
    public async Task A_closed_session_is_told_in_real_time()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (phoneToken, _) = await LoginAsync(factory, agent, Phone);
        var (laptopToken, _) = await LoginAsync(factory, agent, Laptop);
        using var client = factory.CreateApiClient();
        var laptopSession = await SessionIdAsync(client, phoneToken, Laptop);

        var channel = Channel.CreateUnbounded<JsonElement>();
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/live"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(laptopToken);
            })
            .Build();
        connection.On<JsonElement>("SessionClosed", received => channel.Writer.TryWrite(received));
        await connection.StartAsync();

        using var close = await client.PostJsonAsync(phoneToken, $"/api/v1/auth/sessions/{laptopSession}/revoke", new { });

        Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var message = await channel.Reader.ReadAsync(timeout.Token);
        Assert.Equal(laptopSession, message.GetProperty("sessionId").GetGuid());
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Connexion depuis un « appareil » (User-Agent) ; renvoie le jeton d'accès et le cookie.</summary>
    private static async Task<(string Token, string Cookie)> LoginAsync(HawalaApiFactory factory, TestAccount account, string device)
    {
        using var client = factory.CreateApiClient();
        Assert.True(client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", device));
        return await client.LoginOkAsync(account.Email, account.Password);
    }

    private static async Task<Guid> SessionIdAsync(HttpClient client, string token, string device)
    {
        using var response = await client.SendGetAsync(token, "/api/v1/auth/sessions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).EnumerateArray()
            .Single(s => s.GetProperty("device").GetString() == device)
            .GetProperty("id").GetGuid();
    }

    private static async Task<long> CountAuditAsync(HawalaApiFactory factory, Guid accountId)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """SELECT count(*) FROM audit.audit_log WHERE "Action" = @action AND "EntityId" = @id""", connection);
        command.Parameters.AddWithValue("action", SessionHandlers.AuditAction);
        command.Parameters.AddWithValue("id", accountId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
