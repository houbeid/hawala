using System.Globalization;
using Hawala.SharedKernel.Money;
using Organization.Contracts;
using Parties.Application.Abstractions;
using Parties.Domain.Parties;

namespace Parties.Application.Features.Import;

/// <summary>Une ligne prête à importer (après vérification).</summary>
internal sealed record ParsedPartyRow(
    int Row,
    string Name,
    PartyType Type,
    string? Phone,
    string? Nni,
    string? Address,
    string? Note,
    bool InBalanceSheet,
    IReadOnlyList<ParsedAgencyValues> Agencies);

/// <param name="Opening">Solde initial signé : positif = il nous doit (rouge), négatif = on lui doit (vert).</param>
internal sealed record ParsedAgencyValues(Guid AgencyId, string AgencyCode, decimal Opening, decimal? CreditLimit);

/// <summary>
/// Vérifie chaque ligne avec les règles d'un tiers (nom, type, téléphone, NNI : celles de la création), les montants dans la
/// devise de l'agence (FCFA entier, MRO à 2 décimales), le sens, le plafond, et l'unicité du téléphone et du NNI, dans le
/// fichier et dans la base. Une ligne entièrement vide est ignorée. Au plus 2 000 tiers par fichier.
/// </summary>
public sealed class PartyImportValidator(IPartyRepository parties, IAgencyDirectory agencies, Hawala.SharedKernel.Time.IClock clock)
{
    public const int MaxRows = 2_000;

    // Libellés acceptés (français, anglais, arabe), sans tenir compte de la casse ni des accents de saisie.
    private static readonly Dictionary<string, PartyType> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        ["client"] = PartyType.Client, ["عميل"] = PartyType.Client, ["زبون"] = PartyType.Client,
        ["fournisseur"] = PartyType.Supplier, ["supplier"] = PartyType.Supplier, ["مورد"] = PartyType.Supplier,
        ["personnel"] = PartyType.Staff, ["staff"] = PartyType.Staff, ["موظف"] = PartyType.Staff,
        ["autre"] = PartyType.Other, ["other"] = PartyType.Other, ["آخر"] = PartyType.Other,
    };

    private static readonly Dictionary<string, int> Directions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["il nous doit"] = 1, ["عليه"] = 1, ["débiteur"] = 1, ["debiteur"] = 1, ["rouge"] = 1,
        ["on lui doit"] = -1, ["له"] = -1, ["créditeur"] = -1, ["crediteur"] = -1, ["vert"] = -1,
    };

    private static readonly Dictionary<string, bool> YesNo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["oui"] = true, ["yes"] = true, ["نعم"] = true, ["1"] = true, ["vrai"] = true, ["true"] = true,
        ["non"] = false, ["no"] = false, ["لا"] = false, ["0"] = false, ["faux"] = false, ["false"] = false,
    };

    internal async Task<(PartyImportReport Report, IReadOnlyList<ParsedPartyRow> Rows)> ValidateAsync(
        IReadOnlyList<PartyImportRow> rows, CancellationToken cancellationToken)
    {
        var issues = new List<PartyImportIssue>();
        var filled = rows.Where(r => !IsEmpty(r)).ToList();
        if (filled.Count == 0)
        {
            return (Report(0, [], [new PartyImportIssue(0, null, "Parties.Import.Empty")]), []);
        }

        if (filled.Count > MaxRows)
        {
            return (Report(filled.Count, [], [new PartyImportIssue(0, null, "Parties.Import.TooManyRows")]), []);
        }

        var known = (await agencies.GetAllAsync(cancellationToken)).ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var phones = new Dictionary<string, int>(StringComparer.Ordinal);
        var nnis = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var parsed = new List<ParsedPartyRow>();

        foreach (var row in filled)
        {
            var before = issues.Count;
            void Problem(string? column, string code) => issues.Add(new PartyImportIssue(row.Row, column, code));

            if (!Types.TryGetValue((row.Type ?? string.Empty).Trim(), out var type))
            {
                Problem("type", "Parties.Import.TypeUnknown");
            }

            PartyPhone? phone = null;
            if (!string.IsNullOrWhiteSpace(row.Phone))
            {
                var parsedPhone = PartyPhone.Create(row.Phone);
                if (parsedPhone.IsFailure)
                {
                    Problem("phone", parsedPhone.Error.Code);
                }
                else
                {
                    phone = parsedPhone.Value;
                }
            }

            // Mêmes règles que la création d'un tiers (nom obligatoire, 30 caractères, NNI, adresse, note).
            var party = Party.Create(row.Name, type, phone, row.Nni, row.Address, row.Note, clock.UtcNow);
            if (party.IsFailure)
            {
                Problem(ColumnOf(party.Error.Code), party.Error.Code);
            }

            var inBalanceSheet = true;
            if (!string.IsNullOrWhiteSpace(row.InBalanceSheet) && !YesNo.TryGetValue(row.InBalanceSheet.Trim(), out inBalanceSheet))
            {
                Problem("inBalanceSheet", "Parties.Import.YesNoUnknown");
                inBalanceSheet = true;
            }

            var values = new List<ParsedAgencyValues>();
            foreach (var cells in row.Agencies)
            {
                if (!known.TryGetValue(cells.AgencyCode, out var agency))
                {
                    continue;
                }

                var opening = ReadOpening(cells, agency.Currency, out var openingIssue);
                if (openingIssue is not null)
                {
                    Problem($"{agency.Code}.{openingIssue.Value.Column}", openingIssue.Value.Code);
                }

                decimal? limit = null;
                if (!string.IsNullOrWhiteSpace(cells.CreditLimit))
                {
                    if (!TryReadAmount(cells.CreditLimit, out var value) || value < 0m)
                    {
                        Problem($"{agency.Code}.creditLimit", "Parties.Import.AmountInvalid");
                    }
                    else if (Money.Create(value, agency.Currency).IsFailure)
                    {
                        Problem($"{agency.Code}.creditLimit", "Parties.Import.AmountTooPrecise");
                    }
                    else
                    {
                        limit = value;
                    }
                }

                values.Add(new ParsedAgencyValues(agency.Id, agency.Code, opening, limit));
            }

            // Unicité dans le fichier, puis dans la base (la base est relue même si le fichier a déjà un doublon).
            if (phone is not null)
            {
                if (phones.ContainsKey(phone.Value))
                {
                    Problem("phone", "Parties.Import.PhoneDuplicateInFile");
                }
                else
                {
                    phones[phone.Value] = row.Row;
                    if (await parties.PhoneExistsAsync(phone, excludingPartyId: null, cancellationToken))
                    {
                        Problem("phone", "Parties.Party.PhoneTaken");
                    }
                }
            }

            if (party.IsSuccess && party.Value.Nni is { } nni)
            {
                if (nnis.ContainsKey(nni))
                {
                    Problem("nni", "Parties.Import.NniDuplicateInFile");
                }
                else
                {
                    nnis[nni] = row.Row;
                    if (await parties.NniExistsAsync(nni, excludingPartyId: null, cancellationToken))
                    {
                        Problem("nni", "Parties.Party.NniTaken");
                    }
                }
            }

            if (issues.Count == before && party.IsSuccess)
            {
                parsed.Add(new ParsedPartyRow(
                    row.Row, party.Value.Name, type, phone?.Value, party.Value.Nni, row.Address, row.Note, inBalanceSheet, values));
            }
        }

        return (Report(filled.Count, parsed, issues), parsed);
    }

    private static PartyImportReport Report(int rowCount, IReadOnlyList<ParsedPartyRow> parsed, IReadOnlyList<PartyImportIssue> issues) =>
        new(
            rowCount,
            parsed.Count,
            issues,
            parsed.SelectMany(r => r.Agencies)
                .GroupBy(a => a.AgencyCode)
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Opening), StringComparer.Ordinal));

    /// <summary>Solde signé : montant positif + sens, ou montant négatif sans sens ; vide = 0.</summary>
    private static decimal ReadOpening(PartyImportAgencyCells cells, Currency currency, out (string Column, string Code)? issue)
    {
        issue = null;
        if (string.IsNullOrWhiteSpace(cells.Balance))
        {
            return 0m;
        }

        if (!TryReadAmount(cells.Balance, out var amount))
        {
            issue = ("balance", "Parties.Import.AmountInvalid");
            return 0m;
        }

        if (Money.Create(Math.Abs(amount), currency).IsFailure)
        {
            issue = ("balance", "Parties.Import.AmountTooPrecise");
            return 0m;
        }

        if (string.IsNullOrWhiteSpace(cells.Direction))
        {
            if (amount > 0m)
            {
                issue = ("direction", "Parties.Import.DirectionRequired");
            }

            return amount;
        }

        if (!Directions.TryGetValue(cells.Direction.Trim(), out var sign))
        {
            issue = ("direction", "Parties.Import.DirectionUnknown");
            return 0m;
        }

        if (amount < 0m)
        {
            issue = ("balance", "Parties.Import.AmountInvalid");
            return 0m;
        }

        return sign * amount;
    }

    /// <summary>Nombre saisi en texte : espaces de groupe acceptés, virgule ou point décimal.</summary>
    internal static bool TryReadAmount(string text, out decimal value)
    {
        var cleaned = new string([.. text.Trim().Where(c => !char.IsWhiteSpace(c))]).Replace(',', '.');
        cleaned = cleaned.Replace('−', '-');
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsEmpty(PartyImportRow row) =>
        string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.Type) && string.IsNullOrWhiteSpace(row.Phone)
        && string.IsNullOrWhiteSpace(row.Nni) && string.IsNullOrWhiteSpace(row.Address) && string.IsNullOrWhiteSpace(row.Note)
        && row.Agencies.All(a => string.IsNullOrWhiteSpace(a.Balance) && string.IsNullOrWhiteSpace(a.CreditLimit));

    /// <summary>Colonne d'une erreur de la fiche (« Parties.Party.NameRequired » → name).</summary>
    private static string? ColumnOf(string code) => code switch
    {
        _ when code.Contains(".Name", StringComparison.Ordinal) => "name",
        _ when code.Contains("Nni", StringComparison.Ordinal) => "nni",
        _ when code.Contains("Address", StringComparison.Ordinal) => "address",
        _ when code.Contains("Note", StringComparison.Ordinal) => "note",
        _ when code.Contains("Type", StringComparison.Ordinal) => "type",
        _ => null,
    };
}
