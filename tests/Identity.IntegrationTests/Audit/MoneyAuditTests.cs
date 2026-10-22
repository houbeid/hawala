using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Audit;

/// <summary>
/// Réglages et corrections tracés dans l'audit (phase 6, tour 7c, règle du 22 octobre 2026) : tiers (création, fiche, solde
/// initial, plafond, archivage), caisse (annulation, ajustement), transferts (annulation, modification d'argent, livraison
/// sans code, nouveau code). Pas les opérations courantes. Jamais de téléphone, de NNI, de code ni de pièce d'identité en clair.
/// Taux de vente 3 650 : 10 000 FCFA = 7 300 MRO.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class MoneyAuditTests(ApiFixture fixture)
{
    [Fact]
    public async Task Party_settings_are_traced_without_phone_or_nni_in_clear()
    {
        var ctx = await ArrangeAsync();
        var phone = $"+2223{Random.Shared.Next(1_000_000, 9_999_999)}";
        var nni = $"{Random.Shared.NextInt64(1_000_000_000, 9_999_999_999)}";
        using var create = await ctx.Client.CreatePartyAsync(ctx.Token, new { name = "Tiers audité", type = "Client", phone, nni });
        var id = (await create.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var update = await ctx.Client.UpdatePartyAsync(
            ctx.Token, id, new { name = "Tiers renommé", type = "Client", phone, nni, inBalanceSheet = true });
        using var limit = await ctx.Client.PutJsonAsync(ctx.Token, $"/api/v1/parties/{id}/agencies/{ctx.Nkc}/credit-limit", new { creditLimit = 5_000m });
        using var opening = await ctx.Client.PutJsonAsync(ctx.Token, $"/api/v1/parties/{id}/agencies/{ctx.Nkc}/opening-balance", new { amount = 1_000m });
        using var back = await ctx.Client.PutJsonAsync(ctx.Token, $"/api/v1/parties/{id}/agencies/{ctx.Nkc}/opening-balance", new { amount = 0m });
        using var archive = await ctx.Client.ArchivePartyAsync(ctx.Token, id);
        using var unarchive = await ctx.Client.UnarchivePartyAsync(ctx.Token, id);
        Assert.Equal(HttpStatusCode.NoContent, unarchive.StatusCode);

        var (lines, text) = await ReadAsync(ctx, $"entityType=Party&entityId={id}");

        Assert.Equal(
            [
                "Parties.Party.Unarchived",
                "Parties.Party.Archived",
                "Parties.Party.OpeningBalanceChanged",
                "Parties.Party.OpeningBalanceChanged",
                "Parties.Party.CreditLimitChanged",
                "Parties.Party.Updated",
                "Parties.Party.Created",
            ],
            lines.Select(l => l.GetProperty("action").GetString()));
        Assert.DoesNotContain(phone[4..], text, StringComparison.Ordinal);
        Assert.DoesNotContain(nni, text, StringComparison.Ordinal);
        Assert.Equal(("set", (string?)null), (Change(lines[6], "phone").After, Change(lines[6], "phone").Before));
        Assert.Equal(("Tiers audité", "Tiers renommé"), Change(lines[5], "name"));
        Assert.Single(lines[5].GetProperty("changes").EnumerateArray());
        Assert.Equal(((string?)null, "5000"), Change(lines[4], "creditLimit"));
        Assert.Equal(ctx.Nkc, lines[4].GetProperty("agencyId").GetGuid());
        Assert.Equal(("1000", "0"), Change(lines[2], "openingBalance"));
        Assert.Equal("Tiers renommé", lines[0].GetProperty("entityName").GetString());
    }

    [Fact]
    public async Task Cash_corrections_are_traced_but_not_ordinary_movements()
    {
        var ctx = await ArrangeAsync();
        var party = await ctx.CreatePartyAsync();
        using var movement = await ctx.Client.CreateCashMovementAsync(
            ctx.Token,
            new { agencyId = ctx.Nkc, type = "Receipt", treasuryAccountId = ctx.NkcCash, partyId = party, amount = 500m, note = "Test" },
            Guid.NewGuid().ToString("N"));
        var movementId = (await movement.ReadJsonAsync()).GetProperty("id").GetGuid();
        var (beforeCancel, _) = await ReadAsync(ctx, $"entityId={movementId}");

        using var cancel = await ctx.Client.CancelCashMovementAsync(ctx.Token, movementId, "Saisie en double");
        using var adjust = await ctx.Client.CreateBalanceAdjustmentAsync(
            ctx.Token, new { agencyId = ctx.Nkc, target = "Party", targetId = party, amount = -250m, reason = "Écart constaté" });

        Assert.Empty(beforeCancel);
        var cancelled = Assert.Single((await ReadAsync(ctx, $"entityId={movementId}")).Lines);
        Assert.Equal("Cash.Movement.Cancelled", cancelled.GetProperty("action").GetString());
        Assert.Equal("Saisie en double", cancelled.GetProperty("reason").GetString());
        var adjusted = Assert.Single((await ReadAsync(ctx, $"action=Cash.Adjustment&entityId={party}")).Lines);
        Assert.Equal("Party", adjusted.GetProperty("entityType").GetString());
        Assert.Equal(((string?)null, "-250"), Change(adjusted, "adjustment"));
        Assert.Equal("Écart constaté", adjusted.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Transfer_corrections_are_traced_but_not_creation_or_texts()
    {
        var ctx = await ArrangeAsync();
        var (id, code) = await ctx.CreateTransferAsync();
        using var texts = await ctx.Client.EditTransferTextsAsync(
            ctx.Token, id, new { version = await ctx.VersionAsync(id), senderInfo = "Autre expéditeur", beneficiaryInfo = "Bénéficiaire fictif" });
        var (afterTexts, _) = await ReadAsync(ctx, $"entityType=Transfer&entityId={id}");

        using var newCode = await ctx.Client.NewTransferCodeAsync(ctx.Token, id);
        var replacedCode = (await newCode.ReadJsonAsync()).GetProperty("withdrawalCode").GetString()!;
        using var edit = await ctx.Client.EditTransferAmountsAsync(ctx.Token, id, new
        {
            version = await ctx.VersionAsync(id),
            amountXof = 20_000m,
            payments = new[] { new { treasuryAccountId = ctx.NkcCash, amount = 14_600m } },
            reason = "Montant mal saisi",
        });
        using var cancel = await ctx.Client.CancelTransferAsync(ctx.Token, id, "Client parti");

        Assert.Equal(HttpStatusCode.NoContent, texts.StatusCode);
        Assert.Empty(afterTexts);
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        var (lines, text) = await ReadAsync(ctx, $"entityType=Transfer&entityId={id}");
        Assert.Equal(
            ["Transfers.Transfer.Cancelled", "Transfers.Transfer.AmountsEdited", "Transfers.Transfer.CodeReplaced"],
            lines.Select(l => l.GetProperty("action").GetString()));
        Assert.Equal("Client parti", lines[0].GetProperty("reason").GetString());
        Assert.Equal("Montant mal saisi", lines[1].GetProperty("reason").GetString());
        var amount = Change(lines[1], "AmountXof");
        Assert.StartsWith("10000", amount.Before, StringComparison.Ordinal);
        Assert.Equal("20000", amount.After);
        Assert.Equal(ctx.Nkc, lines[0].GetProperty("agencyId").GetGuid());
        Assert.DoesNotContain(code, text, StringComparison.Ordinal);
        Assert.DoesNotContain(replacedCode, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delivery_without_code_is_traced_without_the_id_document()
    {
        var ctx = await ArrangeAsync();
        var (id, _) = await ctx.CreateTransferAsync();

        using var deliver = await ctx.Client.DeliverWithoutCodeAsync(ctx.Token, id, new
        {
            idDocument = "Passeport 7654321",
            reason = "Code perdu, identité vérifiée",
            payments = new[] { new { treasuryAccountId = ctx.DkrCash, amount = 10_000m } },
        });

        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        var (lines, text) = await ReadAsync(ctx, $"entityType=Transfer&entityId={id}");
        var line = Assert.Single(lines);
        Assert.Equal("Transfers.Transfer.DeliveredWithoutCode", line.GetProperty("action").GetString());
        Assert.Equal(ctx.Dkr, line.GetProperty("agencyId").GetGuid());
        Assert.Equal("Code perdu, identité vérifiée", line.GetProperty("reason").GetString());
        Assert.DoesNotContain("7654321", text, StringComparison.Ordinal);
    }

    // --- Outils ------------------------------------------------------------

    private static (string? Before, string? After) Change(JsonElement line, string field)
    {
        var change = line.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == field);
        return (change.GetProperty("before").GetString(), change.GetProperty("after").GetString());
    }

    private static async Task<(List<JsonElement> Lines, string Text)> ReadAsync(Context ctx, string query)
    {
        using var response = await ctx.Client.SendGetAsync(ctx.Token, $"/api/v1/audit?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        return ([.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()], text);
    }

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

        var ctx = new Context(factory, client, token, nkc, dkr, new DateTimeOffset(day.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero));
        return ctx with { NkcCash = await ctx.CreateCashAsync(nkc), DkrCash = await ctx.CreateCashAsync(dkr) };
    }

    private sealed record Context(HawalaApiFactory Factory, HttpClient Client, string Token, Guid Nkc, Guid Dkr, DateTimeOffset OperationAt)
    {
        public Guid NkcCash { get; init; }

        public Guid DkrCash { get; init; }

        public async Task<Guid> CreateCashAsync(Guid agencyId)
        {
            using var response = await Client.CreateTreasuryAccountAsync(
                Token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<int> VersionAsync(Guid transferId)
        {
            using var response = await Client.GetTransferAsync(Token, transferId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("version").GetInt32();
        }

        public async Task<Guid> CreatePartyAsync()
        {
            using var response = await Client.CreatePartyAsync(Token, new { name = $"Tiers {Guid.NewGuid():N}"[..30], type = "Client" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        /// <summary>Transfert Nouakchott → Dakar de 10 000 FCFA, payé 7 300 MRO comptant.</summary>
        public async Task<(Guid Id, string Code)> CreateTransferAsync()
        {
            using var response = await Client.CreateTransferAsync(
                Token,
                new
                {
                    sourceAgencyId = Nkc,
                    destinationAgencyId = Dkr,
                    amountXof = 10_000m,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = OperationAt,
                    payments = new[] { new { treasuryAccountId = NkcCash, amount = 7_300m } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.ReadJsonAsync();
            return (created.GetProperty("id").GetGuid(), created.GetProperty("withdrawalCode").GetString()!);
        }
    }
}
