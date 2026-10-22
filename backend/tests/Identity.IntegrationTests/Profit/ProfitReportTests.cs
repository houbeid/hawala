using System.Globalization;
using System.Net;
using System.Text.Json;
using Identity.Domain.Roles;
using Identity.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.IntegrationTests.Profit;

/// <summary>
/// Écran « Bénéfice » (proposition validée le 21 octobre 2026) : total de la période, une ligne par jour, détail par transfert ;
/// un transfert compte pour l'agence qui l'a envoyé ; annulés exclus ; Superviseur seulement.
/// Taux du jour : vente 3 650, achat 3 600. Formules validées :
///   Nouakchott → Dakar 5 000 000 FCFA : M = 3 650 000, B = M − F × T / 5000 = 3 650 000 − 3 600 000 = 50 000 MRO ;
///   Dakar → Nouakchott 1 000 000 FCFA : M = 730 000, B = F × T / 5000 − M = 720 000 − 730 000 = −10 000 MRO.
/// Chaque test travaille sur des jours tirés au hasard (1975-1995).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProfitReportTests(ApiFixture fixture)
{
    [Fact]
    public async Task Profit_of_a_day_by_sending_agency_and_in_total_without_cancelled_transfers()
    {
        var ctx = await ArrangeAsync();
        await ctx.TransferAsync(fromNkc: true, 5_000_000m);
        await ctx.TransferAsync(fromNkc: false, 1_000_000m);
        var cancelled = await ctx.TransferAsync(fromNkc: true, 5_000_000m);
        using (var cancel = await ctx.Client.CancelTransferAsync(ctx.SupervisorToken, cancelled, "Client a renoncé"))
        {
            Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        }

        var both = await ctx.ReportAsync(agency: null);
        var nkc = await ctx.ReportAsync(ctx.Nkc);
        var dkr = await ctx.ReportAsync(ctx.Dkr);

        Assert.Equal(40_000m, both.GetProperty("totalProfitMro").GetDecimal());
        Assert.Equal(2, both.GetProperty("transferCount").GetInt32());
        Assert.Equal(50_000m, nkc.GetProperty("totalProfitMro").GetDecimal());
        Assert.Equal(-10_000m, dkr.GetProperty("totalProfitMro").GetDecimal());

        var day = Assert.Single(both.GetProperty("days").EnumerateArray());
        Assert.Equal(ctx.DayText, day.GetProperty("date").GetString());
        Assert.Equal(2, day.GetProperty("transferCount").GetInt32());
        Assert.Equal(6_000_000m, day.GetProperty("amountXof").GetDecimal());
        Assert.Equal(3600m, day.GetProperty("purchaseRate").GetDecimal());
        Assert.Equal(40_000m, day.GetProperty("profitMro").GetDecimal());
    }

    [Fact]
    public async Task A_day_opens_on_its_transfers()
    {
        var ctx = await ArrangeAsync();
        var sent = await ctx.TransferAsync(fromNkc: true, 5_000_000m);

        using var response = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/profit/days/{ctx.DayText}?agencyId={ctx.Nkc}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.ReadJsonAsync();
        var line = Assert.Single(detail.GetProperty("transfers").EnumerateArray());
        Assert.Equal(sent, line.GetProperty("transferId").GetGuid());
        Assert.Equal(5_000_000m, line.GetProperty("amountXof").GetDecimal());
        Assert.Equal(3_650_000m, line.GetProperty("amountMro").GetDecimal());
        Assert.Equal(3650m, line.GetProperty("saleRate").GetDecimal());
        Assert.Equal(3600m, line.GetProperty("purchaseRate").GetDecimal());
        Assert.Equal(50_000m, line.GetProperty("profitMro").GetDecimal());
        Assert.Equal(50_000m, detail.GetProperty("totalProfitMro").GetDecimal());
    }

    [Fact]
    public async Task A_period_lists_one_line_per_day_most_recent_first()
    {
        var ctx = await ArrangeAsync();
        await ctx.TransferAsync(fromNkc: true, 5_000_000m);
        var nextDay = ctx.Day.AddDays(1);
        using (var rate = await ctx.Client.SetDailyRateAsync(ctx.SupervisorToken, nextDay, 3600m, 3650m))
        {
            Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);
        }

        await ctx.TransferAsync(fromNkc: true, 10_000_000m, nextDay);

        var report = await ctx.ReportAsync(ctx.Nkc, to: nextDay);

        Assert.Equal(150_000m, report.GetProperty("totalProfitMro").GetDecimal());
        Assert.Equal(
            [nextDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ctx.DayText],
            report.GetProperty("days").EnumerateArray().Select(d => d.GetProperty("date").GetString()));
    }

    [Fact]
    public async Task Profit_is_for_the_supervisor_only()
    {
        var ctx = await ArrangeAsync();

        using var byAgent = await ctx.Client.SendGetAsync(ctx.NkcAgentToken, "/api/v1/profit");
        using var badPeriod = await ctx.Client.SendGetAsync(ctx.SupervisorToken, "/api/v1/profit?from=2026-02-02&to=2026-02-01");
        using var unknownAgency = await ctx.Client.SendGetAsync(ctx.SupervisorToken, $"/api/v1/profit?agencyId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, byAgent.StatusCode);
        Assert.Equal("Profit.Filter.PeriodInvalid", await badPeriod.ReadErrorCodeAsync());
        Assert.Equal("Profit.Filter.AgencyNotFound", await unknownAgency.ReadErrorCodeAsync());
    }

    // --- Outils ------------------------------------------------------------

    private async Task<Context> ArrangeAsync()
    {
        var factory = await fixture.GetFactoryAsync();
        var client = factory.CreateApiClient();
        var supervisor = await TestAccounts.CreateAsync(factory, RoleTemplate.Supervisor);
        var (token, _) = await client.LoginOkAsync(supervisor.Email, supervisor.Password);

        await using var scope = factory.Services.CreateAsyncScope();
        var nkc = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "NKC");
        var dkr = await TestAccounts.GetAgencyIdAsync(scope.ServiceProvider, "DKR");

        var nkcAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "NKC");
        var dkrAgent = await TestAccounts.CreateAsync(factory, RoleTemplate.Agent, agencyCode: "DKR");
        var (nkcToken, _) = await client.LoginOkAsync(nkcAgent.Email, nkcAgent.Password);
        var (dkrToken, _) = await client.LoginOkAsync(dkrAgent.Email, dkrAgent.Password);

        var day = new DateOnly(1975, 1, 1).AddDays(Random.Shared.Next(0, 7_500));
        using var rate = await client.SetDailyRateAsync(token, day, 3600m, 3650m);
        Assert.Equal(HttpStatusCode.NoContent, rate.StatusCode);

        return new Context(
            client, token, nkcToken, dkrToken, nkc, dkr, day,
            await AccountAsync(client, token, nkc), await AccountAsync(client, token, dkr));
    }

    private static async Task<Guid> AccountAsync(HttpClient client, string token, Guid agencyId)
    {
        using var response = await client.CreateTreasuryAccountAsync(
            token, new { name = $"Caisse {Guid.NewGuid():N}"[..30], type = "cash", agencyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private sealed record Context(
        HttpClient Client,
        string SupervisorToken,
        string NkcAgentToken,
        string DkrAgentToken,
        Guid Nkc,
        Guid Dkr,
        DateOnly Day,
        Guid NkcCash,
        Guid DkrCash)
    {
        public string DayText => Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Transfert au taux du jour (vente 3 650), payé comptant dans la devise de l'agence d'envoi.</summary>
        public async Task<Guid> TransferAsync(bool fromNkc, decimal amountXof, DateOnly? day = null)
        {
            var paid = fromNkc ? amountXof * 3650m / 5000m : amountXof;
            using var response = await Client.CreateTransferAsync(
                fromNkc ? NkcAgentToken : DkrAgentToken,
                new
                {
                    destinationAgencyId = fromNkc ? Dkr : Nkc,
                    amountXof,
                    senderInfo = "Expéditeur fictif",
                    beneficiaryInfo = "Bénéficiaire fictif",
                    operationAt = new DateTimeOffset((day ?? Day).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero),
                    payments = new[] { new { treasuryAccountId = fromNkc ? NkcCash : DkrCash, amount = paid } },
                },
                Guid.NewGuid().ToString("N"));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<JsonElement> ReportAsync(Guid? agency, DateOnly? to = null)
        {
            var toText = (to ?? Day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var agencyQuery = agency is { } id ? $"&agencyId={id}" : string.Empty;
            using var response = await Client.SendGetAsync(SupervisorToken, $"/api/v1/profit?from={DayText}&to={toText}{agencyQuery}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
    }
}
