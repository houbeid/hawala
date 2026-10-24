using System.Globalization;
using System.Net;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using UglyToad.PdfPig;

namespace Identity.IntegrationTests.Transfers;

/// <summary>
/// Reçu PDF de l'expéditeur (règles du 24 octobre 2026) : agence d'envoi ou Superviseur ; avec le code seulement pour le compte
/// qui le détient (créateur, ou auteur du dernier nouveau code), code faux refusé sans être compté ; duplicata sans code ;
/// répartition et part à crédit sur le reçu, jamais les taux ni le nom du tiers. Le texte du PDF est relu avec PdfPig.
/// Transfert de référence : Nouakchott → Dakar, 50 000 FCFA à 3 650 (36 500 MRO) ; taux d'une date tirée au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TransferReceiptTests(ApiFixture fixture)
{
    [Fact]
    public async Task The_creator_gets_the_receipt_with_the_code_and_the_payment_details()
    {
        var ctx = await ArrangeAsync();
        var (id, code, reference) = await ctx.CreateAsync();

        using var response = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal($"Recu-{reference}.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.True(response.Headers.CacheControl?.NoStore);

        var text = await PdfTextAsync(response);
        Assert.Contains("ETSEL-VETH", text, StringComparison.Ordinal);
        Assert.Contains(Squash(reference), text, StringComparison.Ordinal);
        Assert.Contains(code, text, StringComparison.Ordinal);
        Assert.Contains("Codederetrait", text, StringComparison.Ordinal);
        Assert.Contains("Expéditeurfictif", text, StringComparison.Ordinal);
        Assert.Contains(Squash(ctx.NkcCashName), text, StringComparison.Ordinal);
        Assert.Contains("36500MRO", text, StringComparison.Ordinal);
        Assert.Contains("50000FCFA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Duplicata", text, StringComparison.Ordinal);
        // Ni prix de vente ni prix d'achat sur le reçu (les chiffres seuls peuvent apparaître dans le code ou un nom tiré au hasard).
        Assert.DoesNotContain("FCFA=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3650MRO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("3600MRO", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Prix", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_sending_agency_and_the_supervisor_get_a_duplicate_without_the_code()
    {
        var ctx = await ArrangeAsync();
        var (id, code, reference) = await ctx.CreateAsync();

        using var byColleague = await ctx.Client.GetTransferReceiptAsync(ctx.OtherNkcAgentToken, id);
        using var bySupervisor = await ctx.Client.GetTransferReceiptAsync(ctx.SupervisorToken, id);

        foreach (var response in new[] { byColleague, bySupervisor })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = await PdfTextAsync(response);
            Assert.Contains("Duplicata", text, StringComparison.Ordinal);
            Assert.Contains(Squash(reference), text, StringComparison.Ordinal);
            Assert.DoesNotContain(code, text, StringComparison.Ordinal);
            Assert.DoesNotContain("Codederetrait", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_destination_agency_gets_no_receipt()
    {
        var ctx = await ArrangeAsync();
        var (id, code, _) = await ctx.CreateAsync();

        using var duplicate = await ctx.Client.GetTransferReceiptAsync(ctx.DkrAgentToken, id);
        using var withCode = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.DkrAgentToken, id, code);

        Assert.Equal(HttpStatusCode.Forbidden, duplicate.StatusCode);
        Assert.Equal("Transfers.Receipt.NotSendingAgency", await duplicate.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Receipt.NotSendingAgency", await withCode.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Only_the_account_holding_the_code_gets_it_on_the_receipt()
    {
        var ctx = await ArrangeAsync();
        var (id, code, _) = await ctx.CreateAsync();

        using var byColleague = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.OtherNkcAgentToken, id, code);
        using var bySupervisor = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.SupervisorToken, id, code);

        Assert.Equal(HttpStatusCode.Forbidden, byColleague.StatusCode);
        Assert.Equal("Transfers.Receipt.NotCodeHolder", await byColleague.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Receipt.NotCodeHolder", await bySupervisor.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task A_wrong_code_is_refused_without_counting_against_delivery()
    {
        var ctx = await ArrangeAsync();
        var (id, code, _) = await ctx.CreateAsync();

        for (var i = 0; i < 6; i++)
        {
            using var wrong = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, WrongCode(code));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal("Transfers.Receipt.WrongCode", await wrong.ReadErrorCodeAsync());
        }

        using var malformed = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, "12ab");
        using var empty = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, null);
        Assert.Equal("Transfers.Receipt.WrongCode", await malformed.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Receipt.WrongCode", await empty.ReadErrorCodeAsync());

        // Rien n'a été compté : le transfert n'est pas bloqué et se livre avec son code.
        using var deliver = await ctx.DeliverAsync(id, code);
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
    }

    [Fact]
    public async Task After_a_new_code_only_its_author_gets_the_receipt_with_it()
    {
        var ctx = await ArrangeAsync();
        var (id, oldCode, _) = await ctx.CreateAsync();

        using var renewed = await ctx.Client.NewTransferCodeAsync(ctx.SupervisorToken, id);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        var newCode = (await renewed.ReadJsonAsync()).GetProperty("withdrawalCode").GetString()!;

        using var byCreator = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, oldCode);
        using var oldBySupervisor = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.SupervisorToken, id, oldCode);
        using var newBySupervisor = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.SupervisorToken, id, newCode);

        Assert.Equal("Transfers.Receipt.NotCodeHolder", await byCreator.ReadErrorCodeAsync());
        Assert.Equal("Transfers.Receipt.WrongCode", await oldBySupervisor.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, newBySupervisor.StatusCode);
        Assert.Contains(newCode, await PdfTextAsync(newBySupervisor), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delivered_transfer_gives_a_duplicate_marked_delivered_and_never_the_code()
    {
        var ctx = await ArrangeAsync();
        var (id, code, _) = await ctx.CreateAsync();
        using var deliver = await ctx.DeliverAsync(id, code);
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);

        using var withCode = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, code);
        using var duplicate = await ctx.Client.GetTransferReceiptAsync(ctx.NkcAgentToken, id);

        Assert.Equal(HttpStatusCode.Conflict, withCode.StatusCode);
        Assert.Equal("Transfers.Receipt.CodeNotUsable", await withCode.ReadErrorCodeAsync());
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("Duplicata·Livré", await PdfTextAsync(duplicate), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_credit_part_is_shown_without_the_party_name()
    {
        var ctx = await ArrangeAsync();
        var partyName = $"Débiteur {Guid.NewGuid():N}"[..20];
        using var party = await ctx.Client.CreatePartyAsync(ctx.SupervisorToken, new { name = partyName, type = "Client" });
        Assert.Equal(HttpStatusCode.Created, party.StatusCode);
        var partyId = (await party.ReadJsonAsync()).GetProperty("id").GetGuid();

        var (id, code, _) = await ctx.CreateAsync(paid: 20_000m, creditPartyId: partyId);
        using var response = await ctx.Client.GetTransferReceiptWithCodeAsync(ctx.NkcAgentToken, id, code);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await PdfTextAsync(response);
        Assert.Contains("Partàcrédit", text, StringComparison.Ordinal);
        Assert.Contains("16500MRO", text, StringComparison.Ordinal);
        Assert.Contains("20000MRO", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Squash(partyName), text, StringComparison.Ordinal);
    }

    // --- Outils ------------------------------------------------------------

    /// <summary>Texte du PDF sans aucun espace (PdfPig ne rend pas toujours les espaces entre les mots).</summary>
    private static async Task<string> PdfTextAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4), StringComparison.Ordinal);
        using var document = PdfDocument.Open(bytes);
        return Squash(string.Concat(document.GetPages().Select(page => page.Text)));
    }

    private static string Squash(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

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
        var otherNkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (otherNkcToken, _) = await client.LoginOkAsync(otherNkcAgent.Email, otherNkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        var nkcCashName = $"Caisse {Guid.NewGuid():N}"[..30];
        return new Context(
            client, token, nkcToken, otherNkcToken, dkrToken, dkr,
            new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
            await CreateCashAsync(client, token, nkc, nkcCashName), nkcCashName,
            await CreateCashAsync(client, token, dkr, $"Caisse {Guid.NewGuid():N}"[..30]));
    }

    private static async Task<Guid> CreateCashAsync(HttpClient client, string token, Guid agencyId, string name)
    {
        using var response = await client.CreateTreasuryAccountAsync(token, new { name, type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string OtherNkcAgentToken,
        string DkrAgentToken,
        Guid Dkr,
        DateTimeOffset OperationAt,
        Guid NkcCash,
        string NkcCashName,
        Guid DkrCash)
    {
        /// <param name="paid">Part payée en caisse (MRO) ; le reste va à crédit si <paramref name="creditPartyId"/> est donné.</param>
        public async Task<(Guid Id, string Code, string Reference)> CreateAsync(decimal paid = 36_500m, Guid? creditPartyId = null)
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
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = paid } },
                    creditPartyId,
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.ReadJsonAsync();
            return (body.GetProperty("id").GetGuid(), body.GetProperty("withdrawalCode").GetString()!, body.GetProperty("reference").GetString()!);
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
