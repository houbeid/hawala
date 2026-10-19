using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;

namespace Parties.Domain.Parties;

/// <summary>
/// Téléphone d'un tiers, au format international E.164 : même règle que pour les comptes
/// (<see cref="PhoneNumbers"/>), codes d'erreur propres aux Tiers. Unique quand il est renseigné.
/// POINT OUVERT : stocké en clair ; chiffrement au repos + empreinte pour l'unicité et la recherche en phase 6.
/// </summary>
public sealed record PartyPhone
{
    private PartyPhone(string value) => Value = value;

    public string Value { get; }

    /// <summary>Une saisie vide n'est pas une erreur ici : le téléphone est facultatif (voir <see cref="Party"/>).</summary>
    public static Result<PartyPhone> Create(string input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return PhoneNumbers.TryNormalizeE164(input, out var normalized)
            ? new PartyPhone(normalized)
            : PartiesErrors.Phone.Invalid;
    }

    public override string ToString() => Value;
}
