using Hawala.SharedKernel.Results;
using Hawala.SharedKernel.Text;

namespace Identity.Domain.Accounts;

/// <summary>
/// Numéro de téléphone d'un compte : facultatif, unique s'il est renseigné,
/// permet aussi de se connecter. Stocké au format international E.164 (+22222123456).
/// La règle de format est partagée avec les autres modules (<see cref="PhoneNumbers"/>) ;
/// les codes d'erreur, eux, restent ceux d'Identité (déjà publiés).
/// </summary>
public sealed record PhoneNumber
{
    private PhoneNumber(string value) => Value = value;

    /// <summary>Numéro E.164, ex. « +22222123456 ».</summary>
    public string Value { get; }

    /// <summary>
    /// Accepte les saisies courantes : espaces, tirets, points, parenthèses,
    /// et le préfixe « 00 » à la place de « + » (00222… → +222…).
    /// L'indicatif pays est obligatoire.
    /// </summary>
    public static Result<PhoneNumber> Create(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return IdentityErrors.Phone.Empty;
        }

        return PhoneNumbers.TryNormalizeE164(input, out var normalized)
            ? new PhoneNumber(normalized)
            : IdentityErrors.Phone.Invalid;
    }

    public override string ToString() => Value;
}
