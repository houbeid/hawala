using Hawala.SharedKernel.Results;

namespace Organization.Domain.Agencies;

/// <summary>
/// Couleur d'une agence, affichée dans l'interface (badges, en-têtes) pour distinguer
/// Nouakchott de Dakar d'un coup d'œil. Format #RRGGBB, stocké en majuscules.
/// </summary>
public sealed record AgencyColor
{
    private AgencyColor(string value) => Value = value;

    public string Value { get; }

    public static Result<AgencyColor> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return OrganizationErrors.Color.Invalid;
        }

        var value = input.Trim().ToUpperInvariant();

        var isHex = value.Length == 7
            && value[0] == '#'
            && value[1..].All(char.IsAsciiHexDigit);

        return isHex ? new AgencyColor(value) : OrganizationErrors.Color.Invalid;
    }

    public override string ToString() => Value;
}