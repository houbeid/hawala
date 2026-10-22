using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Modification de la fiche d'un tiers par le Superviseur (état complet). La base est partagée :
/// chaque test utilise des téléphones et NNI aléatoires (fictifs).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UpdatePartyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Supervisor_updates_the_whole_card()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, NewPhone(), NewNni());
        var newPhone = NewPhone();
        var newNni = NewNni();

        using var response = await client.UpdatePartyAsync(token, id, new
        {
            name = "  Nom Corrigé  ",
            type = "supplier",
            phone = newPhone,
            nni = newNni,
            address = "Médina",
            note = (string?)null,
            inBalanceSheet = false,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var party = await ReadAsync(client, token, id);
        Assert.Equal("Nom Corrigé", party.GetProperty("name").GetString());
        Assert.Equal("Supplier", party.GetProperty("type").GetString());
        Assert.Equal("40", party.GetProperty("accountCode").GetString());
        Assert.Equal(newPhone, party.GetProperty("phone").GetString());
        Assert.Equal(newNni, party.GetProperty("nni").GetString());
        Assert.Equal("Médina", party.GetProperty("address").GetString());
        Assert.Equal(JsonValueKind.Null, party.GetProperty("note").ValueKind);
        Assert.False(party.GetProperty("inBalanceSheet").GetBoolean());
    }

    [Fact]
    public async Task Party_out_of_the_balance_sheet_appears_with_the_out_filter()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var tag = Guid.NewGuid().ToString("N")[..10];
        var id = await CreateAsync(client, token, phone: null, nni: null, name: $"Hors {tag}");

        using var response = await client.UpdatePartyAsync(token, id, new { name = $"Hors {tag}", type = "Client", inBalanceSheet = false });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var outList = await client.ListPartiesAsync(token, $"search={tag}&balanceSheet=out");
        using var inList = await client.ListPartiesAsync(token, $"search={tag}&balanceSheet=in");
        Assert.Equal(1, (await outList.ReadJsonAsync()).GetProperty("count").GetInt32());
        Assert.Equal(0, (await inList.ReadJsonAsync()).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Keeping_its_own_phone_and_nni_is_not_a_duplicate_and_empty_values_remove_them()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var phone = NewPhone();
        var nni = NewNni();
        var id = await CreateAsync(client, token, phone, nni);

        using var keep = await client.UpdatePartyAsync(token, id, new { name = "Même", type = "Client", phone, nni, inBalanceSheet = true });
        using var remove = await client.UpdatePartyAsync(token, id, new { name = "Même", type = "Client", phone = "", inBalanceSheet = true });

        Assert.Equal(HttpStatusCode.NoContent, keep.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);
        var party = await ReadAsync(client, token, id);
        Assert.Equal(JsonValueKind.Null, party.GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, party.GetProperty("nni").ValueKind);
    }

    // --- Refus -------------------------------------------------------------

    [Fact]
    public async Task Phone_or_nni_of_another_party_is_refused_and_nothing_changes()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var otherPhone = NewPhone();
        var otherNni = NewNni();
        await CreateAsync(client, token, otherPhone, otherNni);
        var id = await CreateAsync(client, token, phone: null, nni: null, name: "Original");

        using var samePhone = await client.UpdatePartyAsync(token, id, new { name = "Pris", type = "Client", phone = otherPhone, inBalanceSheet = true });
        using var sameNni = await client.UpdatePartyAsync(token, id, new { name = "Pris", type = "Client", nni = otherNni, inBalanceSheet = true });

        Assert.Equal("Parties.Party.PhoneTaken", await samePhone.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NniTaken", await sameNni.ReadErrorCodeAsync());
        Assert.Equal("Original", (await ReadAsync(client, token, id)).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Invalid_fields_are_refused()
    {
        var factory = await fixture.GetFactoryAsync();
        using var client = factory.CreateApiClient();
        var token = await LoginAsSupervisorAsync(factory, client);
        var id = await CreateAsync(client, token, phone: null, nni: null);

        using var noBalanceSheet = await client.UpdatePartyAsync(token, id, new { name = "Nom", type = "Client" });
        using var noName = await client.UpdatePartyAsync(token, id, new { name = " ", type = "Client", inBalanceSheet = true });
        using var badType = await client.UpdatePartyAsync(token, id, new { name = "Nom", type = "Banque", inBalanceSheet = true });
        using var badPhone = await client.UpdatePartyAsync(token, id, new { name = "Nom", type = "Client", phone = "22 12", inBalanceSheet = true });
        using var unknown = await client.UpdatePartyAsync(token, Guid.NewGuid(), new { name = "Nom", type = "Client", inBalanceSheet = true });

        Assert.Equal("Parties.Party.InBalanceSheetRequired", await noBalanceSheet.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.NameEmpty", await noName.ReadErrorCodeAsync());
        Assert.Equal("Parties.Party.TypeInvalid", await badType.ReadErrorCodeAsync());
        Assert.Equal("Parties.Phone.Invalid", await badPhone.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Agent_can_create_but_not_update_a_party()
    {
        var factory = await fixture.GetFactoryAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        using var client = factory.CreateApiClient();
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);
        var id = await CreateAsync(client, agentToken, phone: null, nni: null);

        using var response = await client.UpdatePartyAsync(agentToken, id, new { name = "Modifié", type = "Client", inBalanceSheet = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private static string NewPhone() =>
        "+22171" + Random.Shared.Next(1_000_000, 9_999_999).ToString(CultureInfo.InvariantCulture);

    private static string NewNni() => "U" + Guid.NewGuid().ToString("N")[..12];

    private static async Task<Guid> CreateAsync(
        HttpClient client, string token, string? phone, string? nni, string name = "Tiers à modifier")
    {
        using var response = await client.CreatePartyAsync(token, new { name, type = "Client", phone, nni });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string token, Guid id)
    {
        using var response = await client.GetPartyAsync(token, id);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static async Task<string> LoginAsSupervisorAsync(HawalaApiFactory factory, HttpClient client)
    {
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        return token;
    }
}
