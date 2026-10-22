using System.Text.Json;
using Audit.Contracts;
using Hawala.SharedInfrastructure.Persistence;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Identity.IntegrationTests.Audit;

/// <summary>
/// Journal d'audit (socle, phase 6) : une ligne est écrite dans la transaction de la commande qu'elle trace,
/// jamais en dehors, et la base refuse qu'on la modifie ou la supprime.
/// Éprouvé avec une commande de test passée par le décorateur de transaction commun, comme le grand livre.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditLogTests(ApiFixture fixture)
{
    private static readonly Guid Actor = Guid.Parse("0199f0a0-0000-7000-8000-00000000d001");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Committed_command_keeps_its_audit_line_with_its_changes()
    {
        var factory = await fixture.GetFactoryAsync();
        var target = Guid.NewGuid();
        var agency = Guid.NewGuid();
        var record = new AuditRecord(
            "Tests.Audit.Recorded",
            "TestEntity",
            target,
            Actor,
            agency,
            "Motif de test",
            [new AuditChange("name", "Avant", "Après"), new AuditChange("phone", null, "+22200000001")]);

        var result = await RunAsync(factory, new RecordCommand(record, Fail: false));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var row = await ReadAsync(factory, result.Value);
        Assert.NotNull(row);
        Assert.Equal("Tests.Audit.Recorded", row.Action);
        Assert.Equal("TestEntity", row.EntityType);
        Assert.Equal(target, row.EntityId);
        Assert.Equal(Actor, row.ActorId);
        Assert.Equal(agency, row.AgencyId);
        Assert.Equal("Motif de test", row.Reason);
        Assert.Equal(
            [new AuditChange("name", "Avant", "Après"), new AuditChange("phone", null, "+22200000001")],
            JsonSerializer.Deserialize<List<AuditChange>>(row.ChangesJson, JsonOptions));

        // Hors requête HTTP : ni IP ni appareil.
        Assert.Null(row.IpAddress);
        Assert.Null(row.UserAgent);
    }

    [Fact]
    public async Task Failed_command_leaves_no_audit_line()
    {
        var factory = await fixture.GetFactoryAsync();
        var record = new AuditRecord("Tests.Audit.RolledBack", "TestEntity", Guid.NewGuid(), Actor);

        var result = await RunAsync(factory, new RecordCommand(record, Fail: true));

        Assert.True(result.IsFailure);
        Assert.Equal(0, await CountAsync(factory, record.EntityId!.Value));
    }

    [Fact]
    public async Task Invalid_line_is_refused_and_nothing_is_written()
    {
        var factory = await fixture.GetFactoryAsync();
        var record = new AuditRecord(" ", "TestEntity", Guid.NewGuid(), Actor);

        var result = await RunAsync(factory, new RecordCommand(record, Fail: false));

        Assert.True(result.IsFailure);
        Assert.Equal("Audit.Entry.ActionEmpty", result.Error.Code);
        Assert.Equal(0, await CountAsync(factory, record.EntityId!.Value));
    }

    [Fact]
    public async Task Recording_outside_a_shared_transaction_is_a_bug()
    {
        var factory = await fixture.GetFactoryAsync();
        var record = new AuditRecord("Tests.Audit.NoTransaction", "TestEntity", Guid.NewGuid(), Actor);

        await using var scope = factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => audit.RecordAsync(record, CancellationToken.None));
        Assert.Equal(0, await CountAsync(factory, record.EntityId!.Value));
    }

    [Fact]
    public async Task Database_refuses_to_change_or_delete_an_audit_line()
    {
        var factory = await fixture.GetFactoryAsync();
        var record = new AuditRecord("Tests.Audit.Immutable", "TestEntity", Guid.NewGuid(), Actor);
        var id = (await RunAsync(factory, new RecordCommand(record, Fail: false))).Value;

        await AssertSqlRefusedAsync(factory, """UPDATE audit.audit_log SET "Reason" = 'modifié' WHERE "Id" = @id""", id);
        await AssertSqlRefusedAsync(factory, """DELETE FROM audit.audit_log WHERE "Id" = @id""", id);
        await AssertSqlRefusedAsync(factory, "TRUNCATE audit.audit_log", id);

        Assert.Equal(1, await CountAsync(factory, record.EntityId!.Value));
    }

    // --- Outils ------------------------------------------------------------

    private static async Task<Result<Guid>> RunAsync(HawalaApiFactory factory, RecordCommand command)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var handler = new SharedTransactionCommandHandler<RecordCommand, Guid>(
            new RecordHandler(services.GetRequiredService<IAuditLog>()),
            services.GetRequiredService<SharedConnection>(),
            services.GetServices<ISharedTransactionParticipant>());

        return await handler.Handle(command, CancellationToken.None);
    }

    private static async Task<AuditRow?> ReadAsync(HawalaApiFactory factory, Guid id)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Action", "EntityType", "EntityId", "ActorId", "AgencyId", "Reason", "Changes"::text, "IpAddress", "UserAgent"
            FROM audit.audit_log WHERE "Id" = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new AuditRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    private static async Task<long> CountAsync(HawalaApiFactory factory, Guid entityId)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """SELECT count(*) FROM audit.audit_log WHERE "EntityId" = @entity""", connection);
        command.Parameters.AddWithValue("entity", entityId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task AssertSqlRefusedAsync(HawalaApiFactory factory, string sql, Guid id)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());

        Assert.Equal("23001", error.SqlState);
    }

    private sealed record AuditRow(
        string Action,
        string EntityType,
        Guid? EntityId,
        Guid? ActorId,
        Guid? AgencyId,
        string? Reason,
        string ChangesJson,
        string? IpAddress,
        string? UserAgent);

    /// <summary>Commande de test : écrit une ligne d'audit, puis réussit ou échoue.</summary>
    private sealed record RecordCommand(AuditRecord Record, bool Fail) : ICommand<Guid>;

    private sealed class RecordHandler(IAuditLog audit) : ICommandHandler<RecordCommand, Guid>
    {
        public async Task<Result<Guid>> Handle(RecordCommand command, CancellationToken cancellationToken)
        {
            var recorded = await audit.RecordAsync(command.Record, cancellationToken);

            return command.Fail
                ? Error.Failure("Tests.Simulated", "Échec simulé après la ligne d'audit.")
                : recorded;
        }
    }
}
