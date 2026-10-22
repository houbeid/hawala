using System.Net;
using System.Security.Cryptography;
using Hawala.SharedInfrastructure.Security;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Parties.Infrastructure;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Chiffrement au repos (phase 6, règle validée le 22 octobre 2026) : téléphone et NNI des tiers chiffrés en base
/// (AES-GCM), unicité par empreinte, valeurs en clair relues puis chiffrées au démarrage ; l'API les rend en clair.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PartyEncryptionTests(ApiFixture fixture)
{
    // --- Le chiffreur ------------------------------------------------------

    private static readonly FieldProtector Protector = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void A_value_comes_back_after_encryption_and_is_never_written_the_same_way()
    {
        var first = Protector.Protect("+22236123456");
        var second = Protector.Protect("+22236123456");

        Assert.StartsWith("v1:", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.Equal("+22236123456", Protector.Unprotect(first));
        Assert.Equal("رقم وطني ١٢٣", Protector.Unprotect(Protector.Protect("رقم وطني ١٢٣")));
    }

    [Fact]
    public void The_index_is_the_same_for_the_same_value_and_differs_with_the_key()
    {
        var other = new FieldProtector(RandomNumberGenerator.GetBytes(32));

        Assert.Equal(Protector.Index("1234567890"), Protector.Index("1234567890"));
        Assert.NotEqual(Protector.Index("1234567890"), Protector.Index("1234567891"));
        Assert.NotEqual(Protector.Index("1234567890"), other.Index("1234567890"));
        Assert.Equal(FieldProtector.IndexLength, Protector.Index("x").Length);
    }

    [Fact]
    public void A_tampered_value_or_another_key_is_refused()
    {
        var stored = Protector.Protect("NNI 42");
        var bytes = Convert.FromBase64String(stored[3..]);
        bytes[^1] ^= 0x01;
        var tampered = "v1:" + Convert.ToBase64String(bytes);

        Assert.ThrowsAny<CryptographicException>(() => Protector.Unprotect(tampered));
        Assert.ThrowsAny<CryptographicException>(() => new FieldProtector(RandomNumberGenerator.GetBytes(32)).Unprotect(stored));
    }

    [Fact]
    public void A_legacy_plain_value_is_read_as_is()
    {
        Assert.Equal("+22236123456", Protector.Unprotect("+22236123456"));
    }

    [Fact]
    public void A_missing_or_short_key_stops_the_start()
    {
        var missing = new ConfigurationBuilder().Build();
        var short16 = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [FieldProtector.ConfigurationKey] = Convert.ToBase64String(new byte[16]) })
            .Build();

        Assert.Throws<InvalidOperationException>(() => FieldProtector.FromConfiguration(missing));
        Assert.Throws<InvalidOperationException>(() => FieldProtector.FromConfiguration(short16));
    }

    [Fact]
    public void A_column_holds_the_longest_encrypted_value()
    {
        var longest = new string('م', 200);

        Assert.True(Protector.Protect(longest).Length <= FieldProtector.StoredLength(200));
    }

    // --- Les tiers ---------------------------------------------------------

    [Fact]
    public async Task Phone_and_nni_are_encrypted_in_the_database_and_clear_through_the_api()
    {
        var ctx = await ArrangeAsync();
        var phone = NewPhone();
        var nni = NewNni();

        var id = await ctx.CreateAsync(phone, nni);

        var (storedPhone, storedNni) = await ReadStoredAsync(ctx.Factory, id);
        Assert.StartsWith("v1:", storedPhone, StringComparison.Ordinal);
        Assert.StartsWith("v1:", storedNni, StringComparison.Ordinal);
        Assert.DoesNotContain(phone[4..], storedPhone, StringComparison.Ordinal);
        Assert.DoesNotContain(nni, storedNni, StringComparison.Ordinal);

        using var get = await ctx.Client.GetPartyAsync(ctx.Token, id);
        var party = await get.ReadJsonAsync();
        Assert.Equal(phone, party.GetProperty("phone").GetString());
        Assert.Equal(nni, party.GetProperty("nni").GetString());
    }

    [Fact]
    public async Task Phone_and_nni_stay_unique()
    {
        var ctx = await ArrangeAsync();
        var phone = NewPhone();
        var nni = NewNni();
        await ctx.CreateAsync(phone, nni);

        using var samePhone = await ctx.Client.CreatePartyAsync(ctx.Token, new { name = "Doublon téléphone", type = "Client", phone });
        using var sameNni = await ctx.Client.CreatePartyAsync(ctx.Token, new { name = "Doublon NNI", type = "Client", nni });

        Assert.Equal("Parties.Party.PhoneTaken", await samePhone.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NniTaken", await sameNni.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Searching_part_of_a_phone_still_works()
    {
        var ctx = await ArrangeAsync();
        var phone = NewPhone();
        var id = await ctx.CreateAsync(phone, nni: null);

        using var list = await ctx.Client.ListPartiesAsync(ctx.Token, $"search={phone[5..11]}");

        var ids = (await list.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid());
        Assert.Contains(id, ids);
    }

    [Fact]
    public async Task Legacy_plain_values_are_encrypted_at_start_and_count_for_uniqueness()
    {
        var ctx = await ArrangeAsync();
        var phone = NewPhone();
        var nni = NewNni();
        var id = Guid.CreateVersion7();
        await using (var connection = await ctx.Factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync())
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO parties.parties ("Id", "Name", "Type", "Phone", "Nni", "InBalanceSheet", "IsArchived", "CreatedAt")
                VALUES (@id, 'Ancien tiers', 'Client', @phone, @nni, true, false, now())
                """,
                connection);
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("phone", phone);
            insert.Parameters.AddWithValue("nni", nni);
            await insert.ExecuteNonQueryAsync();
        }

        // Le démarrage (migrations + reprise) est rejoué : la reprise ne touche que les valeurs encore en clair.
        await ctx.Factory.Services.InitializePartiesModuleAsync();

        var (storedPhone, storedNni) = await ReadStoredAsync(ctx.Factory, id);
        Assert.StartsWith("v1:", storedPhone, StringComparison.Ordinal);
        Assert.StartsWith("v1:", storedNni, StringComparison.Ordinal);
        using var samePhone = await ctx.Client.CreatePartyAsync(ctx.Token, new { name = "Doublon ancien", type = "Client", phone });
        Assert.Equal("Parties.Party.PhoneTaken", await samePhone.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private static string NewPhone() => $"+2223{Random.Shared.Next(1_000_000, 9_999_999)}";

    private static string NewNni() => $"{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";

    private static async Task<(string? Phone, string? Nni)> ReadStoredAsync(HawalaApiFactory factory, Guid id)
    {
        await using var connection = await factory.Services.GetRequiredService<NpgsqlDataSource>().OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""SELECT "Phone", "Nni" FROM parties.parties WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return new Context(factory, client, token);
    }

    private sealed record Context(HawalaApiFactory Factory, HttpClient Client, string Token)
    {
        public async Task<Guid> CreateAsync(string phone, string? nni)
        {
            using var response = await Client.CreatePartyAsync(Token, new { name = "Tiers chiffré", type = "Client", phone, nni });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }
    }
}
