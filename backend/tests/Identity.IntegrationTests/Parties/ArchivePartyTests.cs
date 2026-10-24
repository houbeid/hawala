using System.Net;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Ledger.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Parties;

/// <summary>
/// Archivage d'un tiers (pas de suppression) : seulement à solde nul dans les deux agences ; ses comptes au grand livre
/// sont fermés, puis rouverts au désarchivage. Chaque test crée son propre tiers, avec un mot unique dans le nom.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ArchivePartyTests(ApiFixture fixture)
{
    [Fact]
    public async Task Party_with_zero_balances_is_archived_hidden_and_closed_then_unarchived()
    {
        var (factory, client, token, partyId, tag, nouakchott, _) = await ArrangeAsync();

        using var archive = await client.ArchivePartyAsync(token, partyId);

        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);
        // Échec intermittent (21 et 24 octobre 2026) : le tiers restait visible juste après l'archivage. Le message dit lequel.
        var stillVisible = await VisibleAsync(client, token, $"search={tag}");
        Assert.True(stillVisible.Count == 0, $"Encore visibles après l'archivage : {string.Join(" ; ", stillVisible)}");
        Assert.Equal(1, await CountAsync(client, token, $"search={tag}&includeArchived=true"));
        Assert.All(await LedgerAccountsActiveAsync(factory, partyId), Assert.False);

        // Archivé : plus de changement de solde initial.
        using var opening = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 100m });
        Assert.Equal("Parties.Party.Archived", await opening.ReadErrorCodeAsync());

        using var unarchive = await client.UnarchivePartyAsync(token, partyId);

        Assert.Equal(HttpStatusCode.NoContent, unarchive.StatusCode);
        Assert.Equal(1, await CountAsync(client, token, $"search={tag}"));
        Assert.All(await LedgerAccountsActiveAsync(factory, partyId), Assert.True);
        using var reopened = await client.SetOpeningBalanceAsync(token, partyId, nouakchott, new { amount = 100m });
        Assert.Equal(HttpStatusCode.NoContent, reopened.StatusCode);
    }

    [Fact]
    public async Task Money_left_in_one_agency_blocks_archiving()
    {
        var (factory, client, token, partyId, tag, _, dakar) = await ArrangeAsync();
        using var opening = await client.SetOpeningBalanceAsync(token, partyId, dakar, new { amount = -1_000m });
        Assert.Equal(HttpStatusCode.NoContent, opening.StatusCode);

        using var response = await client.ArchivePartyAsync(token, partyId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Parties.Party.BalanceNotZero", await response.ReadErrorCodeAsync());
        Assert.Equal(1, await CountAsync(client, token, $"search={tag}"));
        Assert.All(await LedgerAccountsActiveAsync(factory, partyId), Assert.True);
    }

    [Fact]
    public async Task Archiving_twice_or_unarchiving_an_active_party_is_refused()
    {
        var (_, client, token, partyId, _, _, _) = await ArrangeAsync();

        using var unarchiveActive = await client.UnarchivePartyAsync(token, partyId);
        using var first = await client.ArchivePartyAsync(token, partyId);
        using var second = await client.ArchivePartyAsync(token, partyId);

        Assert.Equal("Parties.Party.NotArchived", await unarchiveActive.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal("Parties.Party.AlreadyArchived", await second.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Agent_cannot_archive_and_unknown_party_is_not_found()
    {
        var (factory, client, token, partyId, _, _, _) = await ArrangeAsync();
        var agent = await TestAccounts.CreateAsync(factory);
        var (agentToken, _) = await client.LoginOkAsync(agent.Email, agent.Password);

        using var byAgent = await client.ArchivePartyAsync(agentToken, partyId);
        using var unknown = await client.ArchivePartyAsync(token, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    // --- Outils ------------------------------------------------------------

    private async Task<(HawalaApiFactory Factory, HttpClient Client, string Token, Guid PartyId, string Tag, Guid Nouakchott, Guid Dakar)> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);
        // Lettres seulement : la recherche compare aussi les chiffres du texte aux téléphones, et un marqueur avec des
        // chiffres trouvait parfois le téléphone d'un autre test (cause des échecs intermittents des 21 et 24 octobre).
        var tag = new string([.. Guid.NewGuid().ToString("N")[..10].Select(c => (char)('g' + Convert.ToInt32(c.ToString(), 16)))]);

        using var created = await client.CreatePartyAsync(token, new { name = $"Archive {tag}", type = "Client" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var partyId = (await created.ReadJsonAsync()).GetProperty("id").GetGuid();

        await using var scope = factory.Services.CreateAsyncScope();
        var nouakchott = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dakar = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");
        return (factory, client, token, partyId, tag, nouakchott, dakar);
    }

    /// <summary>« nom (id, archivé : oui/non) » de chaque tiers renvoyé par la liste.</summary>
    private static async Task<List<string>> VisibleAsync(HttpClient client, string token, string queryString)
    {
        using var response = await client.ListPartiesAsync(token, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(p =>
            $"{p.GetProperty("name").GetString()} ({p.GetProperty("id").GetGuid()}, archivé : {p.GetProperty("isArchived").GetBoolean()})")];
    }

    private static async Task<int> CountAsync(HttpClient client, string token, string queryString)
    {
        using var response = await client.ListPartiesAsync(token, queryString);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("count").GetInt32();
    }

    private static async Task<List<bool>> LedgerAccountsActiveAsync(HawalaApiFactory factory, Guid partyId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var flags = await scope.ServiceProvider.GetRequiredService<LedgerDbContext>()
            .Accounts.AsNoTracking().Where(a => a.OwnerId == partyId).Select(a => a.IsActive).ToListAsync();
        Assert.Equal(2, flags.Count);
        return flags;
    }
}
