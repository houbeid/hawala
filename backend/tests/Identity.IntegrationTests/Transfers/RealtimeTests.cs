using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Temps réel (règles validées le 21 octobre 2026) : message léger « TransferChanged » (sans montant ni nom) aux deux agences
/// et aux Superviseurs, envoyé APRÈS la validation de la transaction ; « RatesChanged » à tous quand un taux change.
/// Transport long polling sur le serveur de test ; chaque test filtre les messages sur ses propres transferts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RealtimeTests(ApiFixture fixture)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Silence = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task A_new_transfer_reaches_both_agencies_and_the_supervisors_without_amounts()
    {
        var ctx = await ArrangeAsync();
        await using var nkc = await ctx.ListenAsync(ctx.NkcAgentToken);
        await using var dkr = await ctx.ListenAsync(ctx.DkrAgentToken);
        await using var supervisor = await ctx.ListenAsync(ctx.SupervisorToken);

        var (id, _) = await ctx.CreateAsync();

        foreach (var listener in new[] { nkc, dkr, supervisor })
        {
            var message = await listener.NextForAsync(id);
            Assert.Equal("Created", message.GetProperty("event").GetString());
            Assert.Equal("NotDelivered", message.GetProperty("status").GetString());
            Assert.Equal(1, message.GetProperty("version").GetInt32());
            Assert.False(message.TryGetProperty("amountXof", out _));
            Assert.False(message.TryGetProperty("amountMro", out _));
            Assert.False(message.TryGetProperty("senderInfo", out _));
        }
    }

    [Fact]
    public async Task Delivery_and_wrong_codes_are_announced_too()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateAsync();
        await using var nkc = await ctx.ListenAsync(ctx.NkcAgentToken);

        using (var wrong = await ctx.DeliverAsync(id, WrongCode(code)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        using (var deliver = await ctx.DeliverAsync(id, code))
        {
            Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        }

        Assert.Equal("WrongCode", (await nkc.NextForAsync(id)).GetProperty("event").GetString());
        var delivered = await nkc.NextForAsync(id);
        Assert.Equal("Delivered", delivered.GetProperty("event").GetString());
        Assert.Equal("Delivered", delivered.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_refused_operation_announces_nothing()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateAsync();
        await using var dkr = await ctx.ListenAsync(ctx.DkrAgentToken);

        // Total livré faux : refusé avant toute écriture, rien n'est validé, donc rien n'est annoncé.
        using var refused = await ctx.Client.DeliverTransferAsync(ctx.DkrAgentToken, id, new
        {
            code = "000000",
            operationAt = ctx.OperationAt.AddHours(1),
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 1m } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.False(await dkr.AnyForAsync(id, Silence));
    }

    [Fact]
    public async Task A_new_daily_rate_is_announced_to_everyone()
    {
        var ctx = await ArrangeAsync();
        await using var dkr = await ctx.ListenAsync(ctx.DkrAgentToken, "RatesChanged");
        var day = ctx.Day.AddDays(1);

        using var rate = await ctx.Client.SetDailyRateAsync(ctx.SupervisorToken, day, 3610m, 3660m);

        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);
        var message = await dkr.NextAsync();
        Assert.Equal(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), message.GetProperty("date").GetString());
    }

    [Fact]
    public async Task An_anonymous_connection_is_refused()
    {
        var ctx = await ArrangeAsync();

        await using var connection = ctx.BuildConnection(accessToken: null);

        await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static string WrongCode(string code) =>
        ((int.Parse(code, CultureInfo.InvariantCulture) + 1) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(token, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        return new Context(
            factory, client, token, nkcToken, dkrToken, dkr, day,
            await CreateCashAsync(client, token, nkc), await CreateCashAsync(client, token, dkr));
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    /// <summary>Une connexion au hub et les messages reçus d'un type.</summary>
    private sealed class Listener(HubConnection connection, Channel<JsonElement> messages) : IAsyncDisposable
    {
        public async Task<JsonElement> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Wait);
            return await messages.Reader.ReadAsync(timeout.Token);
        }

        public async Task<JsonElement> NextForAsync(Guid transferId)
        {
            using var timeout = new CancellationTokenSource(Wait);
            while (true)
            {
                var message = await messages.Reader.ReadAsync(timeout.Token);
                if (message.GetProperty("transferId").GetGuid() == transferId)
                {
                    return message;
                }
            }
        }

        public async Task<bool> AnyForAsync(Guid transferId, TimeSpan during)
        {
            using var timeout = new CancellationTokenSource(during);
            try
            {
                while (true)
                {
                    var message = await messages.Reader.ReadAsync(timeout.Token);
                    if (message.GetProperty("transferId").GetGuid() == transferId)
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed record Context(
        HawalaApiFactory Factory,
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Dkr,
        DateOnly Day,
        Guid NkcCash,
        Guid DkrCash)
    {
        public DateTimeOffset OperationAt => new(Day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);

        public HubConnection BuildConnection(string? accessToken) =>
            new HubConnectionBuilder()
                .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/live"), options =>
                {
                    options.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;
                    if (accessToken is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                    }
                })
                .Build();

        public async Task<Listener> ListenAsync(string token, string message = "TransferChanged")
        {
            var connection = BuildConnection(token);
            var channel = Channel.CreateUnbounded<JsonElement>();
            connection.On<JsonElement>(message, received => channel.Writer.TryWrite(received));
            await connection.StartAsync();
            return new Listener(connection, channel);
        }

        public async Task<(Guid Id, string Code)> CreateAsync()
        {
            using var response = await Client.CreateTransferAsync(
                NkcAgentToken,
                new
                {
                    destinationAgencyId = Dkr,
                    amountXof = 50_000m,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 36_500m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("withdrawalCode").GetString()!);
        }

        public Task<HttpResponseMessage> DeliverAsync(Guid id, string code) =>
            Client.DeliverTransferAsync(DkrAgentToken, id, new
            {
                code,
                operationAt = OperationAt.AddHours(1),
                payments = new[] { new { treasuryAccountId = DkrCash, amount = 50_000m } },
            });
    }
}
