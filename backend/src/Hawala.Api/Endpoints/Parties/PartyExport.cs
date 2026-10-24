using System.Security.Claims;
using Hawala.Api.Configuration;
using Hawala.Api.Errors;
using Hawala.Api.Exports;
using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Time;
using Organization.Contracts;
using Parties.Application.Features.Parties;
using Parties.Application.Features.Parties.ListParties;

namespace Hawala.Api.Endpoints.Parties;

/// <summary>
/// Export Excel des tiers (permission export), avec la requête de la liste : mêmes filtres, même périmètre (tiers autorisés
/// d'un compte limité) et soldes seulement avec parties.view_balance. Pour chaque agence visible : solde signé (positif =
/// il nous doit), sens en mots, plafond (vide = pas de plafond). Jamais le NNI (donnée d'identité, chiffrée au repos).
/// POINT OUVERT : NNI et adresse dans l'export, à confirmer par Hamade (absents pour l'instant).
/// </summary>
public static class PartyExport
{
    /// <param name="lang">« ar » pour les en-têtes en arabe ; français sinon.</param>
    public static async Task<IResult> ExportAsync(
        Guid? agencyId,
        string? search,
        string? type,
        string? balanceSheet,
        string? balance,
        bool? includeArchived,
        string? lang,
        ClaimsPrincipal user,
        IQueryHandler<ListPartiesQuery, PartyList> handler,
        IAgencyDirectory agencies,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!user.TryGetAccountId(out var actorId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await handler.Handle(
            new ListPartiesQuery(actorId, agencyId, search, type, balanceSheet, balance, includeArchived ?? false), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.ToProblem();
        }

        var items = result.Value.Items;
        if (items.Count > ExcelExport.MaxRows)
        {
            return ExcelExport.TooLarge.ToProblem();
        }

        var language = ExcelExport.ParseLanguage(lang);
        var visibleAgencies = items.SelectMany(p => p.Balances ?? []).Select(b => b.AgencyId).ToHashSet();
        var agencyColumns = (await agencies.GetAllAsync(cancellationToken))
            .Where(a => visibleAgencies.Contains(a.Id))
            .OrderBy(a => a.Code, StringComparer.Ordinal)
            .ToList();

        var content = ExcelExport.Build(language, "Tiers", "الأطراف", Columns(language, agencyColumns), items);
        var day = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        return ExcelExport.File(content, $"Tiers-{day}.xlsx");
    }

    private static List<ExportColumn<PartySummary>> Columns(ExportLanguage language, IReadOnlyList<AgencySummary> agencies)
    {
        var columns = new List<ExportColumn<PartySummary>>
        {
            new("Nom", "الاسم", p => p.Name, Width: 28),
            new("Type", "النوع", p => Type(language, p.Type), Width: 14),
            new("Compte comptable", "الحساب المحاسبي", p => p.AccountCode, Width: 10),
            new("Téléphone", "الهاتف", p => p.Phone, Width: 18),
            new("Dans le bilan", "ضمن الميزانية", p => YesNo(language, p.InBalanceSheet), Width: 10),
            new("Archivé", "مؤرشف", p => YesNo(language, p.IsArchived), Width: 10),
        };

        foreach (var agency in agencies)
        {
            var city = language.City(agency.Code, agency.Name);
            var unit = ExcelExport.Unit(agency.Currency);
            var format = agency.Currency == Hawala.SharedKernel.Money.Currency.XOF ? ExportFormat.Integer : ExportFormat.Amount;
            PartyAgencyBalance? Of(PartySummary p) => p.Balances?.FirstOrDefault(b => b.AgencyId == agency.Id);

            columns.Add(new($"Solde {city} ({unit})", $"الرصيد {city} ({unit})", p => Of(p)?.Balance, format, 18));
            columns.Add(new($"Sens {city}", $"الاتجاه {city}", p => Of(p) is { } b ? Direction(language, b.Balance) : null, Width: 16));
            columns.Add(new($"Plafond {city} ({unit})", $"سقف الآجل {city} ({unit})", p => Of(p)?.CreditLimit, format, 18));
        }

        columns.Add(new("Note", "ملاحظة", p => p.Note, Width: 28));
        return columns;
    }

    private static string Type(ExportLanguage language, string type) => type switch
    {
        "Client" => language.Pick("Client", "زبون"),
        "Supplier" => language.Pick("Fournisseur", "مورد"),
        "Staff" => language.Pick("Personnel", "موظف"),
        _ => language.Pick("Autre", "آخر"),
    };

    /// <summary>Positif = rouge (il nous doit) ; négatif = vert (on lui doit).</summary>
    private static string Direction(ExportLanguage language, decimal balance) =>
        balance > 0 ? language.Pick("Il nous doit", "عليه")
        : balance < 0 ? language.Pick("On lui doit", "له")
        : language.Pick("Soldé", "مسدد");

    private static string YesNo(ExportLanguage language, bool value) =>
        value ? language.Pick("Oui", "نعم") : language.Pick("Non", "لا");
}
